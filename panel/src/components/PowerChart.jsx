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

export function PowerChart({ samples = [], hours = 1 / 60, height = 170, maxPoints = 180, emptyMessage = '暂无服务历史记录' }) {
  const [wrapRef, width] = useElementWidth()
  const gradientId = useId()

  const w = Math.round(width) || 600
  const h = height
  const iw = Math.max(1, w - PAD.left - PAD.right)
  const ih = Math.max(1, h - PAD.top - PAD.bottom)
  const yFor = (value) =>
    PAD.top + ih / 2 - (Math.min(MAX_POWER_W, Math.max(-MAX_POWER_W, value)) / MAX_POWER_W) * (ih / 2)

  const window = samples.slice(-maxPoints)
  const finiteCount = window.filter(Number.isFinite).length
  const step = window.length > 1 ? iw / (window.length - 1) : 0
  const segments = []
  let segment = []
  window.forEach((value, index) => {
    if (Number.isFinite(value)) segment.push([PAD.left + (window.length <= 1 ? iw : index * step), yFor(value)])
    else if (segment.length > 0) {
      segments.push(segment)
      segment = []
    }
  })
  if (segment.length > 0) segments.push(segment)
  const drawableSegments = segments.filter((points) => points.length >= 2)
  const lastSegment = segments[segments.length - 1]
  const last = lastSegment?.[lastSegment.length - 1]

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
          −{Math.max(1, Math.round(hours * 3600))} s
        </text>
        <text className="hc-chart-text" x={w - PAD.right} y={h - 8} textAnchor="end">
          现在
        </text>

        {finiteCount > 0 ? (
          <>
            {segments.filter((points) => points.length === 1).map((points) => (
              <circle key={points[0][0]} cx={points[0][0]} cy={points[0][1]} r="3" fill="var(--accent)" />
            ))}
            {drawableSegments.map((points, index) => {
              const linePath = points
                .map(([x, y], pointIndex) => `${pointIndex === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`)
                .join(' ')
              const areaPath = `${linePath} L${points[points.length - 1][0].toFixed(1)} ${yFor(0).toFixed(1)} L${points[0][0].toFixed(1)} ${yFor(0).toFixed(1)} Z`
              return (
                <g key={index}>
                  <path d={areaPath} fill={`url(#${gradientId})`} />
                  <path className="hc-chart-line" d={linePath} />
                </g>
              )
            })}
            {last ? (
              <>
                <circle className="hc-chart-end-halo" cx={last[0]} cy={last[1]} r="7" />
                <circle cx={last[0]} cy={last[1]} r="4" fill="var(--accent)" />
              </>
            ) : null}
          </>
        ) : (
          <text className="hc-chart-text" x={PAD.left} y={PAD.top + ih / 2} textAnchor="start">
            {emptyMessage}
          </text>
        )}
      </svg>
    </div>
  )
}
