using HonorControl.Contracts;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service.Hardware;

internal sealed record HardwareSensorSnapshot(
    double? BatteryTemperatureC,
    double? BatteryDesignCapacityWh,
    double? BatteryFullChargeCapacityWh,
    IReadOnlyList<SensorReading> Sensors,
    IReadOnlyList<FanReading> Fans)
{
    public static HardwareSensorSnapshot Empty { get; } = new(null, null, null,
        Array.Empty<SensorReading>(), Array.Empty<FanReading>());
}

/// <summary>
/// Reads hardware sensors from the privileged service process. The panel only sees
/// the cached, serialized result over the existing named pipe.
/// </summary>
internal sealed class HardwareSensorReader : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private readonly ILogger logger;
    private Computer? computer;
    private DateTimeOffset retryAfter;
    private bool disposed;

    public HardwareSensorReader(ILogger logger) => this.logger = logger;

    public HardwareSensorSnapshot Read()
    {
        if (disposed || !EnsureOpen()) return HardwareSensorSnapshot.Empty;

        try
        {
            UpdateVisitor visitor = new(logger);
            computer!.Accept(visitor);
            return ReadSensors(computer.Hardware, visitor.FailedHardwareIds);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "LibreHardwareMonitor 传感器采样失败");
            return HardwareSensorSnapshot.Empty;
        }
    }

    private bool EnsureOpen()
    {
        if (computer is not null) return true;
        if (DateTimeOffset.UtcNow < retryAfter) return false;

        Computer candidate = new()
        {
            IsBatteryEnabled = true,
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
        };

        try
        {
            candidate.Open();
            computer = candidate;
            logger.LogInformation("LibreHardwareMonitor 已在后台服务中启动");
            return true;
        }
        catch (Exception exception)
        {
            try { candidate.Close(); }
            catch { }
            retryAfter = DateTimeOffset.UtcNow + RetryDelay;
            logger.LogWarning(exception, "LibreHardwareMonitor 无法打开硬件传感器；一分钟后重试");
            return false;
        }
    }

    private static HardwareSensorSnapshot ReadSensors(
        IEnumerable<IHardware> hardwareItems,
        IReadOnlySet<string> failedHardwareIds)
    {
        List<SensorReading> temperatures = new();
        List<FanReading> fans = new();
        double? batteryTemperatureC = null;
        double? designedCapacityWh = null;
        double? fullChargeCapacityWh = null;

        void Visit(IHardware hardware)
        {
            if (!failedHardwareIds.Contains(hardware.Identifier.ToString()))
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.Value is not float value || !float.IsFinite(value)) continue;

                    if (hardware.HardwareType == HardwareType.Battery)
                    {
                        if (sensor.SensorType == SensorType.Temperature &&
                            sensor.Name.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
                        {
                            batteryTemperatureC = value;
                        }
                        else if (sensor.SensorType == SensorType.Energy)
                        {
                            // Windows BATTERY_INFORMATION reports capacities in mWh.
                            if (sensor.Name.Equals("Designed Capacity", StringComparison.OrdinalIgnoreCase))
                                designedCapacityWh = value / 1000.0;
                            else if (sensor.Name.Equals("Fully-Charged Capacity", StringComparison.OrdinalIgnoreCase))
                                fullChargeCapacityWh = value / 1000.0;
                        }
                        continue;
                    }

                    string id = sensor.Identifier.ToString();
                    string label = string.IsNullOrWhiteSpace(hardware.Name) || hardware.Name == sensor.Name
                        ? sensor.Name
                        : $"{hardware.Name} · {sensor.Name}";

                    if (sensor.SensorType == SensorType.Temperature &&
                        (sensor.Name.Contains("Battery", StringComparison.OrdinalIgnoreCase) ||
                         hardware.Name.Contains("Battery", StringComparison.OrdinalIgnoreCase)))
                    {
                        batteryTemperatureC = value;
                    }
                    else if (sensor.SensorType == SensorType.Temperature)
                    {
                        temperatures.Add(new SensorReading(id, label, value));
                    }
                    else if (sensor.SensorType == SensorType.Fan && value >= 0)
                    {
                        // LibreHardwareMonitor reports the current RPM, but not a rated maximum.
                        fans.Add(new FanReading(id, label, value, MaxRpm: null));
                    }
                }
            }

            foreach (IHardware child in hardware.SubHardware) Visit(child);
        }

        foreach (IHardware hardware in hardwareItems) Visit(hardware);

        return new HardwareSensorSnapshot(
            batteryTemperatureC,
            designedCapacityWh,
            fullChargeCapacityWh,
            temperatures,
            fans);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { computer?.Close(); }
        catch (Exception exception) { logger.LogDebug(exception, "关闭 LibreHardwareMonitor 时出错"); }
        computer = null;
    }

    private sealed class UpdateVisitor : IVisitor
    {
        private readonly ILogger logger;
        public HashSet<string> FailedHardwareIds { get; } = new(StringComparer.Ordinal);

        public UpdateVisitor(ILogger logger) => this.logger = logger;

        public void VisitComputer(IComputer computer)
        {
            foreach (IHardware hardware in computer.Hardware) VisitHardware(hardware);
        }

        public void VisitHardware(IHardware hardware)
        {
            try { hardware.Update(); }
            catch (Exception exception)
            {
                FailedHardwareIds.Add(hardware.Identifier.ToString());
                logger.LogDebug(exception, "读取传感器失败：{Hardware}", hardware.Name);
            }

            foreach (IHardware child in hardware.SubHardware) VisitHardware(child);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
