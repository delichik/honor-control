/**
 * 风扇策略曲线（示例）。
 *
 * ⚠️ 服务当前拿不到风扇曲线：写路径要经过荣耀内核驱动（\\.\WDT0001）+ NLD 风扇库，
 * 没有用户态通道（见可行性文档 5.1 与 research/conclusions.md 第 212 行）。
 * 因此这里的曲线**只是示例策略**，用于把"性能模式 → 散热策略"这层关系画出来；
 * 等确认了读/写通道，改成从 Capabilities 拿即可。界面上必须带"示例"角标。
 *
 * 两套策略：
 * - `auto`：性能模式决定的自动策略（设计稿里的 smart/high 两条曲线，转速随温度非线性上升）；
 * - `custom`：控制点分段线性插值（性能设置页里可拖动的曲线）。
 */

/** 示例曲线纵轴和模拟风扇上限；实时 RPM 不会用它推算转速百分比。 */
export const FAN_MAX_RPM = 6000

/** 自动策略的起转温度与满转温度：高能模式更早拉高转速。 */
export const AUTO_CURVE_BY_MODE = {
  1: { lo: 48, hi: 100, baseRpm: 1100, exponent: 1.15 },
  2: { lo: 42, hi: 96, baseRpm: 1100, exponent: 1.15 },
}

/** 默认的自定义控制点：温度(°C) → 转速(RPM)。 */
export const EXAMPLE_FAN_CURVE = [
  { t: 35, r: 1200 },
  { t: 50, r: 1800 },
  { t: 65, r: 3000 },
  { t: 80, r: 4400 },
  { t: 95, r: 5600 },
]

/** 分段线性插值：给定温度求策略要求的转速。 */
export function rpmForTemperature(curve, temperatureC) {
  if (!curve?.length || temperatureC === null || temperatureC === undefined) return null
  if (temperatureC <= curve[0].t) return curve[0].r
  const last = curve[curve.length - 1]
  if (temperatureC >= last.t) return last.r

  for (let i = 0; i < curve.length - 1; i += 1) {
    const a = curve[i]
    const b = curve[i + 1]
    if (temperatureC >= a.t && temperatureC <= b.t) {
      const ratio = (temperatureC - a.t) / (b.t - a.t)
      return a.r + ratio * (b.r - a.r)
    }
  }
  return last.r
}

/**
 * 自动策略：`rpm = base + k^exponent × (max − base)`，`k` 是温度在 [lo, hi] 上的归一化值。
 *
 * 用幂函数而不是直线：真实风扇在起转温度附近变化平缓，接近上限时才陡起来，
 * 画成直线会让"50 °C 就快满转"这种错误印象出现在图上。
 */
export function autoCurveRpm(performanceMode, temperatureC) {
  const profile = AUTO_CURVE_BY_MODE[performanceMode] ?? AUTO_CURVE_BY_MODE[1]
  const k = Math.min(1, Math.max(0, (temperatureC - profile.lo) / (profile.hi - profile.lo)))
  return profile.baseRpm + Math.pow(k, profile.exponent) * (FAN_MAX_RPM - profile.baseRpm)
}

/**
 * 统一的转速策略入口：曲线图与"实际工作点"都用它，避免两处各算一套。
 * policy: { kind: 'auto' | 'custom' | 'max', mode, points }
 */
export function curveRpm(policy, temperatureC) {
  if (policy?.kind === 'max') return FAN_MAX_RPM
  if (policy?.kind === 'custom') {
    const value = rpmForTemperature(policy.points ?? EXAMPLE_FAN_CURVE, temperatureC)
    return Math.min(FAN_MAX_RPM, Math.max(0, value ?? 0))
  }
  return autoCurveRpm(policy?.mode ?? 1, temperatureC)
}
