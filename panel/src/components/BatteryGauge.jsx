import { useCallback, useRef, useState } from 'react'
import { makeStyles, tokens, useFluent } from '@fluentui/react-components'

const useStyles = makeStyles({
  wrap: { display: 'flex', alignItems: 'center', gap: '16px', flexWrap: 'wrap' },
  svg: { flex: '1 1 320px', minWidth: '260px', maxWidth: '520px', touchAction: 'none' },
  readouts: { display: 'flex', flexDirection: 'column', gap: '2px', minWidth: '84px' },
  big: { fontVariantNumeric: 'tabular-nums', lineHeight: '1.1' },
  hint: { color: tokens.colorNeutralForeground3 },
})

// SVG 的坐标系；所有位置都用百分比换算，缩放窗口不会错位。
const VIEW = { width: 300, height: 118 }
const BODY = { x: 8, y: 14, width: 258, height: 90, radius: 18 }
const INNER_PAD = 7
const AXIS = {
  x: BODY.x + INNER_PAD,
  width: BODY.width - INNER_PAD * 2,
}

/**
 * 电池图形。
 *
 * 一根 0–100 的轴上同时表达三件事：当前电量、充电窗口（开始/停止阈值之间）、阈值位置。
 * 设置页把阈值标记做成可拖动的，首页只读——交互差异由 `interactive` 控制，
 * 但两者的渲染完全一致，避免"首页和设置页显示不一样"。
 */
export function BatteryGauge({
  percent = 0,
  startPercent = 40,
  stopPercent = 70,
  charging = false,
  interactive = false,
  onChangeStart,
  onChangeStop,
}) {
  const styles = useStyles()
  // SVG 的表现属性（fill/stroke）不支持 var()，而 Fluent 的 tokens 全是 var(--…) 字符串，
  // 直接塞进属性会渲染不出颜色。所以 SVG 里一律用 useFluent() 解析出的字面量颜色。
  const { theme } = useFluent()
  const svgRef = useRef(null)
  const [dragging, setDragging] = useState(null)

  const xFor = useCallback(
    (value) => AXIS.x + (Math.min(100, Math.max(0, value)) / 100) * AXIS.width,
    [],
  )

  const percentAt = useCallback((clientX) => {
    const rect = svgRef.current?.getBoundingClientRect()
    if (!rect) return null
    const scale = rect.width / VIEW.width
    const x = (clientX - rect.left) / scale
    return ((x - AXIS.x) / AXIS.width) * 100
  }, [])

  const handlePointerDown = (event) => {
    if (!interactive) return
    const value = percentAt(event.clientX)
    if (value === null) return
    // 选离指针更近的那个标记，避免两个标记挨在一起时抢焦点。
    const target = Math.abs(value - startPercent) <= Math.abs(value - stopPercent) ? 'start' : 'stop'
    setDragging(target)
    svgRef.current?.setPointerCapture(event.pointerId)
  }

  const handlePointerMove = (event) => {
    if (!dragging) return
    const raw = percentAt(event.clientX)
    if (raw === null) return
    const value = Math.round(Math.min(100, Math.max(0, raw)))
    if (dragging === 'start') onChangeStart?.(value)
    else onChangeStop?.(value)
  }

  const handlePointerUp = (event) => {
    if (!dragging) return
    setDragging(null)
    svgRef.current?.releasePointerCapture(event.pointerId)
  }

  const fillWidth = Math.max(0, (Math.min(100, Math.max(0, percent)) / 100) * AXIS.width)
  const startX = xFor(startPercent)
  const stopX = xFor(stopPercent)

  return (
    <div className={styles.wrap}>
      <svg
        ref={svgRef}
        className={styles.svg}
        viewBox={`0 0 ${VIEW.width} ${VIEW.height}`}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
        onPointerCancel={handlePointerUp}
        style={{ cursor: interactive ? 'ew-resize' : 'default' }}
        role="img"
        aria-label={`电量 ${Math.round(percent)}%，充电窗口 ${startPercent}% 到 ${stopPercent}%`}
      >
        <defs>
          {/* 充电窗口的斜纹：和电量填充区分开，避免被误读成"已充电" */}
          <pattern id="charge-window" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <rect width="6" height="6" fill="transparent" />
            <line x1="0" y1="0" x2="0" y2="6" stroke={theme.colorBrandStroke1} strokeWidth="2" />
          </pattern>
        </defs>

        {/* 外壳与正极 */}
        <rect
          x={BODY.x}
          y={BODY.y}
          width={BODY.width}
          height={BODY.height}
          rx={BODY.radius}
          fill={theme.colorNeutralBackground2}
          stroke={theme.colorNeutralStroke1}
          strokeWidth="2"
        />
        <rect
          x={BODY.x + BODY.width + 3}
          y={BODY.y + BODY.height / 2 - 16}
          width="10"
          height="32"
          rx="5"
          fill={theme.colorNeutralStroke1}
        />
        <rect
          x={AXIS.x}
          y={BODY.y + INNER_PAD}
          width={AXIS.width}
          height={BODY.height - INNER_PAD * 2}
          rx="12"
          fill={theme.colorNeutralBackground4}
        />

        {/* 充电窗口：先画窗口再画电量，电量始终盖在上面 */}
        <rect
          x={startX}
          y={BODY.y + INNER_PAD}
          width={Math.max(0, stopX - startX)}
          height={BODY.height - INNER_PAD * 2}
          fill="url(#charge-window)"
          opacity="0.35"
        />
        <rect
          x={AXIS.x}
          y={BODY.y + INNER_PAD}
          width={fillWidth}
          height={BODY.height - INNER_PAD * 2}
          rx="12"
          fill={charging ? theme.colorPaletteGreenBackground3 : theme.colorBrandBackground2}
          style={{ transitionProperty: 'width', transitionDuration: '500ms' }}
        />

        {/* 电量数字压在电池里，加半透明底保证在填充区/空槽上都可读 */}
        <rect x={AXIS.x + AXIS.width / 2 - 40} y={BODY.y + BODY.height / 2 - 17} width="80" height="34" rx="8" fill="rgba(0,0,0,0.28)" />
        <text
          x={AXIS.x + AXIS.width / 2}
          y={BODY.y + BODY.height / 2 + 7}
          textAnchor="middle"
          fontSize="22"
          fontWeight="600"
          fill="#ffffff"
          style={{ fontVariantNumeric: 'tabular-nums' }}
        >
          {Math.round(percent)}%
        </text>

        {/* 阈值标记 */}
        {[
          { key: 'start', x: startX, value: startPercent, color: theme.colorPaletteGreenForeground1 },
          { key: 'stop', x: stopX, value: stopPercent, color: theme.colorPaletteRedForeground1 },
        ].map((marker) => (
          <g key={marker.key}>
            <rect
              x={marker.x - 2.5}
              y={BODY.y - 6}
              width="5"
              height={BODY.height + 12}
              rx="2.5"
              fill={marker.color}
              opacity={dragging === marker.key ? 1 : 0.9}
            />
            <text x={marker.x} y={VIEW.height - 4} textAnchor="middle" fontSize="11" fill={theme.colorNeutralForeground3}>
              {marker.value}%
            </text>
          </g>
        ))}
      </svg>

      <div className={styles.readouts}>
        <span className={styles.hint}>充电窗口</span>
        <span className={styles.big} style={{ fontSize: 20, fontWeight: 600 }}>
          {startPercent}% – {stopPercent}%
        </span>
        {interactive ? <span className={styles.hint}>拖动电池上的标记即可调整</span> : null}
      </div>
    </div>
  )
}
