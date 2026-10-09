import { useId } from 'react'
import { useElementWidth } from '../app/useElementWidth.js'

/**
 * 历史曲线 —— 设计稿里 `drawSeriesChart()` 的等价实现（充放电历史 / 功耗历史两页共用）。
 *
 * 与首页那张 60 秒小图的区别（两处都是照原稿来的，不要合并）：
 * - 内边距不同：这里是 padL 48 / padR 14 / padT 14 / padB 26，首页是 44/12/12/22；
 * - 横轴只标两端（"−24 小时" 与 "现在"），首页标的是 −60 s / 现在；
 * - 纵轴刻度由数据范围算成整数（原稿是每张图写死的刻度数组，效果一致）；
 * - 没有末端圆点。
 *
 * 数据是**等间隔的数值数组**（服务端降采样后返回，见可行性文档 5.4），不带时间戳，
 * 因此横轴用下标换算成"相对现在的时间"。
 *
 * 颜色用 CSS 变量字符串（`var(--accent)` 等）传入，换主题时不需要重画。
 */
const PAD = { left: 48, right: 14, top: 14, bottom: 26 }

/** 从范围里挑一个"整"的刻度间隔（1 / 2 / 2.5 / 5 × 10ⁿ），保证刻度是整数。 */
function niceStep(range) {
  const raw = range / 4
  if (!(raw > 0)) return 1
  const magnitude = Math.pow(10, Math.floor(Math.log10(raw)))
  const candidates = [1, 2, 2.5, 5, 10].map((factor) => factor * magnitude)
  return candidates.find((candidate) => candidate >= raw - 1e-9) ?? 10 * magnitude
}

/** 按间隔铺刻度，落在 [min, max] 内的整数倍；0 永远是 0（不要 -0）。 */
function buildTicks(min, max) {
  const step = niceStep(max - min)
  const ticks = []
  for (let value = Math.ceil(min / step) * step; value <= max + 1e-9; value += step) {
    ticks.push(Math.abs(value) < 1e-9 ? 0 : Number(value.toFixed(6)))
  }
  return ticks.length >= 2 ? ticks : [min, max]
}

