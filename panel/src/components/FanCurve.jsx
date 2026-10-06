import { useElementWidth } from '../app/useElementWidth.js'
import { FAN_MAX_RPM, curveRpm } from '../data/fanCurve.js'
import { temperatureTone } from '../data/derive.js'

/**
 * 风扇曲线 —— 设计稿里的 `.curve`。
 *
 * 横轴温度、纵轴转速，**曲线本身就是当前性能模式的散热策略**：模式决定策略，
 * 传感器是曲线上的竖线加一个点（该温度下策略要求多少转速），带光环的实心点是
 * 实际工作点（最高温度 × 实际平均转速）——它应该落在曲线上，偏离说明风扇没跟上策略。
 *
 * 用 SVG 而不是图表库：这张图的每个元素都要和左侧传感器列表**同色对齐**
 * （色点、竖线、点、读数共用一个 tone），图表库要绕一圈才能做到，
 * 而这里直接算坐标更短也更准。
 */
const CP = { padL: 46, padR: 16, padT: 16, padB: 28, h: 210 }

const TONE_COLOR = {
  ok: 'var(--success)',
  warn: 'var(--caution)',
  hot: 'var(--critical)',
  neutral: 'var(--neutral)',
}

export function FanCurve({ sensors = [], fans = [], policy }) {
  const [wrapRef, width] = useElementWidth()
  const W = Math.max(360, Math.round(width) || 560)

  const tx = (t) => CP.padL + (Math.min(100, Math.max(0, t)) / 100) * (W - CP.padL - CP.padR)
  const ry = (r) => CP.h - CP.padB - (Math.min(FAN_MAX_RPM, Math.max(0, r)) / FAN_MAX_RPM) * (CP.h - CP.padB - CP.padT)

  const hasFans = fans.length > 0
  let curvePath = ''
  let areaPath = ''
  if (hasFans) {
    const points = []
    for (let t = 0; t <= 100; t += 1) {
      points.push(`${t === 0 ? 'M' : 'L'}${tx(t).toFixed(1)} ${ry(curveRpm(policy, t)).toFixed(1)}`)
    }
    curvePath = points.join(' ')
    areaPath = `${curvePath} L${tx(100).toFixed(1)} 182 L${tx(0).toFixed(1)} 182 Z`
  }

  const hottest = sensors.reduce(
    (acc, sensor) => (acc === null || sensor.TempC > acc.TempC ? sensor : acc),
    null,
  )
  const averageRpm = hasFans ? fans.reduce((sum, fan) => sum + fan.Rpm, 0) / fans.length : null
  const showOp = hasFans && hottest !== null && averageRpm !== null && hottest.TempC <= 100

  return (
    <div className="hc-curve-wrap" ref={wrapRef}>
      <svg className="hc-curve" viewBox={`0 0 ${W} ${CP.h}`} role="img" aria-label="温度与转速的风扇策略曲线">
        <g className="hc-curve-grid">
          {[0, 25, 50, 75, 100].map((t) => (
            <line key={`x${t}`} x1={tx(t)} y1={CP.padT} x2={tx(t)} y2={CP.h - CP.padB} />
          ))}
          {[0, 3000, 6000].map((r) => (
            <line key={`y${r}`} x1={CP.padL} y1={ry(r)} x2={W - CP.padR} y2={ry(r)} />
          ))}
          {[0, 25, 50, 75, 100].map((t) => (
            <text
              key={`xt${t}`}
              x={tx(t)}
              y={CP.h - 8}
              textAnchor={t === 0 ? 'start' : t === 100 ? 'end' : 'middle'}
            >
              {t === 100 ? '100 °C' : t}
            </text>
          ))}
          {[0, 3000, 6000].map((r) => (
            <text key={`yt${r}`} x={CP.padL - 8} y={ry(r) + 4} textAnchor="end">
              {r === 0 ? '0' : `${r / 1000}k`}
            </text>
          ))}
          <text x={CP.padL - 8} y={CP.padT - 4} textAnchor="end">
            RPM
          </text>
        </g>

        {hasFans ? (
          <>
            <path className="hc-curve-area" d={areaPath} />
            <path className="hc-curve-line" d={curvePath} />
          </>
        ) : null}

        {sensors.map((sensor) => {
          const tone = TONE_COLOR[temperatureTone(sensor.TempC, sensor.WarnC, sensor.HotC)] ?? TONE_COLOR.neutral
          return (
            <g className="hc-curve-mark" key={sensor.Id} style={{ color: tone }}>
              <line
                x1={tx(sensor.TempC)}
                y1={CP.padT}
                x2={tx(sensor.TempC)}
                y2={CP.h - CP.padB}
                stroke={tone}
              />
              {/* 曲线上的点表达"该温度下策略要求多少转速"，没有风扇读数时就不画 */}
              {hasFans ? <circle cx={tx(sensor.TempC)} cy={ry(curveRpm(policy, sensor.TempC))} r="4" fill={tone} /> : null}
            </g>
          )
        })}

        {showOp ? (
          <>
            <circle className="hc-op-halo" r="10" cx={tx(hottest.TempC)} cy={ry(averageRpm)} />
            <circle className="hc-op-dot" r="4.5" cx={tx(hottest.TempC)} cy={ry(averageRpm)} />
          </>
        ) : (
          <>
            <circle className="hc-op-halo" r="10" cx="-99" cy="-99" />
            <circle className="hc-op-dot" r="4.5" cx="-99" cy="-99" />
          </>
        )}
      </svg>
    </div>
  )
}
