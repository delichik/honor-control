namespace HonorControl.Models
{
    public sealed class SystemPowerSnapshot
    {
        public SystemPowerSnapshot(bool? isOnAcPower, int? batteryPercent)
        {
            IsOnAcPower = isOnAcPower;
            BatteryPercent = batteryPercent;
        }

        public bool? IsOnAcPower { get; private set; }
        public int? BatteryPercent { get; private set; }
    }
}
