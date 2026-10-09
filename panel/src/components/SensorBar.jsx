/**
 * 温度传感器读数行 —— 设计稿里的 `.sensor-item`。
 *
 * 一条 0–100 °C 的固定色阶 + 一个指针 + 读数，阈值线来自硬件读数。
 * 色阶是**装饰性贴图**（不是按温度映射的颜色），指针位置才是数据；
 * 多个传感器共用同一把尺子，才能横向比较。
 *
 * tone 由调用方按传感器的 warn/hot 阈值算好（见 data/derive.js 的 temperatureTone），
 * 色点、读数、曲线上的标记同色——同一个温度在整张卡里只有一个颜色。
 */
const TONE_COLOR = {
  ok: 'var(--success)',
  warn: 'var(--caution)',
  hot: 'var(--critical)',
  neutral: 'var(--neutral)',
}

export function SensorBar({ label, tempC, tone = 'neutral', warnC, hotC }) {
  const color = TONE_COLOR[tone] ?? TONE_COLOR.neutral
  const clamped = Math.min(100, Math.max(0, tempC ?? 0))
  const known = tempC !== null && tempC !== undefined
  const warningKnown = typeof warnC === 'number' && Number.isFinite(warnC)
  const criticalKnown = typeof hotC === 'number' && Number.isFinite(hotC)

  return (
    <div className="hc-sensor-item" style={{ color }}>
      <i className="hc-s-dot" />
      <span className="hc-s-name">{label}</span>
      <div
        className="hc-s-bar"
        role="img"
        aria-label={`${label} ${known ? `${tempC.toFixed(1)} °C` : '未知'}${warningKnown ? `，警告阈值 ${warnC} °C` : ''}${criticalKnown ? `，临界阈值 ${hotC} °C` : ''}`}
      >
        {warningKnown ? (
          <i
            className="hc-s-threshold hc-s-threshold--warn"
            style={{ left: `${Math.min(100, Math.max(0, warnC))}%` }}
            title={`警告 ${warnC} °C`}
          />
        ) : null}
        {criticalKnown ? (
          <i
            className="hc-s-threshold hc-s-threshold--hot"
            style={{ left: `${Math.min(100, Math.max(0, hotC))}%` }}
            title={`临界 ${hotC} °C`}
          />
        ) : null}
        <div className="hc-s-needle" style={{ left: `${clamped}%` }} />
      </div>
      <span className="hc-s-val" style={{ color }}>
        {known ? `${tempC.toFixed(1)} °C` : '—'}
      </span>
    </div>
  )
}

export { TONE_COLOR }
