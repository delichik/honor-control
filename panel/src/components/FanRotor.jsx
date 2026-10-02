import { makeStyles, tokens, useFluent } from '@fluentui/react-components'

const useStyles = makeStyles({
  wrap: { display: 'flex', alignItems: 'center', gap: '10px' },
  rotor: {
    display: 'block',
    transformBox: 'fill-box',
    transformOrigin: 'center',
    // 转速用动画时长表达：动画只说明"在转"和转得多快，不参与任何计算
    animationName: 'panel-fan-spin',
    animationIterationCount: 'infinite',
    animationTimingFunction: 'linear',
  },
  rpm: { fontVariantNumeric: 'tabular-nums' },
})

/**
 * 风扇转子。
 *
 * 叶片用参数化的后掠形状（前缘外凸、后缘内凹），不是对称花瓣；
 * 转速映射取非线性，低转速段也能看出在转，高转速段有明显加速感。
 */
export function FanRotor({ rpm = 0, maxRpm = 6000, size = 54 }) {
  const styles = useStyles()
  // SVG 属性不支持 var()，Fluent token 是 var(--…) 字符串，所以这里解析成字面量颜色
  const { theme } = useFluent()
  const ratio = Math.min(1, Math.max(0, rpm / maxRpm))
  const stopped = rpm < 120
  const secondsPerTurn = Math.max(0.2, 2.6 - Math.pow(ratio, 0.75) * 2.35)
  const hot = ratio > 0.85

  const blade = 'M0,-9 C7,-15 15,-19 22,-17 C17,-11 13,-5 11,1 C7,-4 3,-7 0,-9 Z'
  const blades = [0, 72, 144, 216, 288]

  return (
    <div className={styles.wrap}>
      <svg width={size} height={size} viewBox="-30 -30 60 60" role="img" aria-label={`风扇转速 ${Math.round(rpm)} 转每分`}>
        <circle cx="0" cy="0" r="27" fill="none" stroke={theme.colorNeutralStroke2} strokeWidth="2" />
        <g
          className={styles.rotor}
          style={{
            animationDuration: `${secondsPerTurn}s`,
            animationPlayState: stopped ? 'paused' : 'running',
          }}
        >
          {blades.map((angle) => (
            <path
              key={angle}
              d={blade}
              transform={`rotate(${angle})`}
              fill={hot ? theme.colorPaletteDarkOrangeBackground3 : theme.colorBrandBackground2}
              stroke={theme.colorNeutralStroke1}
              strokeWidth="0.6"
            />
          ))}
        </g>
        <circle cx="0" cy="0" r="8" fill={theme.colorNeutralBackground3} stroke={theme.colorNeutralStroke1} />
      </svg>
      <span className={styles.rpm}>{Math.round(rpm).toLocaleString('zh-CN')} RPM</span>
    </div>
  )
}
