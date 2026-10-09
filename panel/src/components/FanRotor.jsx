import { FAN_MAX_RPM } from '../data/fanCurve.js'

/**
 * 风扇转子 —— 设计稿里的 `.fan-rotor` + `.rotor`。
 *
 * 几何按原稿的参数化叶片生成：前缘外凸、后缘内凹、中段收窄，5 片每片转 72°。
 * 转速用动画周期表达（非线性映射：低转速段也看得出在转，高转速段有明显加速感），
 * 超过 85% 上限时叶片转成警示色。转速本身是数据，所以减弱动效时是**降速**而不是停转。
 */

/** 参数化叶片路径：P(r,deg) 围绕 (cx,cy)，sweep 是叶片整体的后掠角。 */
function bladePath(cx, cy, r0, r1, a0, a1, sweep) {
  const rad = (deg) => (deg * Math.PI) / 180
  const point = (r, deg) => [cx + r * Math.cos(rad(deg)), cy + r * Math.sin(rad(deg))]
  const round = ([x, y]) => `${x.toFixed(2)} ${y.toFixed(2)}`

  const rf = point(r0, a0)
  const tf = point(r1, a0 + sweep)
  const c1 = point((r0 + r1) / 2 + 2.5, a0 + sweep * 0.42)
  const c2 = point(r1 + 1.5, (a0 + a1) / 2 + sweep)
  const c3 = point(r0 + 4, a1 + sweep * 0.42)
  const tb = point(r1, a1 + sweep)
  const rb = point(r0, a1)

  return `M${round(rf)} Q ${round(c1)} ${round(tf)} Q ${round(c2)} ${round(tb)} Q ${round(c3)} ${round(rb)} Z`
}

const BLADE = bladePath(32, 32, 7, 26, 0, 22, 48)
const BLADE_ANGLES = [0, 72, 144, 216, 288]

/** 转速 → 每转耗时（秒）：0 → 2.60s，3000 → 1.20s，6000 → 0.25s。 */
export function secondsPerTurn(rpm, maxRpm = FAN_MAX_RPM) {
  const ratio = Math.min(1, Math.max(0, rpm / maxRpm))
  return 2.6 - Math.pow(ratio, 0.75) * 2.35
}

export function FanRotor({ label, rpm = 0, maxRpm = FAN_MAX_RPM }) {
  const hasRatedMaximum = typeof maxRpm === 'number' && Number.isFinite(maxRpm) && maxRpm > 0
  const ratio = hasRatedMaximum ? Math.min(1, Math.max(0, rpm / maxRpm)) : null
  const stopped = !hasRatedMaximum || rpm < 120
  const hot = hasRatedMaximum && ratio > 0.85

  return (
    <div className="hc-fan-mini">
      <span className="hc-fan-rotor">
        <svg
          className="hc-rotor-svg"
          viewBox="0 0 64 64"
          role="img"
          aria-label={`${label} 转速 ${Math.round(rpm)} 转每分`}
        >
          <circle className="hc-fan-ring" cx="32" cy="32" r="29.5" />
          <g
            className={stopped ? 'hc-rotor is-still' : 'hc-rotor'}
            style={{
              '--spin': `${secondsPerTurn(rpm, hasRatedMaximum ? maxRpm : undefined).toFixed(2)}s`,
              fill: hot ? 'var(--caution)' : 'var(--accent)',
            }}
          >
            {BLADE_ANGLES.map((angle) => (
              <path key={angle} d={BLADE} transform={`rotate(${angle} 32 32)`} />
            ))}
          </g>
          <circle className="hc-fan-hub" cx="32" cy="32" r="7" />
          <circle className="hc-fan-pin" cx="32" cy="32" r="2.2" />
        </svg>
      </span>
      <span className="hc-fan-name">{label}</span>
      <span className="hc-fan-rpm">
        <b>{Math.round(rpm).toLocaleString('en-US')}</b> RPM
      </span>
    </div>
  )
}
