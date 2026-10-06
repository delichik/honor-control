/**
 * 温度传感器读数行 —— 设计稿里的 `.sensor-item`。
 *
 * 一条 0–100 °C 的固定色阶 + 一个指针 + 读数，外加一条 55 °C 的过热参考线。
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

export function SensorBar({ label, tempC, tone = 'neutral' }) {
  const color = TONE_COLOR[tone] ?? TONE_COLOR.neutral
  const clamped = Math.min(100, Math.max(0, tempC ?? 0))
  const known = tempC !== null && tempC !== undefined

  return (
    <div className="hc-sensor-item" style={{ color }}>
      <i className="hc-s-dot" />
      <span className="hc-s-name">{label}</span>
      <div className="hc-s-bar">
        <div className="hc-s-needle" style={{ left: `${clamped}%` }} />
      </div>
      <span className="hc-s-val" style={{ color }}>
        {known ? `${tempC.toFixed(1)} °C` : '—'}
      </span>
    </div>
  )
}

export { TONE_COLOR }
