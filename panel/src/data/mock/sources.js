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
 * 已按 HONOR BCC-N（Windows 11 build 26200）的实测结果收敛：
 * 电量、电池功率（带符号）、插电状态、满充容量、循环次数都**已经是真的**，
 * 因此它们不再出现在下面的清单里。
 */
export const MOCK_SOURCES = [
  {
    key: 'BatteryTemperatureC',
    label: '电池温度',
    serviceSupport: 'no',
    reason: 'root\\wmi 的 BatteryTemperature 类在本机存在但**没有实例**，固件未暴露电池温度。',
    reference: '真机实测（2026-10，HONOR BCC-N）',
    fill: (t) => gen.batteryTemperatureCAt(t),
  },
  {
    key: 'AdapterPowerW',
    label: '适配器功率',
    serviceSupport: 'no',
    reason: '0x0902 只给出适配器电压（实测 20000 mV）；电流命令尚未确认，因此服务端不报功率。',
    reference: '真机实测 + research/conclusions.md 第 356 行',
    fill: (t) => gen.adapterPowerWAt(t),
  },
  {
    key: 'BatteryHealthPercent',
    label: '电池健康度',
    serviceSupport: 'no',
    reason: '缺设计容量无法计算：BatteryStaticData 无实例、Win32_Battery.DesignCapacity 为空，而 SYSTEM_BATTERY_STATE.MaxCapacity 实测等于当前满充容量（92041 mWh），拿它当设计容量会恒得 100%。',
    reference: '真机实测（2026-10，HONOR BCC-N）',
    fill: () => 96,
  },
  {
    key: 'BatteryDesignCapacityWh',
    label: '电池设计容量',
    serviceSupport: 'no',
    reason: '同上：本机没有任何可用的设计容量来源。',
    reference: '真机实测（2026-10，HONOR BCC-N）',
    fill: () => 83,
  },
]

/** 传感器（CPU/GPU/SSD 温度）：真机确认没有用户态来源。 */
export const MOCK_SENSORS = {
  serviceSupport: 'no',
  reason: '真机实测：MSAcpi_ThermalZoneTemperature 不可用、Win32_TemperatureProbe 与 Win32_Fan 均为 0 实例、SMART（MSStorageDriver_ATAPISmartData）无实例。要拿核心温度只能引入内核驱动。',
  reference: '真机实测（2026-10，HONOR BCC-N）',
}

/** 风扇转速：Win32 侧没有，荣耀通道的语义还没确认。 */
export const MOCK_FANS = {
  serviceSupport: 'unknown',
  reason: 'Win32_Fan 无实例；荣耀通道 0x0802 返回的 out[1..2]（实测 1776~2838，量级与转速吻合但与封装功率混淆）语义未确认，且 OemWMIMethod 只允许 SYSTEM 访问，需要在服务里另做验证。',
  reference: '真机实测 + research/conclusions.md 第 388 行',
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
  { key: 'SystemLoadW', label: '系统负载功率（拔电时由服务派生）' },
]

/** 演示/占位用的风扇上限；等服务端给出机型参数后应改为由 Capabilities 提供。 */
export const MOCK_FAN_MAX_RPM = 6000
