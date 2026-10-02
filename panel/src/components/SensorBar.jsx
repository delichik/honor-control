import { makeStyles, tokens } from '@fluentui/react-components'

const useStyles = makeStyles({
  row: {
    display: 'grid',
    gridTemplateColumns: '76px 1fr 96px',
    alignItems: 'center',
    gap: '10px',
  },
  label: { color: tokens.colorNeutralForeground2 },
  track: {
    position: 'relative',
    height: '10px',
    borderRadius: '5px',
    background: 'var(--temp-gradient)',
    // 视觉上留出两端，避免指针贴边被裁掉
    margin: '0 6px',
  },
  needle: {
    position: 'absolute',
    top: '-5px',
    width: '3px',
    height: '20px',
    borderRadius: '2px',
    backgroundColor: tokens.colorNeutralForeground1,
    boxShadow: '0 0 0 2px rgba(255,255,255,0.55)',
    transform: 'translateX(-50%)',
    transitionProperty: 'left',
    transitionDuration: '240ms',
    transitionTimingFunction: 'cubic-bezier(0.33, 0, 0.67, 1)',
  },
  value: {
    textAlign: 'right',
    fontVariantNumeric: 'tabular-nums',
  },
})

/**
 * 温度条：0–100 °C 线性映射到色阶上。
 *
 * 之所以固定成 0–100 而不是自适应量程：多个传感器要在**同一把尺子**上比较，
 * 自适应会让"CPU 60 °C"和"SSD 45 °C"的指针看起来差不多。
 */
export function SensorBar({ label, tempC, tone }) {
  const styles = useStyles()
  const clamped = Math.min(100, Math.max(0, tempC ?? 0))

  const colorFor = (value) => {
    if (value === 'hot') return tokens.colorPaletteRedForeground1
    if (value === 'warn') return tokens.colorPaletteDarkOrangeForeground1
    return tokens.colorNeutralForeground1
  }

  return (
    <div className={styles.row}>
      <span className={styles.label}>{label}</span>
      <div className={styles.track}>
        <div className={styles.needle} style={{ left: `${clamped}%` }} />
      </div>
      <span className={styles.value} style={{ color: colorFor(tone) }}>
        {(tempC ?? 0).toFixed(1)} °C
      </span>
    </div>
  )
}
