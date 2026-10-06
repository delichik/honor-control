import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { Icon } from './Icon.jsx'

/**
 * 设计稿里的那套控件（ToggleSwitch / Slider / SelectorBar / 单选卡 / 统计块 / 设置行）。
 *
 * 为什么不用 Fluent 的同名控件：Fluent 的开关、滑块、单选卡尺寸与描边是按 32px 控件设计的，
 * 和设计稿里 40×20 的开关、4px 轨道 + 20px 圆形滑块的规格对不上；混着用一眼就能看出两套体系。
 * 这里的每个尺寸都照抄设计稿的 CSS。
 */

/** ToggleSwitch：40×20 轨道 + 12/14px 滑块，选中时轨道用强调色。 */
export function ToggleSwitch({ checked = false, onChange, disabled = false, label, ariaLabel }) {
  const toggle = () => {
    if (disabled) return
    onChange?.(!checked)
  }

  return (
    <span
      className="hc-tswitch"
      role="switch"
      aria-checked={checked}
      aria-disabled={disabled || undefined}
      aria-label={ariaLabel ?? (typeof label === 'string' ? label : undefined)}
      tabIndex={disabled ? -1 : 0}
      onClick={toggle}
      onKeyDown={(event) => {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault()
          toggle()
        }
      }}
    >
      <span className="hc-tswitch-track">
        <span className="hc-tswitch-knob" />
      </span>
      {label ? <span className="hc-tswitch-label">{label}</span> : null}
    </span>
  )
}

/** Slider：原生 range 重画轨道与滑块，右侧固定宽度的数值。 */
export function Slider({ min = 0, max = 100, step = 1, value, onChange, disabled = false, format }) {
  const percent = max === min ? 0 : ((value - min) / (max - min)) * 100
  return (
    <div className="hc-slider" data-disabled={disabled ? 'true' : undefined} style={{ '--pct': `${percent}%` }}>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange?.(Number(event.target.value))}
      />
      <span className="hc-slider-value">{format ? format(value) : value}</span>
    </div>
  )
}

/**
 * SelectorBar：分段控件。
 *
 * 两种用法：给了 `onChange` 就是可点的分段按钮；没给就是只读指示器
 * （首页的性能模式用它——首页只显示"现在是什么模式"，改模式要走性能设置页）。
 */
export function SelectorBar({ items, value, onChange, ariaLabel }) {
  const [pill, setPill] = useState({ left: 0, width: 0 })
  const containerRef = useRef(null)
  const itemsRef = useRef({})

  const sync = useCallback(() => {
    const element = itemsRef.current[value]
    if (!element) return
    setPill({ left: element.offsetLeft, width: element.offsetWidth })
  }, [value])

  // 首次定位必须在**绘制之前**完成：滑块有 left/width 的 250ms 过渡，
  // 若在 useEffect（绘制之后）里量，首帧会先画成 0 宽、再滑过去——
  // 视觉上是"选中块从左边飞过来"，截图里则可能永远停在 0 宽（虚拟时间下过渡不推进）。
  useLayoutEffect(() => {
    sync()
    const container = containerRef.current
    if (!container || typeof ResizeObserver === 'undefined') return undefined
    const observer = new ResizeObserver(sync)
    observer.observe(container)
    return () => observer.disconnect()
  }, [sync])

  const interactive = typeof onChange === 'function'

  return (
    <div
      className="hc-selectorbar"
      ref={containerRef}
      role={interactive ? 'tablist' : 'img'}
      aria-label={ariaLabel}
    >
      <span className="hc-selectorbar-pill" style={{ left: pill.left, width: pill.width }} />
      {items.map((item) => {
        const selected = item.id === value
        const common = {
          ref: (element) => {
            itemsRef.current[item.id] = element
          },
          className: 'hc-sb-item',
          'data-selected': selected ? 'true' : undefined,
          'aria-selected': selected,
        }

        if (!interactive) {
          return (
            <span key={item.id} {...common}>
              {item.icon ? <Icon name={item.icon} /> : null}
              {item.label}
            </span>
          )
        }

        return (
          <button key={item.id} type="button" {...common} role="tab" onClick={() => onChange(item.id)}>
            {item.icon ? <Icon name={item.icon} /> : null}
            {item.label}
          </button>
        )
      })}
    </div>
  )
}

/** 单选卡：左侧圆点、标题带图标、下面一行关键指标。 */
export function RadioCard({ selected, onSelect, title, icon, metrics, disabled = false }) {
  return (
    <button
      type="button"
      className="hc-radio-card"
      aria-selected={selected}
      aria-disabled={disabled || undefined}
      onClick={() => {
        if (!disabled) onSelect?.()
      }}
    >
      <span className="hc-radio-dot" />
      <span className="hc-radio-body">
        <span className="hc-radio-title">
          {icon ? <Icon name={icon} /> : null}
          {title}
        </span>
        {metrics ? <span className="hc-radio-metrics">{metrics}</span> : null}
      </span>
    </button>
  )
}

/** 统计块：小标签 + 大数字 + 单位，跨列由 span 决定。 */
export function StatCard({ label, value, unit, span = 3 }) {
  return (
    <section className={`hc-card hc-stat-card hc-sp${span}`}>
      <span className="hc-stat-k">{label}</span>
      <span className="hc-stat-v">
        <b>{value}</b>
        {unit ? <i>{unit}</i> : null}
      </span>
    </section>
  )
}

/** 设置行：左边标题与说明，右边控件。 */
export function SettingRow({ title, desc, children }) {
  return (
    <div className="hc-setting">
      <div className="hc-setting-body">
        <div className="hc-setting-title">{title}</div>
        {desc ? <div className="hc-setting-desc">{desc}</div> : null}
      </div>
      {children}
    </div>
  )
}

/** 只读的设置值（与控件同一列宽，保证多行右边缘对齐）。 */
export function SettingValue({ children }) {
  return <div className="hc-setting-value">{children}</div>
}
