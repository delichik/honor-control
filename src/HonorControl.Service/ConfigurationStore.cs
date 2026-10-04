using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using HonorControl.Contracts;

namespace HonorControl.Service;

internal sealed class ConfigurationStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HonorControl");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "service.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object sync = new();

    public ConfigurationStore() => EnsurePrivateDirectory();

    public bool AuthorizeOrEnroll(string? sid, int clientSession, int activeConsoleSession)
    {
        if (sid == null) return false;
        lock (sync)
        {
            ServiceDocument document = ReadDocument();
            if (document.OwnerSid == null)
            {
                if (clientSession < 0 || activeConsoleSession < 0 || clientSession != activeConsoleSession) return false;
                WriteDocument(document with { OwnerSid = sid });
                return true;
            }
            return string.Equals(document.OwnerSid, sid, StringComparison.OrdinalIgnoreCase);
        }
    }

    public DesiredConfiguration Load()
    {
        lock (sync) return ReadDocument().Desired;
    }

    /// <summary>托盘策略。托盘进程由服务或面板按它拉起，见 TrayPolicyMode 的说明。</summary>
    public TrayPolicyMode LoadTrayPolicy()
    {
        lock (sync) return ReadDocument().TrayPolicy;
    }

    /// <summary>配置拥有者的 SID；尚未注册时返回 null（托盘只在拥有者的会话里出现）。</summary>
    public string? LoadOwnerSid()
    {
        lock (sync) return ReadDocument().OwnerSid;
    }

    public TrayPolicyMode UpdateTrayPolicy(TrayPolicyMode policy)
    {
        lock (sync)
        {
            ServiceDocument document = ReadDocument();
            WriteDocument(document with { TrayPolicy = policy });
            return policy;
        }
    }

    /// <summary>
    /// 判断某个 SID 是否已经是配置拥有者——**不会**注册新拥有者。
    /// 用于那些"只有拥有者能做、但不能顺手把调用方变成拥有者"的操作（例如停止服务）。
    /// </summary>
    public bool IsOwner(string? sid)
    {
        if (sid == null) return false;
        lock (sync) return string.Equals(ReadDocument().OwnerSid, sid, StringComparison.OrdinalIgnoreCase);
    }

    public DesiredConfiguration Update(Func<DesiredConfiguration, DesiredConfiguration> update)
    {
        lock (sync)
        {
            ServiceDocument document = ReadDocument();
            DesiredConfiguration desired = update(document.Desired);
            Validate(desired);
            WriteDocument(document with { Desired = desired });
            return desired;
        }
    }

    private ServiceDocument ReadDocument()
    {
        if (!File.Exists(FilePath)) return new ServiceDocument();
        if ((File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置不能是重解析点。");
        return JsonSerializer.Deserialize<ServiceDocument>(File.ReadAllText(FilePath, Encoding.UTF8))
            ?? throw new InvalidDataException("服务配置为空。");
    }

    private void WriteDocument(ServiceDocument document)
    {
        EnsurePrivateDirectory();
        string temporaryPath = FilePath + ".tmp";
        if (File.Exists(temporaryPath) && (File.GetAttributes(temporaryPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置临时文件不能是重解析点。");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, FilePath, true);
    }

    private static void Validate(DesiredConfiguration desired)
    {
        if ((desired.ChargeStart.HasValue || desired.ChargeEnd.HasValue)
            && (!desired.ChargeStart.HasValue || !desired.ChargeEnd.HasValue
                || desired.ChargeStart.Value < 0 || desired.ChargeStart.Value > 100
                || desired.ChargeEnd.Value < 1 || desired.ChargeEnd.Value > 100
                || desired.ChargeStart.Value >= desired.ChargeEnd.Value))
            throw new ArgumentOutOfRangeException(nameof(desired), "充电阈值必须满足 0 <= start < end <= 100。");
        if (desired.PerformanceMode is not (null or 1 or 2))
            throw new ArgumentOutOfRangeException(nameof(desired), "性能模式必须是智能或高能。");
    }

    private static void EnsurePrivateDirectory()
    {
        DirectoryInfo directory = Directory.CreateDirectory(DirectoryPath);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("服务配置目录不能是重解析点。");
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private sealed record ServiceDocument
    {
        public string? OwnerSid { get; init; }
        public DesiredConfiguration Desired { get; init; } = new();

        /// <summary>
        /// 托盘策略。默认 OnDemand：安装后还没有拥有者（归属由面板首次通信注册），
        /// 这时如果默认 Always，服务会去拉起一个读不到任何策略的托盘进程，语义不成立。
        /// </summary>
        public TrayPolicyMode TrayPolicy { get; init; } = TrayPolicyMode.OnDemand;
    }
}
