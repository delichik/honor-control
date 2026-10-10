using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using HonorControl.Contracts;

namespace HonorControl.Services;

/// <summary>Standard Windows plans/settings, independent of HONOR firmware performance modes.</summary>
public sealed class WindowsPowerSettingsService
{
    private readonly object sync = new();
    private readonly IWindowsPowerApi api;
    private readonly IWindowsPowerJournalStore store;
    private static readonly Guid Display = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid DisplayTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid Sleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid SleepTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    private static readonly Guid Intel = new("44f3beca-a7c0-460e-9df2-bb8b99e0cba6");
    private static readonly Guid IntelPlan = new("3619c3f2-afb2-4afc-b0e9-e7fef372de36");
    private static readonly Guid Disk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid DiskTimeout = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");
    private static readonly PowerSetting[] Settings =
    [
        new(nameof(WindowsPowerSettingsUpdate.AcDisplayTimeoutSeconds), Display, DisplayTimeout, true),
        new(nameof(WindowsPowerSettingsUpdate.DcDisplayTimeoutSeconds), Display, DisplayTimeout, false),
        new(nameof(WindowsPowerSettingsUpdate.AcSleepTimeoutSeconds), Sleep, SleepTimeout, true),
        new(nameof(WindowsPowerSettingsUpdate.DcSleepTimeoutSeconds), Sleep, SleepTimeout, false),
        new(nameof(WindowsPowerSettingsUpdate.AcIntelGraphicsPowerPlan), Intel, IntelPlan, true, IsIntelPolicy: true),
        new(nameof(WindowsPowerSettingsUpdate.DcIntelGraphicsPowerPlan), Intel, IntelPlan, false, IsIntelPolicy: true),
        new(nameof(WindowsPowerSettingsUpdate.AcDiskTimeoutSeconds), Disk, DiskTimeout, true),
        new(nameof(WindowsPowerSettingsUpdate.DcDiskTimeoutSeconds), Disk, DiskTimeout, false)
    ];

    public WindowsPowerSettingsService() : this(new WindowsPowerApi(), new WindowsPowerJournalStore()) { }
    internal WindowsPowerSettingsService(IWindowsPowerApi api, IWindowsPowerJournalStore store)
    {
        this.api = api;
        this.store = store;
    }

    public WindowsPowerSettingsSnapshot Get()
    {
        lock (sync) return ReadSnapshot();
    }

