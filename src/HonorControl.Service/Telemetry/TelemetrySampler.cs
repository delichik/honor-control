using HonorControl.Contracts;
using HonorControl.Service.Hardware;
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

    private readonly ReconciliationCoordinator coordinator;
    private readonly BatteryService battery = new();
    private readonly HistoryStore history;
    private readonly ILogger<TelemetrySampler> logger;

    private TelemetrySnapshot current = new(DateTimeOffset.Now, false, 0);
    private ServiceSnapshot? snapshot;
    private DateTimeOffset snapshotReadAt = DateTimeOffset.MinValue;
    private int? cycleCount;
    private DateTimeOffset cycleCountReadAt = DateTimeOffset.MinValue;

    public TelemetrySampler(
        ReconciliationCoordinator coordinator,
        HistoryStore history,
        ILogger<TelemetrySampler> logger)
    {
        this.coordinator = coordinator;
        this.history = history;
        this.logger = logger;
        Capabilities = ProbeCapabilities();
    }

    /// <summary>当前遥测快照。读的是内存缓存，客户端轮询不会打硬件。</summary>
    public TelemetrySnapshot Current => Volatile.Read(ref current);

    /// <summary>服务自己探测出的能力清单。</summary>
    public Capabilities Capabilities { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("遥测采样器启动。能力：{Supports}", string.Join(", ",
            Capabilities.Supports.Select(pair => $"{pair.Key}={(pair.Value ? "可用" : "不可用")}")));

        int tick = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Volatile.Write(ref current, Sample());
                // 每 60 个采样落一次盘（历史曲线的粒度就是 60 秒）
                if (++tick % 60 == 0) history.Append(current);
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

        return new TelemetrySnapshot(
            CheckedAt: now,
            PluggedIn: reading.PluggedIn,
            BatteryPercent: reading.Percent ?? actual.BatteryPercent ?? 0,
            BatteryTemperatureC: null,
            BatteryPowerW: reading.PowerW,
            AdapterPowerW: null,
            SystemLoadW: systemLoadW,
            PerformanceMode: actual.PerformanceMode ?? snapshot?.Desired.PerformanceMode ?? 1,
            ChargeStartPercent: actual.ChargeStart ?? snapshot?.Desired.ChargeStart ?? 0,
            ChargeStopPercent: actual.ChargeEnd ?? snapshot?.Desired.ChargeEnd ?? 100,
            BatteryHealthPercent: null,
            BatteryDesignCapacityWh: null,
            BatteryCycleCount: cycleCount,
            Sensors: Array.Empty<SensorReading>(),
            Fans: Array.Empty<FanReading>(),
            PcManagerOpen: actual.PcManagerOpen,
            ChargeError: actual.ChargeError,
            ServiceError: actual.ServiceError);
    }

    /// <summary>
    /// 能力探测。
    ///
    /// 这里的每个 false 都是**实测结论**，不是保守估计：
    /// - 电池温度：root\wmi 的 BatteryTemperature 类存在但**没有实例**（BCC-N 实测）；
    /// - 健康度/设计容量：设计容量没有可用来源——BatteryStaticData 无实例，Win32_Battery.DesignCapacity 为空，
    ///   SYSTEM_BATTERY_STATE.MaxCapacity 其实是**当前满充容量**（实测 92041 mWh = BatteryFullChargedCapacity），
    ///   拿它当设计容量算健康度会得到恒定的 100%；
    /// - CPU/GPU/SSD 温度：MSAcpi_ThermalZoneTemperature 不可用、Win32_TemperatureProbe 无实例、
    ///   SMART（MSStorageDriver_ATAPISmartData）无实例，没有用户态来源；
    /// - 风扇转速：Win32_Fan 无实例；荣耀通道的 0x0802 语义尚未确认（可能是封装功率），
    ///   且 OemWMIMethod 只允许 SYSTEM 访问，需要在服务里另做验证；
    /// - 适配器功率：0x0902 给出的是电压（实测 16400 mV 量级属电池，适配器为 20000 mV），
    ///   电流命令未确认，因此不报功率；
    /// - 功耗限制/风扇曲线：写路径未定位 / 需要荣耀内核驱动。
    /// </summary>
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

        Dictionary<string, string> missing = new()
        {
            ["BatteryTemperature"] = "root\\wmi 的 BatteryTemperature 类在本机存在但没有实例，固件未暴露电池温度。",
            ["BatteryHealth"] = "设计容量没有可用来源（BatteryStaticData 无实例、Win32_Battery.DesignCapacity 为空）。",
            ["AdapterPower"] = "0x0902 只给出适配器电压，电流命令尚未确认，因此不报适配器功率。",
            ["Sensors"] = "未找到 CPU/GPU/SSD 温度的用户态来源（ACPI 热区、Win32_TemperatureProbe、SMART 均无实例）。",
            ["Fans"] = "Win32_Fan 无实例；荣耀通道 0x0802 的语义未确认，确认前不能当转速用。",
            ["PowerLimits"] = "功耗墙的写入路径尚未定位。",
            ["FanCurve"] = "风扇策略写入需要荣耀内核驱动通道，没有用户态入口。",
        };

        Dictionary<string, bool> supports = new()
        {
            ["BatteryPercent"] = true,
            ["BatteryPower"] = true,
            ["BatteryCycleCount"] = cycles.HasValue,
            ["BatteryTemperature"] = false,
            ["BatteryHealth"] = false,
            ["AdapterPower"] = false,
            ["Sensors"] = false,
            ["Fans"] = false,
            ["PowerLimits"] = false,
            ["FanCurve"] = false,
            ["History"] = true,
        };

        return new Capabilities(
            AdapterRatedW: null,
            // 机型参数，服务端暂时没有来源；面板的风扇界面在 Fans 不可用时不会用到它。
            FanMaxRpm: 6000,
            Supports: supports,
            MissingReason: missing);
    }
}
