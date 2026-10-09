/**
 * 服务当前拿不到或尚未验证的指标清单，用于开发期数据来源说明。
 *
 * 原生面板不会用这些条目合成硬件读数；缺失值保持为空。完整模拟只在浏览器预览或用户主动开启时使用。
 * 条目规则：
 * 1. 每个条目都必须写清 `reason`（为什么拿不到）和 `reference`（实测结论的出处）；
 * 2. 服务接上指标后更新来源状态，组件继续按 Telemetry 字段展示。
 *
 * serviceSupport 的含义：
 *   'yes'     服务已经能提供（只在"全量示例数据"模式下才会被模拟）
 *   'unknown' 数据源未验证，需要真机确认
 *   'no'      已在真机上确认没有用户态来源
 *
 * Windows 自带 WMI 没有实例不代表设备没有数据源。服务用 LibreHardwareMonitor
 * 读取硬件传感器和 Windows 电池设备接口；目标机仍需确认驱动是否提供读数。
 */
export const MOCK_SOURCES = [
  {
    key: 'BatteryTemperatureC',
    label: '电池温度',
    serviceSupport: 'unknown',
    reason: '服务通过 LibreHardwareMonitor 读取电池温度；该库或机型驱动未必提供此字段，仍需真机确认。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
  },
  {
    key: 'AdapterPowerW',
    label: '适配器功率',
    serviceSupport: 'unknown',
    reason: '0x0902 可读到适配器电压；服务还未接入 0x10902/0x110902 电流读取，当前不能计算真实瓦数。',
    reference: '真机实测 + research/conclusions.md 第 356 行',
  },
  {
    key: 'BatteryHealthPercent',
    label: '电池健康度',
    serviceSupport: 'unknown',
    reason: '健康度按满充容量 / 设计容量计算；服务现在通过 LibreHardwareMonitor 和 Windows 电池接口读取两项容量，需在目标机确认返回值。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
  },
  {
    key: 'BatteryDesignCapacityWh',
    label: '电池设计容量',
    serviceSupport: 'unknown',
    reason: '服务通过 LibreHardwareMonitor 的 Windows 电池设备接口读取设计容量；需在目标机确认电池驱动是否提供。',
    reference: 'LibreHardwareMonitor 0.9.6；运行时能力探测',
  },
  {
    key: 'BatteryFullChargeCapacityWh',
    label: '电池满充容量',
    serviceSupport: 'yes',
    reason: '来自 Windows 电池状态接口或 LibreHardwareMonitor；服务未返回时不能用设计容量代替。',
    reference: 'HonorControl.Service BatteryService + LibreHardwareMonitor',
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
