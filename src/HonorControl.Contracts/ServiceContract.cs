namespace HonorControl.Contracts;

/// <summary>
/// 界面（面板）与服务之间的版本化通信契约。
///
/// 约定：请求与响应都是单行 UTF-8 JSON，字段名 PascalCase（System.Text.Json 默认行为）。
/// 服务端每个请求有 5 秒超时，请求行上限 4096 字节——因此历史数据必须在服务端降采样后再返回。
///
/// v5 增加独立的 USB 供电诊断、性能执行状态及 Windows 电源设置命令。
/// 面板与服务**同步发布**，不保留旧协议兼容分支。
/// </summary>
public static class ServiceContract
{
    public const string PipeName = "HonorControl.Service.v1";
    public const int ProtocolVersion = 5;
}

public sealed record DesiredConfiguration(
    int? ChargeStart = null,
    int? ChargeEnd = null,
    int? PerformanceMode = null,
    bool AutoReconcile = false);

public sealed record ActualState(
    int? ChargeStart = null,
    int? ChargeEnd = null,
    int? PerformanceMode = null,
    string? PowerSchemeName = null,
    bool? IsOnAcPower = null,
    int? BatteryPercent = null,
    bool PcManagerOpen = false,
    bool BalancedPlanAvailable = false,
    bool HonorPerformancePlanAvailable = false,
    bool HighPerformanceSupported = false,
    string? ChargeError = null,
    string? PerformanceError = null,
    string? ServiceError = null,
    DateTimeOffset? CheckedAt = null,
    bool PerformancePending = false,
    int? PendingPerformanceMode = null);

public sealed record ServiceSnapshot(DesiredConfiguration Desired, ActualState Actual);

/// <summary>
/// 一个温度传感器读数。Id 由服务端定义且必须稳定（面板用它做 DOM 重建的缓存键）。
/// </summary>
public sealed record SensorReading(string Id, string Label, double TempC, double? WarnC = null, double? HotC = null);

/// <summary>一个风扇读数。笔记本不区分 CPU/GPU 风扇，数量由能力探测决定。</summary>
public sealed record FanReading(string Id, string Label, double Rpm, double? MaxRpm = null);

/// <summary>
/// 一次实时遥测快照（服务端 1 Hz 采样后缓存，客户端轮询只读缓存，不打 WMI）。
///
/// 所有可空字段都表示"当前**拿不到**"：服务端不猜、不填假值，由面板决定是显示占位还是隐藏。
/// 这条约定很重要——一旦服务端开始填估算值，界面上就再也分不清真实读数与猜测。
/// </summary>
public sealed record Telemetry(
    DateTimeOffset CheckedAt,
    bool? PluggedIn,
    double? BatteryPercent,
    double? BatteryTemperatureC = null,
    double? BatteryPowerW = null,
    double? AdapterPowerW = null,
    double? SystemLoadW = null,
    int? PerformanceMode = null,
    int? ChargeStartPercent = null,
    int? ChargeStopPercent = null,
    double? BatteryHealthPercent = null,
    double? BatteryDesignCapacityWh = null,
    double? BatteryFullChargeCapacityWh = null,
    int? BatteryCycleCount = null,
    IReadOnlyList<SensorReading>? Sensors = null,
    IReadOnlyList<FanReading>? Fans = null,
    bool PcManagerOpen = false,
    string? ChargeError = null,
    string? ServiceError = null,
    double? AdapterVoltageV = null,
    double? AdapterCurrentA = null,
    double? AdapterReportedPowerW = null,
    string? AdapterDiagnosticError = null);

/// <summary>
/// 服务自己探测出来的能力。面板用它决定"哪些行可以显示真实读数、哪些只能显示示例/隐藏"。
/// Supports 的键使用稳定的英文标识（BatteryTemperature / BatteryPower / AdapterPower /
/// Sensors / Fans / PowerLimits），MissingReason 给出人话原因。
/// </summary>
public sealed record Capabilities(
    double? AdapterRatedW,
    double? FanMaxRpm,
    IReadOnlyDictionary<string, bool> Supports,
    IReadOnlyDictionary<string, string> MissingReason);

/// <summary>历史查询。Range 取 "1h"/"24h"/"7d"，Points 是期望的返回点数（服务端负责降采样）。</summary>
public sealed record HistoryQuery(string Metric, string Range, int Points = 0);

/// <summary>
/// 历史序列：**等间隔的数值数组**（不带时间戳，未记录的点为 null）+ 区间小时数。
/// 面板用 hours / Samples.Count 求 dt 做 Wh 积分，因此两者必须同时给出。
/// </summary>
public sealed record HistorySeries(string Metric, string Range, double Hours, IReadOnlyList<double?> Samples);

public sealed record ServiceRequest(
    int Version,
    string Command,
    DesiredConfiguration? Desired = null,
    HistoryQuery? History = null,
    WindowsPowerSettingsUpdate? WindowsPower = null);

public sealed record ServiceResponse(
    int Version,
    ServiceSnapshot? Snapshot = null,
    Telemetry? Telemetry = null,
    Capabilities? Capabilities = null,
    HistorySeries? History = null,
    string? Error = null,
    WindowsPowerSettingsSnapshot? WindowsPower = null);