    public WindowsPowerSettingsSnapshot Set(WindowsPowerSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Validate(update);
        lock (sync)
        {
            Guid active = api.GetActive();
            if (update.ExpectedActiveSchemeId != null && Guid.Parse(update.ExpectedActiveSchemeId) != active)
                throw new InvalidOperationException("Windows 电源方案已被其他程序切换，请刷新后重试。");
            Guid target = update.SchemeId == null ? active : Guid.Parse(update.SchemeId);
            if (!api.Enumerate().Any(s => Guid.Parse(s.Id) == target))
                throw new InvalidOperationException("所选 Windows 电源方案不存在。");

            PowerJournal? previous = store.Load();
            List<PowerChange> requested = [];
            uint?[] values = Values(update);
            for (int i = 0; i < Settings.Length; i++)
            {
                if (values[i] is not uint value) continue;
                uint original = api.Read(target, Settings[i]); // Unsupported settings must never be written.
                if (Settings[i].IsIntelPolicy && original > 2)
                    throw new InvalidOperationException("Intel 显卡电源策略返回未知枚举，不能安全修改。");
                if (original != value) requested.Add(new(target, Settings[i].Name, original, value));
            }
            if (requested.Count == 0 && active == target) return ReadSnapshot();

            List<PowerChange> saved = previous?.Changes.ToList() ?? [];
            foreach (PowerChange change in requested)
            {
                int index = saved.FindIndex(c => c.Scheme == change.Scheme && c.Setting == change.Setting);
                if (index < 0) saved.Add(change);
                else saved[index] = saved[index] with { Applied = change.Applied };
            }
            bool switched = active != target;
            Guid? originalActive = previous?.OriginalActive ?? (switched ? active : null);
            PowerJournal journal = new(originalActive, originalActive.HasValue ? target : null, saved);
            // Persist before the first write, so a service interruption does not lose recovery data.
            store.Save(journal);
            List<PowerChange> attempted = [];
            bool activationAttempted = false;
            try
            {
                foreach (PowerChange change in requested)
                {
                    attempted.Add(change);
                    WriteVerified(change.Scheme, Find(change.Setting), change.Applied);
                }
                // Re-activate to apply edited settings, including when the plan is unchanged.
                activationAttempted = true;
                api.SetActive(target);
                if (api.GetActive() != target) throw new InvalidOperationException("电源方案切换回读不一致。");
                foreach (PowerChange change in requested)
                    if (api.Read(change.Scheme, Find(change.Setting)) != change.Applied)
                        throw new InvalidOperationException($"{change.Setting} 应用后回读不一致。");
                return ReadSnapshot();
            }
            catch (Exception error)
            {
                List<string> failures = RollBack(attempted, activationAttempted ? active : null);
                if (failures.Count == 0)
                {
                    try { store.Save(previous); }
                    catch (Exception e) { failures.Add("恢复备份文件：" + e.Message); }
                }
                throw new InvalidOperationException(failures.Count == 0
                    ? $"Windows 电源设置失败，已恢复本次修改前的值：{error.Message}"
                    : $"Windows 电源设置失败；部分恢复失败，保留恢复备份：{error.Message}；{string.Join("；", failures)}", error);
            }
        }
    }

    public WindowsPowerSettingsSnapshot Restore()
    {
        lock (sync)
        {
            PowerJournal? journal = store.Load();
            if (journal == null) return ReadSnapshot();
            Guid active = api.GetActive();
            if (journal.OriginalActive.HasValue && active != journal.LastActive && active != journal.OriginalActive)
                throw new InvalidOperationException("当前方案已被其他程序切换，不能覆盖外部修改。请先选择本工具最后使用的方案再恢复。");
            List<PowerChange> undo = [];
            // Check all fields before any writes; do not undo another application's later changes.
            foreach (PowerChange saved in journal.Changes)
            {
                uint current = api.Read(saved.Scheme, Find(saved.Setting));
                if (current == saved.Original) continue;
                if (current != saved.Applied)
                    throw new InvalidOperationException($"{saved.Setting} 已被其他程序修改，不能覆盖外部值。恢复备份已保留。");
                undo.Add(saved with { Original = current, Applied = saved.Original });
            }
            List<PowerChange> attempted = [];
            bool activationAttempted = false;
            try
            {
                foreach (PowerChange change in undo)
                {
                    attempted.Add(change);
                    WriteVerified(change.Scheme, Find(change.Setting), change.Applied);
                }
                Guid target = journal.OriginalActive ?? active;
                activationAttempted = true;
                api.SetActive(target);
                if (api.GetActive() != target) throw new InvalidOperationException("恢复电源方案回读不一致。");
                store.Save(null);
                return ReadSnapshot();
            }
            catch (Exception error)
            {
                List<string> failures = RollBack(attempted, activationAttempted ? active : null);
                throw new InvalidOperationException($"恢复 Windows 电源设置失败，备份已保留：{error.Message}"
                    + (failures.Count == 0 ? "" : "；回滚失败：" + string.Join("；", failures)), error);
            }
        }
    }

