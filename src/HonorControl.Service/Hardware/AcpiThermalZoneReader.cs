using System.Globalization;
using HonorControl.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;

namespace HonorControl.Service.Hardware;

/// <summary>
/// Reads the Windows ACPI thermal-zone temperatures from the privileged service.
/// These values are labeled as ACPI zones; they are not treated as battery, CPU,
/// or GPU temperatures unless the firmware exposes a dedicated sensor for them.
/// </summary>
internal sealed class AcpiThermalZoneReader
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
    private readonly ILogger logger;
    private DateTimeOffset refreshAfter = DateTimeOffset.MinValue;
    private IReadOnlyList<SensorReading> cached = Array.Empty<SensorReading>();

    public AcpiThermalZoneReader(ILogger logger) => this.logger = logger;

    public IReadOnlyList<SensorReading> Read()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < refreshAfter) return cached;

        refreshAfter = now + CacheDuration;
        try
        {
            List<SensorReading> readings = new();
            using CimSession session = CimSession.Create(null);
            foreach (CimInstance instance in session.QueryInstances(
                         @"root\wmi", "WQL", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
            {
                object? temperatureValue = GetProperty(instance, "CurrentTemperature");
                if (temperatureValue == null) continue;

                // MSAcpi_ThermalZoneTemperature reports tenths of a kelvin.
                double temperatureC = Convert.ToDouble(temperatureValue, CultureInfo.InvariantCulture) / 10.0 - 273.15;
                if (!double.IsFinite(temperatureC) || temperatureC < 0 || temperatureC > 150) continue;

                string instanceName = Convert.ToString(GetProperty(instance, "InstanceName"), CultureInfo.InvariantCulture)
                    ?? "ACPI\\ThermalZone\\Unknown";
                string zoneName = instanceName.Split('\\').LastOrDefault() ?? "Unknown";
                int suffix = zoneName.IndexOf('_');
                if (suffix >= 0) zoneName = zoneName[..suffix];

                readings.Add(new SensorReading(
                    Id: "acpi-thermal-zone:" + instanceName,
                    Label: "ACPI 热区 " + zoneName,
                    TempC: temperatureC));
            }

            cached = readings;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "读取 ACPI 热区温度失败");
            cached = Array.Empty<SensorReading>();
        }

        return cached;
    }

    private static object? GetProperty(CimInstance instance, string name)
    {
        foreach (CimProperty property in instance.CimInstanceProperties)
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        return null;
    }
}
