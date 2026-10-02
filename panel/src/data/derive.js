import { POWER_EPSILON_W } from './contract.js'

/**
 * 派生口径。
 *
 * 这些公式**必须与服务端一致**（可行性文档 5.3）。历史上原型里首页的"系统负载"是前端派生的，
 * 而监控页的负载是另一条独立序列，两者会给出不同的数字；这里把口径集中到一个文件，
 * 服务端接上采样器以后直接复用同一套定义。
 */

/** 充放电状态：以 ±0.6 W 判定，避免待机时的微小读数被当成充放电。 */
export function chargeStateOf(batteryPowerW) {
  if (batteryPowerW === null || batteryPowerW === undefined) return 'unknown'
  if (batteryPowerW > POWER_EPSILON_W) return 'charging'
  if (batteryPowerW < -POWER_EPSILON_W) return 'discharging'
  return 'idle'
}

export const CHARGE_STATE_LABEL = {
  charging: '充电中',
  discharging: '放电中',
  idle: '未充放',
  unknown: '未知',
}

/**
 * 系统负载功率。
 *
 * - 接着适配器：适配器功率 − 充入电池的功率（电池没在充电时就是适配器功率）；
 * - 拔掉适配器：电池放出的功率就是整机负载。
 *
 * 服务端如果直接给了 SystemLoadW，优先用服务的值——但两者的定义必须保持这一套，
 * 否则首页与历史页会显示不同的数字。
 */
export function deriveSystemLoadW({ serviceValue, adapterPowerW, batteryPowerW, pluggedIn }) {
  if (serviceValue !== null && serviceValue !== undefined) return serviceValue
  if (pluggedIn) {
    if (adapterPowerW === null || adapterPowerW === undefined) return null
    return Math.max(0, adapterPowerW - Math.max(0, batteryPowerW ?? 0))
  }
  if (batteryPowerW === null || batteryPowerW === undefined) return null
  return Math.abs(Math.min(0, batteryPowerW))
}

/** 由电量与功率粗略估算剩余/充满时间，仅用于文案提示，不参与控制。 */
export function estimateMinutes({ chargeState, batteryPercent, batteryPowerW, designCapacityWh, chargeStopPercent }) {
  if (!designCapacityWh || !batteryPowerW) return null
  const remainingWh = (designCapacityWh * batteryPercent) / 100
  if (chargeState === 'charging' && batteryPercent < (chargeStopPercent ?? 100)) {
    const targetWh = (designCapacityWh * (chargeStopPercent ?? 100)) / 100
    return Math.max(0, Math.round(((targetWh - remainingWh) / batteryPowerW) * 60))
  }
  if (chargeState === 'discharging') {
    return Math.max(0, Math.round((remainingWh / Math.abs(batteryPowerW)) * 60))
  }
  return null
}

/**
 * 序列统计。
 *
 * dt 由 hours/点数推导（服务端返回的是等间隔序列，不带时间戳），
 * Wh 的积分口径是 Σ(值 × dt)，与服务端保持一致。
 */
export function summarizeSeries(samples, hours) {
  if (!samples || samples.length === 0) {
    return { peak: 0, average: 0, wh: 0 }
  }
  const dt = hours / samples.length
  let peak = Number.NEGATIVE_INFINITY
  let sum = 0
  for (const value of samples) {
    if (value > peak) peak = value
    sum += value
  }
  return { peak, average: sum / samples.length, wh: sum * dt }
}

/** 充放电历史的统计：分别累计充入/放出的时长与电量。 */
export function summarizeChargeSeries(samples, hours) {
  const dt = hours / Math.max(1, samples?.length ?? 0)
  let chargeHours = 0
  let dischargeHours = 0
  let chargeWh = 0
  let dischargeWh = 0
  for (const value of samples ?? []) {
    // 同样使用 ±0.6 W 的归零阈值，否则待机时段会被算成"一直在充电"。
    if (value > POWER_EPSILON_W) {
      chargeHours += dt
      chargeWh += value * dt
    } else if (value < -POWER_EPSILON_W) {
      dischargeHours += dt
      dischargeWh += -value * dt
    }
  }
  return { chargeHours, dischargeHours, chargeWh, dischargeWh }
}

/** 统一的功率格式化：带符号、一位小数。 */
export function formatWatts(value, { signed = false } = {}) {
  if (value === null || value === undefined) return '—'
  const sign = signed && value > 0 ? '+' : ''
  return `${sign}${value.toFixed(1)}`
}

export function formatTemperature(value) {
  if (value === null || value === undefined) return '—'
  return value.toFixed(1)
}

/** 温度配色：与原型一致的分档，供温度条/数字共用。 */
export function temperatureTone(value, warnC = 45, hotC = 55) {
  if (value === null || value === undefined) return 'neutral'
  if (value >= hotC) return 'hot'
  if (value >= warnC) return 'warn'
  return 'ok'
}
