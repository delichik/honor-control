using HonorControl.Contracts;
using HonorControl.Service.Hardware;
using HonorControl.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// 本文件的命名空间叫 …Service.Telemetry，而契约里的快照类型也叫 Telemetry；
// 在命名空间内部直接用 Telemetry 会解析成命名空间，所以这里起个别名。
using TelemetrySnapshot = HonorControl.Contracts.Telemetry;

namespace HonorControl.Service.Telemetry;

/// <summary>
/// 遥测采样器。
///
/// 设计要点：**采样与请求解耦**。客户端（面板）以 1 Hz 轮询，但它读的只是这里的缓存，
/// 不触发任何 WMI/CIM 调用；真正的采样发生在后台这个循环里。
///
/// 诚实原则：拿不到的指标一律留 null，绝不用估算值填充。面板据此把对应位置标成"示例"或隐藏。
/// 哪些拿得到、哪些拿不到由 <see cref="Capabilities"/> 如实声明，原因是真机实测结论
/// （见 docs/service-tray-tauri-feasibility.md 第 5.1 节与本文件末尾的注释）。
/// </summary>
internal sealed class TelemetrySampler : BackgroundService
{
    private const int SampleIntervalMs = 1000;

    /// <summary>配置类字段（阈值、模式、电脑管家状态）变化很慢，15 秒同步一次即可。</summary>
    private static readonly TimeSpan SnapshotRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>循环次数变化极慢，且要开 CIM 会话，10 分钟读一次。</summary>
    private static readonly TimeSpan CycleCountRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OemRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly ReconciliationCoordinator coordinator;
    private readonly BatteryService battery = new();
    private readonly HistoryStore history;
    private readonly ILogger<TelemetrySampler> logger;
    private readonly HardwareSensorReader hardware;
    private readonly AcpiThermalZoneReader acpiThermalZones;
    private readonly OemWmiClient oem = new();
    private OemTelemetryReading oemSnapshot = new();
    private DateTimeOffset oemReadAt = DateTimeOffset.MinValue;

    private TelemetrySnapshot current = new(DateTimeOffset.Now, null, null);
    private ServiceSnapshot? snapshot;
    private DateTimeOffset snapshotReadAt = DateTimeOffset.MinValue;
    private int? cycleCount;
    private DateTimeOffset cycleCountReadAt = DateTimeOffset.MinValue;
    private bool cycleCountSupported;
    private HardwareSensorSnapshot hardwareSnapshot = HardwareSensorSnapshot.Empty;
    private Capabilities capabilities = new(null, null,
        new Dictionary<string, bool>(), new Dictionary<string, string>());

    public TelemetrySampler(
        ReconciliationCoordinator coordinator,
        HistoryStore history,
        ILogger<TelemetrySampler> logger)
    {
        this.coordinator = coordinator;
        this.history = history;
        this.logger = logger;
        hardware = new HardwareSensorReader(logger);
        acpiThermalZones = new AcpiThermalZoneReader(logger);
        Capabilities = ProbeCapabilities();
    }

    /// <summary>当前遥测快照。读的是内存缓存，客户端轮询不会打硬件。</summary>
    public TelemetrySnapshot Current => Volatile.Read(ref current);

