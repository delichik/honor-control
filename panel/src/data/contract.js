/**
 * 与服务（命名管道 \\.\pipe\HonorControl.Service.v1）的通信契约，v2。
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

export const PROTOCOL_VERSION = 2

/** 与服务端 ServiceContract.PipeName 保持一致。 */
export const PIPE_NAME = String.raw`\\.\pipe\HonorControl.Service.v1`

/**
 * 服务端 PipeServer.Execute 支持的命令。
 * 这是 v2 的完整命令集：服务端与面板同步发布，不保留旧版本分支。
 */
export const COMMANDS = {
  GetState: 'GetState',
  GetTelemetry: 'GetTelemetry',
  GetCapabilities: 'GetCapabilities',
  GetHistory: 'GetHistory',
  SetCharge: 'SetCharge',
  SetPerformance: 'SetPerformance',
  SetAutoReconcile: 'SetAutoReconcile',
  SetTrayPolicy: 'SetTrayPolicy',
  ShutdownService: 'ShutdownService',
}

/** 托盘拉起策略（服务侧持久化，托盘进程只读）。 */
export const TRAY_POLICY = {
  Off: 'Off',
  OnDemand: 'OnDemand',
  Always: 'Always',
}

/** 历史曲线的可选时间范围，与服务端支持的范围一致。 */
export const HISTORY_RANGES = [
  { id: '1h', label: '1 小时', hours: 1, points: 180 },
  { id: '24h', label: '24 小时', hours: 24, points: 240 },
  { id: '7d', label: '7 天', hours: 168, points: 300 },
]

export const HISTORY_METRICS = {
  batteryPower: 'BatteryPower',
  adapterPower: 'AdapterPower',
  systemLoad: 'SystemLoad',
}

/** 性能模式：服务端只认 1（智能）与 2（高能），与 0x0C07 的 payload 对应。 */
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
 * @property {number} MaxRpm      用于转速占比与曲线纵轴
 *
 * @typedef {Object} Telemetry
 * @property {string}  CheckedAt                采样时间（ISO 8601）
 * @property {boolean} PluggedIn                是否接入适配器
 * @property {number}  BatteryPercent           电量百分比
 * @property {number|null} BatteryTemperatureC  电池温度：服务当前无来源 → 示例数据
 * @property {number|null} BatteryPowerW        电池功率，带符号（正=充入，负=放出）
 * @property {number|null} AdapterPowerW        适配器输出功率：需真机验证电压×电流 → 示例数据
 * @property {number|null} SystemLoadW          系统负载功率（服务按 适配器 − 充入功率 派生）
 * @property {number}  PerformanceMode          1=智能 2=高能
 * @property {number}  ChargeStartPercent
 * @property {number}  ChargeStopPercent
 * @property {number|null} BatteryHealthPercent 健康度：待真机确认电池类 → 示例数据
 * @property {number|null} BatteryDesignCapacityWh
 * @property {number|null} BatteryCycleCount
 * @property {SensorReading[]} Sensors          CPU/GPU/SSD 温度：暂无用户态来源 → 示例数据
 * @property {FanReading[]}    Fans             风扇转速：0x0802 语义未定 → 示例数据
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
 * @property {string} [TrayPolicy]
 * @property {string} [ServiceVersion]
 */

/** 服务完全不可用时用来渲染骨架，避免每个组件都要判空。 */
export function emptyTelemetry() {
  return {
    CheckedAt: new Date().toISOString(),
    PluggedIn: false,
    BatteryPercent: 0,
    BatteryTemperatureC: null,
    BatteryPowerW: null,
    AdapterPowerW: null,
    SystemLoadW: null,
    PerformanceMode: 1,
    ChargeStartPercent: 40,
    ChargeStopPercent: 70,
    BatteryHealthPercent: null,
    BatteryDesignCapacityWh: null,
    BatteryCycleCount: null,
    Sensors: [],
    Fans: [],
    PcManagerOpen: false,
    ChargeError: null,
    ServiceError: null,
  }
}
