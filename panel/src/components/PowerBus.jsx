import { useLayoutEffect, useRef, useState } from 'react'
import { formatWatts } from '../data/derive.js'

/**
 * 供电母线 —— 设计稿里的 `.bus-wire`。
 *
 * 几何**按容器实际像素重算**（不是固定 viewBox 缩放）：viewBox 与元素同尺寸，
 * 虚线间距、箭头大小在任何宽度下都均匀，窗口缩放时自动重画。
 * 这是设计稿的明确做法（原稿注释：几何直接写在像素坐标里，虚线不会被非等比缩放拉长）。
 *
 * 功率数字标在**它实际流过的线路上**：适配器 → 母线 → 系统负载，电池挂在母线上
 * （充入时向下流、放电时向上流）。这样读图就能对上号，不需要额外的图例。
 */
export function PowerBus({ state, adapterPowerW, systemLoadW, batteryPowerW }) {
  const wrapRef = useRef(null)
  const [size, setSize] = useState({ width: 0, height: 0 })

  useLayoutEffect(() => {
    const element = wrapRef.current
    if (!element) return undefined
    const measure = () => {
      const rect = element.getBoundingClientRect()
      setSize({ width: Math.round(rect.width), height: Math.round(rect.height) })
    }
    measure()
    if (typeof ResizeObserver === 'undefined') return undefined
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const { width: W, height: H } = size
  const drawable = W >= 60 && H >= 80
  const mx = Math.round(W / 2)
  const uy = Math.round(H * 0.25)
  const my = Math.round(H * 0.5)
  const ly = Math.round(H * 0.75)

  const paths = drawable
    ? {
        'hc-w-ac': `M0 ${uy} H ${mx}`,
        'hc-w-dn': `M ${mx} ${uy} V ${my}`,
        'hc-w-bat': `M ${mx} ${my} H ${W}`,
        'hc-w-dn2': `M ${mx} ${my} V ${ly}`,
        'hc-w-ld': `M ${mx} ${ly} H 0`,
        'hc-s-ac': `M0 ${uy} H ${mx}`,
        'hc-s-dn': `M ${mx} ${uy} V ${my}`,
        'hc-s-bat': `M ${mx} ${my} H ${W}`,
        'hc-s-rev': `M ${W} ${my} H ${mx}`,
        'hc-s-dn2': `M ${mx} ${my} V ${ly}`,
        'hc-s-ld': `M ${mx} ${ly} H 0`,
      }
    : {}

  const batteryText = formatWatts(batteryPowerW, { signed: true })
  const batteryKnown = batteryPowerW !== null && batteryPowerW !== undefined
  const batteryTone =
    batteryKnown && batteryPowerW > 0.6 ? 'var(--success)' : batteryKnown && batteryPowerW < -0.6 ? 'var(--caution)' : 'var(--text-primary)'

  return (
    <div className="hc-bus-wire" data-state={state} ref={wrapRef}>
      {drawable ? (
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" aria-hidden="true">
          <path className="hc-wire hc-w-ac" d={paths['hc-w-ac']} />
          <path className="hc-wire hc-w-dn" d={paths['hc-w-dn']} />
          <path className="hc-wire hc-w-bat" d={paths['hc-w-bat']} />
          <path className="hc-wire hc-w-dn2" d={paths['hc-w-dn2']} />
          <path className="hc-wire hc-w-ld" d={paths['hc-w-ld']} />
          <path className="hc-pulse hc-s-ac" d={paths['hc-s-ac']} />
          <path className="hc-pulse hc-s-dn" d={paths['hc-s-dn']} />
          <path className="hc-pulse hc-s-bat" d={paths['hc-s-bat']} />
          <path className="hc-pulse hc-s-rev" d={paths['hc-s-rev']} />
          <path className="hc-pulse hc-s-dn2" d={paths['hc-s-dn2']} />
          <path className="hc-pulse hc-s-ld" d={paths['hc-s-ld']} />
          <g className="hc-ghost hc-ghost--charge" style={{ fill: 'var(--success)' }}>
            <path d={`M ${W - 5} ${my} l-9 -5.5 v11 z`} />
          </g>
          <g className="hc-ghost hc-ghost--discharge" style={{ fill: 'var(--accent)' }}>
            <path d={`M ${mx + 5} ${my} l9 -5.5 v11 z`} />
          </g>
        </svg>
      ) : null}

      <span className="hc-wire-val hc-wire-val--ac">
        {adapterPowerW === null || adapterPowerW === undefined ? (
          '—'
        ) : (
          <>
            <b>{formatWatts(adapterPowerW)}</b> W
          </>
        )}
      </span>

      <span className="hc-wire-val hc-wire-val--load">
        {systemLoadW === null || systemLoadW === undefined ? (
          '—'
        ) : (
          <>
            <b>{formatWatts(systemLoadW)}</b> W
          </>
        )}
      </span>

      <span className="hc-wire-val hc-wire-val--batt">
        {batteryKnown ? (
          <>
            <b style={{ color: batteryTone }}>{batteryText}</b> W
          </>
        ) : (
          '—'
        )}
      </span>
    </div>
  )
}

/** 母线状态机：未接适配器一律按放电画；否则按电池功率的 ±0.6 W 判定。 */
export function busStateOf({ pluggedIn, chargeState }) {
  if (pluggedIn === null || pluggedIn === undefined) return 'unknown'
  if (!pluggedIn) return 'discharging'
  if (chargeState === 'charging') return 'charging'
  if (chargeState === 'discharging') return 'discharging'
  return 'idle'
}
