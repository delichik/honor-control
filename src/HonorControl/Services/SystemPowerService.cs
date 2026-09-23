using System.Runtime.InteropServices;
using HonorControl.Models;

namespace HonorControl.Services;

public sealed class SystemPowerService
{
    public SystemPowerSnapshot GetSnapshot()
    {
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
        {
            return new SystemPowerSnapshot(null, null);
        }

        bool? isOnAcPower = status.ACLineStatus == 1 ? true : status.ACLineStatus == 0 ? false : null;
        int? batteryPercent = status.BatteryLifePercent == 255 ? null : status.BatteryLifePercent;
        return new SystemPowerSnapshot(isOnAcPower, batteryPercent);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

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
}