    public static void Validate(WindowsPowerSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.SchemeId != null && !Guid.TryParse(update.SchemeId, out _)
            || update.ExpectedActiveSchemeId != null && !Guid.TryParse(update.ExpectedActiveSchemeId, out _))
            throw new ArgumentException("Windows 电源方案 ID 无效。");
        if (new[] { update.AcDisplayTimeoutSeconds, update.DcDisplayTimeoutSeconds,
            update.AcSleepTimeoutSeconds, update.DcSleepTimeoutSeconds,
            update.AcDiskTimeoutSeconds, update.DcDiskTimeoutSeconds }.Any(v => v > 86400))
            throw new ArgumentOutOfRangeException(nameof(update), "关屏、睡眠和硬盘空闲时间须为 0–86400 秒；0 表示永不。");
        if (update.AcIntelGraphicsPowerPlan is not (null or 0 or 1 or 2)
            || update.DcIntelGraphicsPowerPlan is not (null or 0 or 1 or 2))
            throw new ArgumentOutOfRangeException(nameof(update), "Intel 显卡策略仅支持 0（省电）、1（平衡）、2（性能）。");
    }

    private static uint?[] Values(WindowsPowerSettingsUpdate u) =>
    [u.AcDisplayTimeoutSeconds, u.DcDisplayTimeoutSeconds, u.AcSleepTimeoutSeconds,
        u.DcSleepTimeoutSeconds, (uint?)u.AcIntelGraphicsPowerPlan, (uint?)u.DcIntelGraphicsPowerPlan,
        u.AcDiskTimeoutSeconds, u.DcDiskTimeoutSeconds];
    private static PowerSetting Find(string name) => Settings.First(s => s.Name == name);
    private void WriteVerified(Guid scheme, PowerSetting setting, uint value)
    {
        api.Write(scheme, setting, value);
        if (api.Read(scheme, setting) != value)
            throw new InvalidOperationException($"{setting.Name} 写入回读不一致。");
    }

    private List<string> RollBack(List<PowerChange> changes, Guid? active)
    {
        List<string> failures = [];
        foreach (PowerChange change in changes.AsEnumerable().Reverse())
        {
            try { WriteVerified(change.Scheme, Find(change.Setting), change.Original); }
            catch (Exception e) { failures.Add(change.Setting + "：" + e.Message); }
        }
        if (active.HasValue)
        {
            try
            {
                api.SetActive(active.Value);
                if (api.GetActive() != active.Value) throw new InvalidOperationException("方案回读不一致。");
            }
            catch (Exception e) { failures.Add("恢复活动方案：" + e.Message); }
        }
        return failures;
    }

    private WindowsPowerSettingsSnapshot ReadSnapshot()
    {
        Dictionary<string, string> reasons = [];
        Guid? active = null;
        IReadOnlyList<WindowsPowerScheme> schemes = [];
        try { active = api.GetActive(); }
        catch (Exception e) { reasons[nameof(WindowsPowerSettingsSnapshot.ActiveSchemeId)] = e.Message; }
        try { schemes = api.Enumerate(); }
        catch (Exception e) { reasons[nameof(WindowsPowerSettingsSnapshot.Schemes)] = e.Message; }
        uint?[] values = new uint?[Settings.Length];
        for (int i = 0; i < Settings.Length; i++)
        {
            if (!active.HasValue) { reasons[Settings[i].Name] = "当前方案不可读。"; continue; }
            try
            {
                uint value = api.Read(active.Value, Settings[i]);
                if (Settings[i].IsIntelPolicy && value > 2) throw new InvalidOperationException("Intel 显卡策略未知枚举。" );
                values[i] = value;
            }
            catch (Exception e) { reasons[Settings[i].Name] = e.Message; }
        }
        bool canRestore = false;
        try { canRestore = store.Load() != null; }
        catch (Exception e) { reasons[nameof(WindowsPowerSettingsSnapshot.CanRestore)] = e.Message; }
        return new(active?.ToString(), schemes, values[0], values[1], values[2], values[3],
            (int?)values[4], (int?)values[5], canRestore, reasons, values[6], values[7]);
    }
}

