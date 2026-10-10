/**
 * 与服务（命名管道 \\.\pipe\HonorControl.Service.v1）的通信契约，v5。
 *
 * 约定（与服务端 .NET 8 实现一致，改动前必须两边同步）：
 * - 请求与响应都是**单行** UTF-8 JSON，以 \n 结束；一次连接只跑一问一答。
 * - 字段名是 PascalCase（服务端 System.Text.Json 未配置 camelCase 策略）。
 * - 服务端每个请求有 5 秒超时，请求行上限 4096 字节，因此历史数据必须由服务端降采样后再返回。
 *
 * 本文件只描述形状与常量，不做任何取值。真实的取值链路是：
 *   data/queries.js → data/transport.js →（Tauri）Rust service_request → 管道 → 服务
 * 服务拿不到的指标由 data/mock 补齐，详见 data/mock/sources.js。
 */

export const PROTOCOL_VERSION = 5

/** 与服务端 ServiceContract.PipeName 保持一致。 */
export const PIPE_NAME = String.raw`\\.\pipe\HonorControl.Service.v1`

/**
 * 服务端 PipeServer.Execute 支持的命令。
 * 服务端与面板同步发布，不保留旧版本分支。
 */
export const COMMANDS = {
  GetState: 'GetState',
  GetTelemetry: 'GetTelemetry',
  GetCapabilities: 'GetCapabilities',
  GetHistory: 'GetHistory',
  SetCharge: 'SetCharge',
  SetPerformance: 'SetPerformance',
  SetAutoReconcile: 'SetAutoReconcile',
  GetWindowsPower: 'GetWindowsPower',
  SetWindowsPower: 'SetWindowsPower',
  RestoreWindowsPower: 'RestoreWindowsPower',
}

/** 历史曲线的可选时间范围，与服务端支持的范围一致。 */
export const HISTORY_RANGES = [
  { id: '1h', label: '1 小时', hours: 1, points: 60 },
  { id: '24h', label: '24 小时', hours: 24, points: 240 },
  { id: '7d', label: '7 天', hours: 168, points: 300 },
]

export const HISTORY_METRICS = {
  batteryPower: 'BatteryPower',
  adapterPower: 'AdapterPower',
  systemLoad: 'SystemLoad',
}

/** 性能模式：1（智能）与 2（高能），对应 0x0F04 的状态字节 0 / 1。 */
export const PERFORMANCE_MODES = [
  { id: 1, label: '智能模式', hint: '按负载自动平衡功耗与风扇' },
  { id: 2, label: '高能模式', hint: '提高功耗墙与风扇转速上限' },
]

/**
 * 充放电判定阈值（瓦）。
 * 这个口径必须与服务端、以及历史数据聚合保持一致，否则首页（实时值）与监控页（序列）
 * 会出现两个不一样的结论。见可行性文档 5.3。
 */
export const POWER_EPSILON_W = 0.6

/**
 * @typedef {Object} SensorReading
 * @property {string} Id          稳定标识（cpu/gpu/ssd…）。同时用作前端 DOM 重建的缓存键，不要中途改名。
 * @property {string} Label       展示名
 * @property {number} TempC       摄氏温度
 * @property {number} [WarnC]     偏高阈值，用于配色
 * @property {number} [HotC]      过热阈值
 *
 * @typedef {Object} FanReading
 * @property {string} Id
 * @property {string} Label
 * @property {number} Rpm
 * @property {number|null} MaxRpm 额定上限未知时为空
 *
 * @typedef {Object} Telemetry
 * @property {string}  CheckedAt                采样时间（ISO 8601）
 * @property {boolean|null} PluggedIn           是否接入适配器
 * @property {number|null} BatteryPercent        电量百分比
 * @property {number|null} BatteryTemperatureC  电池温度：服务从硬件监测库读取，无读数时为空
 * @property {number|null} BatteryPowerW        电池功率，带符号（正=充入，负=放出）
 * @property {number|null} AdapterPowerW        实时适配器功率：未确认实时电流语义时保持为空
 * @property {number|null} AdapterVoltageV      USB 输入电压诊断，V
 * @property {number|null} AdapterCurrentA      官方 USB 电流值，A；不保证为实时测量
 * @property {number|null} AdapterReportedPowerW 官方瓦数计算值，不用于实时负载或历史
 * @property {string|null} AdapterDiagnosticError
 * @property {number|null} SystemLoadW          系统负载功率（服务按 适配器 − 充入功率 派生）
 * @property {number|null} PerformanceMode      实际模式：1=智能 2=高能
 * @property {number|null} ChargeStartPercent   实际开始阈值
 * @property {number|null} ChargeStopPercent    实际停止阈值
 * @property {number|null} BatteryHealthPercent 健康度：没有设计/满充容量时为空
 * @property {number|null} BatteryDesignCapacityWh 设计容量：接口未返回时为空
 * @property {number|null} BatteryFullChargeCapacityWh
 * @property {number|null} BatteryCycleCount
 * @property {SensorReading[]} Sensors          CPU/GPU/SSD 温度：服务从 LibreHardwareMonitor 读取，无读数时为空数组
 * @property {FanReading[]}    Fans             风扇转速：服务从 LibreHardwareMonitor 读取，无读数时为空数组
 * @property {boolean} PcManagerOpen            荣耀电脑管家在运行（此刻服务只读）
 * @property {string|null} ChargeError
 * @property {string|null} ServiceError
 *
 * @typedef {Object} DesiredConfiguration
 * @property {number|null} ChargeStart
 * @property {number|null} ChargeEnd
 * @property {number|null} PerformanceMode
 * @property {boolean} AutoReconcile
 *
 * @typedef {Object} ActualState
 * @property {number|null} ChargeStart
 * @property {number|null} ChargeEnd
 * @property {number|null} PerformanceMode
 * @property {string|null} PowerSchemeName
 * @property {boolean|null} IsOnAcPower
 * @property {number|null} BatteryPercent
 * @property {boolean} PcManagerOpen
 * @property {string|null} ChargeError
 * @property {string|null} PerformanceError
 * @property {string|null} ServiceError
 * @property {string|null} CheckedAt
 *
 * @typedef {Object} ServiceSnapshot
 * @property {DesiredConfiguration} Desired
 * @property {ActualState} Actual
 * @property {string} [ServiceVersion]
 */

/** 服务完全不可用时用来渲染骨架，避免每个组件都要判空。 */
export function emptyTelemetry() {
  return {
    CheckedAt: new Date().toISOString(),
    PluggedIn: null,
    BatteryPercent: null,
    BatteryTemperatureC: null,
    BatteryPowerW: null,
    AdapterPowerW: null,
    AdapterVoltageV: null,
    AdapterCurrentA: null,
    AdapterReportedPowerW: null,
    AdapterDiagnosticError: null,
    SystemLoadW: null,
    PerformanceMode: null,
    ChargeStartPercent: null,
    ChargeStopPercent: null,
    BatteryHealthPercent: null,
    BatteryDesignCapacityWh: null,
    BatteryFullChargeCapacityWh: null,
    BatteryCycleCount: null,
    Sensors: [],
    Fans: [],
    PcManagerOpen: false,
    ChargeError: null,
    ServiceError: null,
  }
}
