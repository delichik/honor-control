/**
 * 风扇策略曲线的示例数据。
 *
 * ⚠️ 服务当前拿不到风扇曲线：写路径要经过荣耀内核驱动（\\.\WDT0001）+ NLD 风扇库，
 * 没有用户态通道（见可行性文档 5.1 与 research/conclusions.md 第 212 行）。
 * 因此这里的曲线**只是示例策略**，用于把"性能模式 → 散热策略"这层关系画出来；
 * 等确认了读/写通道，改成从 Capabilities 拿即可。
 */

/** 默认示例曲线：温度(°C) → 转速(RPM) */
export const EXAMPLE_FAN_CURVE = [
  { t: 35, r: 1200 },
  { t: 50, r: 1800 },
  { t: 65, r: 3000 },
  { t: 80, r: 4400 },
  { t: 95, r: 5600 },
]

/** 不同性能模式对应不同的散热策略：高能模式更早拉高转速。 */
export const EXAMPLE_CURVES_BY_MODE = {
  1: EXAMPLE_FAN_CURVE,
  2: [
    { t: 30, r: 1400 },
    { t: 45, r: 2200 },
    { t: 60, r: 3400 },
    { t: 75, r: 4800 },
    { t: 90, r: 6000 },
  ],
}

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