internal sealed record PowerSetting(string Name, Guid Subgroup, Guid Setting, bool Ac, bool IsIntelPolicy = false);
internal sealed record PowerChange(Guid Scheme, string Setting, uint Original, uint Applied);
internal sealed record PowerJournal(Guid? OriginalActive, Guid? LastActive, IReadOnlyList<PowerChange> Changes);
internal interface IWindowsPowerJournalStore
{
    PowerJournal? Load();
    void Save(PowerJournal? journal);
}
internal interface IWindowsPowerApi
{
    Guid GetActive();
    IReadOnlyList<WindowsPowerScheme> Enumerate();
    uint Read(Guid scheme, PowerSetting setting);
    void Write(Guid scheme, PowerSetting setting, uint value);
    void SetActive(Guid scheme);
}

internal sealed class WindowsPowerJournalStore : IWindowsPowerJournalStore
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonorControl");
    private string FilePath => Path.Combine(directory, "windows-power-backup.json");
    public PowerJournal? Load()
    {
        if (Directory.Exists(directory)) RejectReparse(directory);
        if (!File.Exists(FilePath)) return null;
        RejectReparse(FilePath);
        PowerJournal journal = JsonSerializer.Deserialize<PowerJournal>(File.ReadAllText(FilePath, Encoding.UTF8))
            ?? throw new InvalidDataException("Windows 电源恢复备份为空。");
        if (journal.Changes == null || journal.Changes.Count > 4096 || journal.Changes.Any(c => c == null))
            throw new InvalidDataException("Windows 电源恢复备份无效。");
        return journal;
    }
    public void Save(PowerJournal? journal)
    {
        DirectoryInfo info = Directory.CreateDirectory(directory);
        RejectReparse(directory);
        DirectorySecurity acl = new();
        acl.SetAccessRuleProtection(true, false);
        foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
        if (File.Exists(FilePath)) RejectReparse(FilePath);
        if (journal == null) { File.Delete(FilePath); return; }
        string temporary = FilePath + ".tmp";
        if (File.Exists(temporary)) RejectReparse(temporary);
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal), new UTF8Encoding(false));
        File.Move(temporary, FilePath, true);
    }
    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Windows 电源恢复备份路径不能是重解析点。");
    }
}

internal sealed class WindowsPowerApi : IWindowsPowerApi
{
    public Guid GetActive()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer), "读取当前方案");
        if (pointer == IntPtr.Zero) throw new InvalidDataException("Windows 返回空的当前方案。");
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public IReadOnlyList<WindowsPowerScheme> Enumerate()
    {
        List<WindowsPowerScheme> schemes = [];
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, out Guid id, ref size);
            if (result == 259) break;
            Check(result, "枚举方案");
            if (size != 16) throw new InvalidDataException("Windows 返回无效的方案 ID 长度。");
            uint nameSize = 0;
            result = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref nameSize);
            if (result != 0 && result != 234) Check(result, "读取方案名称");
            if (nameSize < 2 || nameSize > 65536 || nameSize % 2 != 0)
                throw new InvalidDataException("Windows 返回无效的方案名称长度。");
            IntPtr buffer = Marshal.AllocHGlobal((int)nameSize);
            try
            {
                Check(PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref nameSize), "读取方案名称");
                schemes.Add(new(id.ToString(), Marshal.PtrToStringUni(buffer) ?? id.ToString()));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return schemes;
    }
    public uint Read(Guid scheme, PowerSetting setting)
    {
        Guid group = setting.Subgroup, id = setting.Setting;
        uint value;
        uint result = setting.Ac
            ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, out value);
        Check(result, "读取 " + setting.Name);
        return value;
    }
    public void Write(Guid scheme, PowerSetting setting, uint value)
    {
        Guid group = setting.Subgroup, id = setting.Setting;
        Check(setting.Ac ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, value)
            : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref id, value), "写入 " + setting.Name);
    }
    public void SetActive(Guid scheme) => Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "激活方案");
    private static void Check(uint result, string action)
    {
        if (result != 0) throw new Win32Exception((int)result, $"Windows 电源设置{action}失败（{result}）；可能不受设备支持。");
    }
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr group, IntPtr setting, uint access, uint index, out Guid buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr group, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid group, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