export function HistoryChart({ series = [], hours = 24, yDomain, height = 300, maxPoints = 400, emptyMessage = '暂无服务历史记录' }) {
  const [wrapRef, width] = useElementWidth()
  const gradientPrefix = useId()

  const w = Math.round(width) || 600
  const h = height
  const iw = Math.max(1, w - PAD.left - PAD.right)
  const ih = Math.max(1, h - PAD.top - PAD.bottom)

  const windowed = series.map((item) => ({ ...item, points: (item.samples ?? []).slice(-maxPoints) }))
  const length = windowed.reduce((max, item) => Math.max(max, item.points.length), 0)
  const flat = windowed.flatMap((item) => item.points).filter((value) => Number.isFinite(value))
  const validCount = flat.length

  const dataMin = flat.length ? Math.min(...flat) : 0
  const dataMax = flat.length ? Math.max(...flat) : 1
  const explicitMin = yDomain?.[0] !== undefined && yDomain[0] !== 'auto' ? yDomain[0] : null
  const explicitMax = yDomain?.[1] !== undefined && yDomain[1] !== 'auto' ? yDomain[1] : null

  const yMin = explicitMin ?? Math.min(0, dataMin)
  // 自动上限向上取整到刻度间隔，避免出现 114/85/57/28 这种刻度
  let yMax = explicitMax
  if (yMax === null) {
    const step = niceStep(Math.max(dataMax - yMin, 1))
    yMax = Math.max(step, Math.ceil(dataMax / step) * step)
  }
  const span = yMax - yMin || 1

  const yFor = (value) => PAD.top + ih - ((value - yMin) / span) * ih
  const xFor = (index) => PAD.left + (length <= 1 ? iw : (index / (length - 1)) * iw)
  const ticks = buildTicks(yMin, yMax)
  const hasZero = yMin < 0 && yMax > 0

  const formatTick = (value) => (value > 0 ? `+${value}` : String(value))

  const formatStart = () => {
    if (hours <= 1 / 60) return `−${Math.max(1, Math.round(hours * 3600))} 秒`
    if (hours < 1) return `−${Math.round(hours * 60)} 分`
    if (hours < 48) return `−${hours.toFixed(hours < 10 ? 1 : 0)} 小时`
    return `−${(hours / 24).toFixed(1)} 天`
  }

  return (
    <div className={height > 200 ? 'hc-chart-wrap hc-chart-wrap--lg' : 'hc-chart-wrap'} style={{ height }} ref={wrapRef}>
      <svg className="hc-chart-svg" viewBox={`0 0 ${w} ${h}`} role="img" aria-label="历史曲线">
        <defs>
          {windowed.map((item, index) =>
            item.fill ? (
              <linearGradient
                key={item.key}
                id={`${gradientPrefix}-${index}`}
                x1="0"
                y1={PAD.top}
                x2="0"
                y2={PAD.top + ih}
                gradientUnits="userSpaceOnUse"
              >
                <stop offset="0%" stopColor={item.color} stopOpacity="0.33" />
                <stop offset="100%" stopColor={item.color} stopOpacity="0" />
              </linearGradient>
            ) : null,
          )}
        </defs>

        {ticks.map((value) => (
          <line
            key={value}
            className={value === 0 && hasZero ? 'hc-chart-grid-zero' : 'hc-chart-grid'}
            x1={PAD.left}
            y1={yFor(value) + 0.5}
            x2={w - PAD.right}
            y2={yFor(value) + 0.5}
          />
        ))}

        {ticks.map((value) => (
          <text
            key={`t${value}`}
            className="hc-chart-text"
            x={PAD.left - 8}
            y={yFor(value)}
            textAnchor="end"
            dominantBaseline="middle"
          >
            {formatTick(value)}
          </text>
        ))}

        <text className="hc-chart-text" x={PAD.left} y={h - 8} textAnchor="start">
          {formatStart()}
        </text>
        <text className="hc-chart-text" x={w - PAD.right} y={h - 8} textAnchor="end">
          现在
        </text>

        {validCount === 0 ? (
          <text className="hc-chart-text" x={PAD.left} y={PAD.top + ih / 2} textAnchor="start">
            {emptyMessage}
          </text>
        ) : null}

        {windowed.map((item, index) => {
          const segments = []
          let segment = []
          item.points.forEach((value, pointIndex) => {
            if (Number.isFinite(value)) segment.push([pointIndex, value])
            else if (segment.length > 0) {
              segments.push(segment)
              segment = []
            }
          })
          if (segment.length > 0) segments.push(segment)
          const drawableSegments = segments.filter((points) => points.length >= 2)
          if (segments.length === 0) return null

          const line = drawableSegments
            .map((points) => points
              .map(([pointIndex, value], indexInSegment) => `${indexInSegment === 0 ? 'M' : 'L'}${xFor(pointIndex).toFixed(1)} ${yFor(value).toFixed(1)}`)
              .join(' '))
            .join(' ')
          // 填充基线：跨零的图以零线为界，否则贴到绘图区底部（原稿的 cfg.zero 分支）
          const baseline = yFor(hasZero ? 0 : yMin)
          const area = drawableSegments
            .filter((points) => item.fill)
            .map((points) => {
              const segmentLine = points
                .map(([pointIndex, value], indexInSegment) => `${indexInSegment === 0 ? 'M' : 'L'}${xFor(pointIndex).toFixed(1)} ${yFor(value).toFixed(1)}`)
                .join(' ')
              return `${segmentLine} L${xFor(points[points.length - 1][0]).toFixed(1)} ${baseline.toFixed(1)} L${xFor(points[0][0]).toFixed(1)} ${baseline.toFixed(1)} Z`
            })
            .join(' ')

          return (
            <g key={item.key}>
              {segments.filter((points) => points.length === 1).map((points) => {
                const [pointIndex, value] = points[0]
                return <circle key={pointIndex} cx={xFor(pointIndex)} cy={yFor(value)} r="3" fill={item.color} />
              })}
              {drawableSegments.length > 0 ? (
                <>
              {item.fill && area ? <path d={area} fill={`url(#${gradientPrefix}-${index})`} /> : null}
              {/* 颜色与虚线用行内 style：SVG 的表现属性优先级低于样式表，
                  写成属性会被 .hc-chart-line 里的 stroke 覆盖掉。 */}
              <path
                className={item.dash ? 'hc-chart-line hc-chart-line--dashed' : 'hc-chart-line'}
                d={line}
                style={{ stroke: item.color, strokeDasharray: item.dash }}
              />
                </>
              ) : null}
            </g>
          )
        })}
      </svg>
    </div>
  )
}
