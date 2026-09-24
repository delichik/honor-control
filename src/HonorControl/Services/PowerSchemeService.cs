using System.ComponentModel;
using System.Runtime.InteropServices;
using HonorControl.Models;

namespace HonorControl.Services;

public sealed class PowerSchemeService
{
    public static readonly Guid BalancedSchemeId = new("381B4222-F694-41F0-9685-FF5BB260DF2E");
    public static readonly Guid HonorPerformanceSchemeId = new("B8A2C9F4-7D3E-4A1B-9C2F-5E8D6A3B1C4F");

    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;

    public PowerSchemeStatus GetStatus()
    {
        PowerSchemeInfo active = GetActiveScheme();
        PowerSchemeInfo? balanced = TryGetScheme(BalancedSchemeId);
        PowerSchemeInfo? honorPerformance = TryGetScheme(HonorPerformanceSchemeId);
        return new PowerSchemeStatus(active, balanced, honorPerformance);
    }

    public PowerSchemeInfo SetActiveForMode(int mode)
    {
        Guid target = mode switch
        {
            1 => BalancedSchemeId,
            2 => HonorPerformanceSchemeId,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "性能模式必须是智能或高能。")
        };
        return SetActive(target);
    }

    public PowerSchemeInfo SetActive(Guid schemeId)
    {
        PowerSchemeInfo target = GetScheme(schemeId);
        Guid mutableId = schemeId;
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref mutableId);
        if (result != ErrorSuccess)
            throw new Win32Exception((int)result, $"无法切换到 Windows 电源方案“{target.Name}”。");

        PowerSchemeInfo active = GetActiveScheme();
        if (active.Id != schemeId)
            throw new InvalidOperationException($"Windows 电源方案验证失败：请求 {schemeId}，当前为 {active.Id}。");
        return active;
    }

    private static PowerSchemeInfo GetActiveScheme()
    {
        uint result = PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePointer);
        if (result != ErrorSuccess || activePointer == IntPtr.Zero)
            throw new Win32Exception((int)result, "无法读取当前 Windows 电源方案。");

        try
        {
            Guid id = Marshal.PtrToStructure<Guid>(activePointer);
            return GetScheme(id);
        }
        finally
        {
            LocalFree(activePointer);
        }
    }

    private static PowerSchemeInfo? TryGetScheme(Guid id)
    {
        try { return GetScheme(id); }
        catch (Win32Exception) { return null; }
    }

    private static PowerSchemeInfo GetScheme(Guid id)
    {
        Guid mutableId = id;
        uint size = 0;
        uint result = PowerReadFriendlyName(IntPtr.Zero, ref mutableId, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (result != ErrorMoreData || size < sizeof(char))
            throw new Win32Exception((int)result, $"Windows 电源方案 {id} 不存在或无法读取。");

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            result = PowerReadFriendlyName(IntPtr.Zero, ref mutableId, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (result != ErrorSuccess)
                throw new Win32Exception((int)result, $"无法读取 Windows 电源方案 {id} 的名称。");
            string? name = Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
            return new PowerSchemeInfo(id, string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