    /// <summary>服务自己探测出的能力清单。</summary>
    public Capabilities Capabilities
    {
        get => Volatile.Read(ref capabilities);
        private set => Volatile.Write(ref capabilities, value);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        int tick = 0;
        bool loggedCapabilities = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TelemetrySnapshot sample = Sample();
                Volatile.Write(ref current, sample);
                if (!loggedCapabilities)
                {
                    logger.LogInformation("遥测采样器启动。能力：{Supports}", string.Join(", ",
                        Capabilities.Supports.Select(pair => $"{pair.Key}={(pair.Value ? "可用" : "不可用")}")));
                    loggedCapabilities = true;
                }
                // 每 60 个采样落一次盘（历史曲线的粒度就是 60 秒）
                if (++tick % 60 == 0) history.Append(sample);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "遥测采样失败");
            }

            try
            {
                await Task.Delay(SampleIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TelemetrySnapshot Sample()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        hardwareSnapshot = hardware.Read();
        if (now - oemReadAt >= OemRefreshInterval)
        {
            try { oemSnapshot = oem.GetTelemetry(); }
            catch (Exception exception)
            {
                // Reset failed readings; never carry an old successful sample indefinitely.
                oemSnapshot = new(BatteryTemperatureError: exception.Message, AdapterDiagnosticError: exception.Message);
                logger.LogDebug(exception, "荣耀 NTC/USB 诊断采样失败");
            }
            oemReadAt = now;
        }

        if (now - snapshotReadAt >= SnapshotRefreshInterval)
        {
            snapshot = coordinator.Snapshot();
            snapshotReadAt = now;
        }

        if (now - cycleCountReadAt >= CycleCountRefreshInterval)
        {
            cycleCount = Capabilities.Supports.TryGetValue("BatteryCycleCount", out bool supported) && supported
                ? battery.GetCycleCount()
                : null;
            cycleCountReadAt = now;
        }

        BatteryReading reading = battery.Read();
        ActualState actual = snapshot?.Actual ?? new ActualState();

        // 系统负载：只有拔掉适配器时才能**如实**算出来（电池放出的就是整机负载）。
        // 插着适配器时整机负载 = 适配器功率 − 充入电池的功率，而适配器功率当前没有可信来源，
        // 所以这里返回 null，而不是用"负载 = 电池功率"这种只在放电时成立的公式去糊弄。
        double? systemLoadW = !reading.PluggedIn && reading.PowerW.HasValue
            ? Math.Abs(Math.Min(0, reading.PowerW.Value))
            : null;
        double? fullChargeWh = hardwareSnapshot.BatteryFullChargeCapacityWh ?? reading.FullChargeWh;
        double? batteryHealth = hardwareSnapshot.BatteryDesignCapacityWh is > 0 && fullChargeWh is > 0
            ? fullChargeWh.Value / hardwareSnapshot.BatteryDesignCapacityWh.Value * 100.0
            : null;
        SensorReading[] sensors = hardwareSnapshot.Sensors.Concat(acpiThermalZones.Read()).ToArray();
        Capabilities = BuildCapabilities(cycleCountSupported, hardwareSnapshot, fullChargeWh.HasValue, sensors.Length);

        return new TelemetrySnapshot(
            CheckedAt: now,
            PluggedIn: reading.PluggedIn,
            BatteryPercent: reading.Percent ?? actual.BatteryPercent,
            BatteryTemperatureC: oemSnapshot.BatteryTemperatureC ?? hardwareSnapshot.BatteryTemperatureC,
            BatteryPowerW: reading.PowerW,
            AdapterPowerW: null,
            SystemLoadW: systemLoadW,
            PerformanceMode: actual.PerformanceMode,
            ChargeStartPercent: actual.ChargeStart,
            ChargeStopPercent: actual.ChargeEnd,
            BatteryHealthPercent: batteryHealth,
            BatteryDesignCapacityWh: hardwareSnapshot.BatteryDesignCapacityWh,
            BatteryFullChargeCapacityWh: fullChargeWh,
            BatteryCycleCount: cycleCount,
            Sensors: sensors,
            Fans: hardwareSnapshot.Fans,
            PcManagerOpen: actual.PcManagerOpen,
            ChargeError: actual.ChargeError,
            ServiceError: actual.ServiceError,
            AdapterVoltageV: reading.PluggedIn == true ? oemSnapshot.AdapterVoltageV : null,
            AdapterCurrentA: reading.PluggedIn == true ? oemSnapshot.AdapterCurrentA : null,
            AdapterReportedPowerW: reading.PluggedIn == true ? oemSnapshot.AdapterReportedPowerW : null,
            AdapterDiagnosticError: reading.PluggedIn == true ? oemSnapshot.AdapterDiagnosticError : null);
    }

    private Capabilities ProbeCapabilities()
    {
        int? cycles = null;
        try
        {
            cycles = battery.GetCycleCount();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "读取电池循环次数失败");
        }

        cycleCountSupported = cycles.HasValue;
        return BuildCapabilities(
            cycleCountSupported,
            hardwareSnapshot,
            battery.Read().FullChargeWh.HasValue,
            hardwareSnapshot.Sensors.Count + acpiThermalZones.Read().Count);
    }

    private Capabilities BuildCapabilities(
        bool hasCycleCount,
        HardwareSensorSnapshot readings,
        bool hasFullChargeCapacity,
        int sensorCount)
    {
        Dictionary<string, string> missing = new()
        {
            ["BatteryTemperature"] = oemSnapshot.BatteryTemperatureC.HasValue || readings.BatteryTemperatureC.HasValue
                ? string.Empty
                : oemSnapshot.BatteryTemperatureError ?? "荣耀 NTC 与硬件监测库未返回电池温度。",
            ["BatteryHealth"] = readings.BatteryDesignCapacityWh.HasValue && hasFullChargeCapacity
                ? string.Empty
                : "硬件监测库或 Windows 电池接口未返回设计容量与满充容量。",
            ["AdapterPower"] = "已探测到扩展电流值，但单位及实时/额定语义未确认；服务暂不换算为实时功率。",
            ["AdapterDiagnostics"] = oemSnapshot.AdapterDiagnosticError ?? "USB 输入电压、电流与乘积仅供诊断，尚未确认是实时功耗或铭牌额定功率。",
            ["Sensors"] = sensorCount > 0
                ? string.Empty
                : "硬件库与 ACPI 均未从此设备返回温度传感器。",
            ["Fans"] = readings.Fans.Count > 0
                ? string.Empty
                : "LibreHardwareMonitor 未从此设备返回风扇转速。",
            ["PowerLimits"] = "功耗墙的写入路径尚未定位。",
        };

        Dictionary<string, bool> supports = new()
        {
            ["BatteryPercent"] = true,
            ["BatteryPower"] = true,
            ["BatteryCycleCount"] = hasCycleCount,
            ["BatteryTemperature"] = oemSnapshot.BatteryTemperatureC.HasValue || readings.BatteryTemperatureC.HasValue,
            ["BatteryDesignCapacity"] = readings.BatteryDesignCapacityWh.HasValue,
            ["BatteryHealth"] = readings.BatteryDesignCapacityWh.HasValue && hasFullChargeCapacity,
            ["AdapterPower"] = false,
            ["AdapterDiagnostics"] = oemSnapshot.AdapterVoltageV.HasValue && oemSnapshot.AdapterCurrentA.HasValue,
            ["Sensors"] = sensorCount > 0,
            ["Fans"] = readings.Fans.Count > 0,
            ["PowerLimits"] = false,
            ["History"] = true,
        };

        return new Capabilities(
            AdapterRatedW: null,
            FanMaxRpm: null,
            Supports: supports,
            MissingReason: missing);
    }

    public override void Dispose()
    {
        hardware.Dispose();
        base.Dispose();
    }
}
