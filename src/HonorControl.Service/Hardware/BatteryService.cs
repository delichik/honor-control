using System.Runtime.InteropServices;

namespace HonorControl.Service.Hardware;

/// <summary>
/// 电池读数快照。
/// <para>
/// 电量百分比与功率都在这里；<see cref="PowerW"/> 是**带符号**的：正=充入、负=放出。
/// </para>
/// </summary>
public sealed record BatteryReading(
    bool Present,
    bool PluggedIn,
    bool Charging,
    bool Discharging,
    int? Percent,
    double? PowerW,
    double? RemainingWh,
    double? FullChargeWh);

/// <summary>
/// 电池数据源。
///
/// 数据来源是 <c>CallNtPowerInformation(SystemBatteryState)</c>：一次调用拿到电量、
/// 充放标志与**带符号**功率，没有 CIM 会话开销，适合 1 Hz 采样。
///
/// 为什么不用 <c>root\wmi:BatteryStatus</c>：它在真机上与这里返回的是同一组数值
/// （实测 ChargeRate 79917 mW 与 Rate 完全相同），但要走 CIM，慢且需要额外会话。
/// WMI 只在读循环次数时用一次（见 <see cref="GetCycleCount"/>）。
///
/// 已在 HONOR BCC-N（Windows 11 build 26200）上实测：
/// Rate=79917 mW（充电中，正值）、MaxCapacity=92041 mWh、RemainingCapacity=39100 mWh。
/// 注意 <c>MaxCapacity</c> 是**当前满充容量**而不是设计容量——设计容量在本机没有可用来源，
/// 因此健康度只能报 null，不能拿 MaxCapacity 硬算。
/// </summary>
public sealed class BatteryService
{
    /// <summary>SYSTEM_BATTERY_STATE 的信息级别。</summary>
    private const int BatteryStateInformationLevel = 5;

    private const int UnknownPercent = 255;

    public BatteryReading Read()
    {
        bool pluggedIn = false;
        int? percent = null;

        if (GetSystemPowerStatus(out SystemPowerStatus status))
        {
            pluggedIn = status.ACLineStatus == 1;
            percent = status.BatteryLifePercent == UnknownPercent ? null : status.BatteryLifePercent;
        }

        int size = Marshal.SizeOf<SystemBatteryState>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint result = CallNtPowerInformation(BatteryStateInformationLevel, IntPtr.Zero, 0, buffer, (uint)size);
            if (result != 0)
            {
                // 拿不到就如实返回"只有插电状态"，不猜功率。
                return new BatteryReading(true, pluggedIn, false, false, percent, null, null, null);
            }

            SystemBatteryState state = Marshal.PtrToStructure<SystemBatteryState>(buffer);
            bool present = state.BatteryPresent != 0;
            bool charging = state.Charging != 0;
            bool discharging = state.Discharging != 0;

            // Rate 是有符号毫瓦：正=充入、负=放出。AppContainer/固件异常时可能给出荒谬值，
            // 因此按额定容量做一个粗校验（>1000 W 视为无效），宁可报 null 也不报错值。
            double? powerW = null;
            int rate = unchecked((int)state.Rate);
            if (present && Math.Abs(rate) <= 1_000_000)
            {
                powerW = rate / 1000.0;
            }

            double? remainingWh = state.RemainingCapacity > 0 ? state.RemainingCapacity / 1000.0 : null;
            double? fullWh = state.MaxCapacity > 0 ? state.MaxCapacity / 1000.0 : null;

            // 电量优先用容量比（比 Win32 的整数百分比精细），退化时用 Win32 的值。
            if (remainingWh.HasValue && fullWh.HasValue && fullWh.Value > 0)
            {
                percent = (int)Math.Round(remainingWh.Value * 100 / fullWh.Value);
            }

            return new BatteryReading(present, pluggedIn || state.AcOnLine != 0, charging, discharging, percent, powerW, remainingWh, fullWh);
        }
        catch
        {
            return new BatteryReading(false, pluggedIn, false, false, percent, null, null, null);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 循环次数。走 WMI 且变化极慢，调用方应缓存（采样器每 10 分钟读一次）。
    /// 本机实测可用；机型不支持时返回 null。
    /// </summary>
    public int? GetCycleCount()
    {
        try
        {
            using Microsoft.Management.Infrastructure.CimSession session =
                Microsoft.Management.Infrastructure.CimSession.Create(null);
            foreach (Microsoft.Management.Infrastructure.CimInstance instance in
                session.QueryInstances(@"root\wmi", "WQL", "SELECT CycleCount FROM BatteryCycleCount"))
            {
                object? value = instance.CimInstanceProperties["CycleCount"]?.Value;
                if (value != null) return Convert.ToInt32(value);
            }
        }
        catch
        {
            // 机型不支持就算了：这是展示性数据，不影响任何控制逻辑。
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, uint inputBufferSize, IntPtr outputBuffer, uint outputBufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemBatteryState
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public byte Spare1;
        public byte Spare2;
        public byte Spare3;
        public byte Spare4;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public uint Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }
}
