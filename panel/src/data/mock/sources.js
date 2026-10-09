import * as gen from './generator.js'

/**
 * 服务当前拿不到的指标清单 —— 这些字段在前端先用示例数据占位。
 *
 * 这是**唯一**允许造假数据的地方。规则：
 * 1. 每个条目都必须写清 `reason`（为什么拿不到）和 `reference`（实测结论的出处）；
 * 2. 取值一律来自 generator.js，不要在组件里临时凑数字；
 * 3. 服务把这些指标接上以后，**删除对应条目**即可——组件不需要改，因为组件只认 Telemetry 的字段。
 *
 * serviceSupport 的含义：
 *   'yes'     服务已经能提供（只在"全量示例数据"模式下才会被模拟）
 *   'unknown' 数据源未验证，需要真机确认
 *   'no'      已在真机上确认没有用户态来源
 *
 * Windows 自带 WMI 没有实例不代表设备没有数据源。服务改用 LibreHardwareMonitor
 * 读取硬件传感器和 Windows 电池设备接口；可用字段仍需在服务运行时确认。
 */
export const MOCK_SOURCES = [
  {
    key: 'BatteryTemperatureC',
    label: '电池温度',
    serviceSupport: 'unknown',
    reason: '服务通过 LibreHardwareMonitor 读取电池温度；该库或机型驱动未必提供此字段，仍需真机确认。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
    fill: (t) => gen.batteryTemperatureCAt(t),
  },
  {
    key: 'AdapterPowerW',
    label: '适配器功率',
    serviceSupport: 'unknown',
    reason: '荣耀协议已定位到 0x0902 电压与 0x10902/0x110902 电流的组合；服务侧电流读取与真机单位校验待完成。',
    reference: '真机实测 + research/conclusions.md 第 356 行',
    fill: (t) => gen.adapterPowerWAt(t),
  },
  {
    key: 'BatteryHealthPercent',
    label: '电池健康度',
    serviceSupport: 'unknown',
    reason: '健康度按满充容量 / 设计容量计算；服务现在通过 LibreHardwareMonitor 和 Windows 电池接口读取两项容量，需在目标机确认返回值。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
    fill: () => 96,
  },
  {
    key: 'BatteryDesignCapacityWh',
    label: '电池设计容量',
    serviceSupport: 'unknown',
    reason: '服务通过 LibreHardwareMonitor 的 Windows 电池设备接口读取设计容量；需在目标机确认电池驱动是否提供。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
    fill: () => 83,
  },
  {
    key: 'BatteryFullChargeCapacityWh',
    label: '电池满充容量',
    serviceSupport: 'yes',
    reason: '来自 Windows 电池状态接口或 LibreHardwareMonitor；服务未返回时不能用设计容量代替。',
    reference: 'HonorControl.Service BatteryService + LibreHardwareMonitor',
    fill: () => 79.68,
  },
]

/** CPU/GPU/SSD 温度：LibreHardwareMonitor 可读到的传感器因机型与驱动而异。 */
export const MOCK_SENSORS = {
  serviceSupport: 'unknown',
  reason: '服务通过 LibreHardwareMonitor 在系统服务中读取 CPU/GPU/存储传感器；具体可用项依赖硬件和驱动。',
  reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
}

/** 风扇转速：由硬件监测库或设备驱动暴露时读取；最高转速没有通用值。 */
export const MOCK_FANS = {
  serviceSupport: 'unknown',
  reason: '服务通过 LibreHardwareMonitor 在系统服务中读取当前 RPM；机型是否暴露风扇传感器需实测，额定最高转速不由库保证。',
  reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
}

/** 风扇策略曲线：既读不到也写不了，曲线图上的形态是示例策略。 */
export const MOCK_FAN_CURVE = {
  serviceSupport: 'no',
  reason: '风扇策略的读/写都要经过荣耀内核驱动（\\.\\WDT0001）+ NLD 风扇库，没有用户态通道，因此曲线形态是按"温度越高转速越高"画的示例策略，不代表本机固件里的真实曲线。',
  reference: 'research/conclusions.md 第 212 行',
}

/**
 * 服务已经能提供的字段，列在这里是为了在"数据来源"面板里能对照展示，
 * 同时也作为"全量示例数据"模式的字段清单。
 *
 * 其中 BatteryPowerW / BatteryCycleCount 是 2026-10 在 HONOR BCC-N 上实测打通后从示例清单里移过来的。
 */
export const SERVICE_FIELDS = [
  { key: 'BatteryPercent', label: '电量百分比' },
  { key: 'PluggedIn', label: '是否接入适配器' },
  { key: 'BatteryPowerW', label: '电池功率（带符号，实测可用）' },
  { key: 'BatteryCycleCount', label: '电池循环次数（实测可用）' },
  { key: 'ChargeStartPercent', label: '开始充电阈值' },
  { key: 'ChargeStopPercent', label: '停止充电阈值' },
  { key: 'PerformanceMode', label: '性能模式' },
  { key: 'SystemLoadW', label: '系统负载功率（拔电实算；接电依赖适配器功率）' },
]

/** 演示/占位用的风扇上限；等服务端给出机型参数后应改为由 Capabilities 提供。 */
export const MOCK_FAN_MAX_RPM = 6000
