import * as gen from './generator.js'

/**
 * 服务当前拿不到的指标清单 —— 这些字段在前端先用示例数据占位。
 *
 * 这是**唯一**允许造假数据的地方。规则：
 * 1. 每个条目都必须写清 `reason`（为什么拿不到）和 `reference`（可行性文档 5.1 的结论来源）；
 * 2. 取值一律来自 generator.js，不要在组件里临时凑数字；
 * 3. 服务把这些指标接上以后，**删除对应条目**即可——组件不需要改，因为组件只认 Telemetry 的字段。
 *
 * serviceSupport 的含义：
 *   'yes'     服务已经能提供（只在"全量示例数据"模式下才会被模拟）
 *   'unknown' 数据源未验证，需要真机确认
 *   'no'      目前没有已知的用户态来源
 */
export const MOCK_SOURCES = [
  {
    key: 'BatteryTemperatureC',
    label: '电池温度',
    serviceSupport: 'unknown',
    reason: '服务当前没有电池温度来源，需要真机枚举 root\\wmi 电池类（BatteryTemperature / MSBatteryClass）确认。',
    reference: '可行性文档 5.1「temp 电池温度」',
    fill: (t) => gen.batteryTemperatureCAt(t),
  },
  {
    key: 'BatteryPowerW',
    label: '电池功率（带符号）',
    serviceSupport: 'unknown',
    reason: '候选来源是 root\\wmi:BatteryStatus 的 ChargeRate/DischargeRate（mW），未在真机验证；备选是按 RemainingCapacity 差分估算。',
    reference: '可行性文档 5.1「batt 电池功率」',
    fill: (t) => gen.batteryPowerWAt(t),
  },
  {
    key: 'AdapterPowerW',
    label: '适配器功率',
    serviceSupport: 'unknown',
    reason: '需要 0x0902 适配器电压(mV) × 0x10902/0x110902 电流，乘法与多口适配器组合尚未实测。',
    reference: '可行性文档 5.1「ac 适配器功率」+ research/conclusions.md 第 356 行',
    fill: (t) => gen.adapterPowerWAt(t),
  },
  {
    key: 'BatteryHealthPercent',
    label: '电池健康度',
    serviceSupport: 'unknown',
    reason: '候选来源 root\\wmi 电池类（BatteryFullChargedCapacity ÷ BatteryStaticData.DesignedCapacity），未验证。',
    reference: '可行性文档 5.1「健康度 / 设计容量 / 循环次数」',
    fill: () => 96,
  },
  {
    key: 'BatteryDesignCapacityWh',
    label: '电池设计容量',
    serviceSupport: 'unknown',
    reason: '同上，需要读取电池静态数据类；机型之间的容量不同，后续应按机型表提供。',
    reference: '可行性文档 5.1',
    fill: () => 83,
  },
  {
    key: 'BatteryCycleCount',
    label: '电池循环次数',
    serviceSupport: 'unknown',
    reason: '候选来源 root\\wmi:BatteryCycleCount，部分机型不实现该计数。',
    reference: '可行性文档 5.1',
    fill: () => 214,
  },
]

/** 传感器（CPU/GPU/SSD 温度）：目前连数据源都没有，属于"很可能长期缺失"的一类。 */
export const MOCK_SENSORS = {
  serviceSupport: 'no',
  reason: '未找到 CPU/GPU/SSD 温度的用户态来源；候选是 ACPI 热区 MSAcpi_ThermalZoneTemperature（多数机型不实现或只给一个粗糙分区）或第三方内核驱动。',
  reference: '可行性文档 5.1「cpu/gpu/ssd 温度」',
}

/** 风扇转速：0x0802 的语义还没定，要用之前必须先确认它到底是不是转速。 */
export const MOCK_FANS = {
  serviceSupport: 'unknown',
  reason: '候选来源是 0x0802 返回的 out[1..2]（实测 1776~2838，量级与转速吻合但与封装功率混淆），语义确认前不能当成转速用。',
  reference: '可行性文档 5.1「fans[].rpm」+ research/conclusions.md 第 388 行',
}

/**
 * 服务已经能提供的字段，列在这里是为了在"数据来源"面板里能对照展示，
 * 同时也作为"全量示例数据"模式的字段清单。
 */
export const SERVICE_FIELDS = [
  { key: 'BatteryPercent', label: '电量百分比' },
  { key: 'PluggedIn', label: '是否接入适配器' },
  { key: 'ChargeStartPercent', label: '开始充电阈值' },
  { key: 'ChargeStopPercent', label: '停止充电阈值' },
  { key: 'PerformanceMode', label: '性能模式' },
  { key: 'SystemLoadW', label: '系统负载功率（服务派生）' },
]

/** 演示/占位用的风扇上限；等服务端给出机型参数后应改为由 Capabilities 提供。 */
export const MOCK_FAN_MAX_RPM = 6000
