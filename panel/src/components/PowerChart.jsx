import { useId } from 'react'
import { useElementWidth } from '../app/useElementWidth.js'

/**
 * 电池输入功率曲线 —— 设计稿里的 `canvas#chart`。
 *
 * 用 SVG 重画（原稿是 canvas）：同一套绘制规则，但能跟着主题变量自动换色，
 * 也不需要按 devicePixelRatio 手工缩放。
 *
 * 规则照抄原稿：纵轴固定 ±100 W（多段数据放在同一把尺子上才能比较），
 * 零线是虚线、其余网格是实线；面积填充的基线固定在零线（不是曲线最低点），
 * 这样"零线以上是充电、以下是放电"一眼能看出来。
 */
const PAD = { left: 44, right: 12, top: 12, bottom: 22 }
const MAX_POWER_W = 100

export function PowerChart({ samples = [], height = 170, maxPoints = 180 }) {
  const [wrapRef, width] = useElementWidth()
  const gradientId = useId()

  const w = Math.round(width) || 600
  const h = height
  const iw = Math.max(1, w - PAD.left - PAD.right)
  const ih = Math.max(1, h - PAD.top - PAD.bottom)
  const yFor = (value) =>
    PAD.top + ih / 2 - (Math.min(MAX_POWER_W, Math.max(-MAX_POWER_W, value)) / MAX_POWER_W) * (ih / 2)

  const window = samples.slice(-maxPoints)
  const hasData = window.length >= 2
  const step = hasData ? iw / (window.length - 1) : 0
  const points = window.map((value, index) => [PAD.left + index * step, yFor(value ?? 0)])
  const linePath = points.map(([x, y], index) => `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ')
  const areaPath = hasData
    ? `${linePath} L${points[points.length - 1][0].toFixed(1)} ${yFor(0).toFixed(1)} L${points[0][0].toFixed(1)} ${yFor(0).toFixed(1)} Z`
    : ''
  const last = points[points.length - 1]

  return (
    <div className="hc-chart-wrap" style={{ height }} ref={wrapRef}>
      <svg className="hc-chart-svg" viewBox={`0 0 ${w} ${h}`} role="img" aria-label="电池输入功率曲线">
        <defs>
          <linearGradient id={gradientId} x1="0" y1={PAD.top} x2="0" y2={PAD.top + ih} gradientUnits="userSpaceOnUse">
            <stop offset="0%" stopColor="var(--accent)" stopOpacity="0.33" />
            <stop offset="100%" stopColor="var(--accent)" stopOpacity="0" />
          </linearGradient>
        </defs>

        {[-100, -50, 0, 50, 100].map((value) => (
          <line
            key={value}
            className={value === 0 ? 'hc-chart-grid-zero' : 'hc-chart-grid'}
            x1={PAD.left}
            y1={yFor(value) + 0.5}
            x2={w - PAD.right}
            y2={yFor(value) + 0.5}
          />
        ))}

        {[-100, -50, 0, 50, 100].map((value) => (
          <text key={`t${value}`} className="hc-chart-text" x={PAD.left - 8} y={yFor(value)} textAnchor="end" dominantBaseline="middle">
            {value > 0 ? `+${value}` : value}
          </text>
        ))}

        <text className="hc-chart-text" x={PAD.left} y={h - 8} textAnchor="start">
          −60 s
        </text>
        <text className="hc-chart-text" x={w - PAD.right} y={h - 8} textAnchor="end">
          现在
        </text>

        {hasData ? (
          <>
            <path d={areaPath} fill={`url(#${gradientId})`} />
            <path className="hc-chart-line" d={linePath} />
            <circle className="hc-chart-end-halo" cx={last[0]} cy={last[1]} r="7" />
            <circle cx={last[0]} cy={last[1]} r="4" fill="var(--accent)" />
          </>
        ) : (
          <text className="hc-chart-text" x={PAD.left} y={PAD.top + ih / 2} textAnchor="start">
            正在采集…
          </text>
        )}
      </svg>
    </div>
  )
}
