import { PlugConnected24Regular, Gauge24Regular, BatteryCharge24Regular } from '@fluentui/react-icons'
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components'
import { chargeStateOf, formatWatts } from '../data/derive.js'

const useStyles = makeStyles({
  grid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(104px, 1fr) 128px minmax(104px, 1fr)',
    justifyItems: 'center',
    alignItems: 'center',
    rowGap: '2px',
  },
  node: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '2px',
    padding: '8px 12px',
    borderRadius: '8px',
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke3}`,
    minWidth: '104px',
  },
  nodeOff: {
    opacity: 0.45,
  },
  power: {
    fontVariantNumeric: 'tabular-nums',
  },
  wireCell: {
    position: 'relative',
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '4px',
  },
  wire: {
    width: '100%',
    height: '2px',
    backgroundImage: `repeating-linear-gradient(90deg, ${tokens.colorBrandStroke1} 0 8px, transparent 8px 16px)`,
    backgroundSize: '16px 2px',
    animationName: 'panel-wire-flow',
    animationDuration: '1s',
    animationIterationCount: 'infinite',
    animationTimingFunction: 'linear',
    opacity: 0.35,
  },
  wireActive: {
    opacity: 1,
  },
  wireIdle: {
    animationPlayState: 'paused',
  },
  wireReverse: {
    animationDirection: 'reverse',
  },
  vertical: {
    width: '2px',
    height: '26px',
    backgroundImage: `repeating-linear-gradient(180deg, ${tokens.colorBrandStroke1} 0 8px, transparent 8px 16px)`,
    backgroundSize: '2px 16px',
    animationName: 'panel-wire-flow-vertical',
    animationDuration: '1s',
    animationIterationCount: 'infinite',
    animationTimingFunction: 'linear',
    opacity: 0.35,
  },
  batteryNode: {
    marginTop: '2px',
  },
})

/**
 * 功率流向图。
 *
 * 功率数字标在**它实际流过的线上**，而不是塞进节点里：适配器 → 母线 → 系统负载，
 * 电池挂在母线上（充入时向下流，放电时向上流）。这样读图就能对上号：
 * 拔掉适配器时适配器节点变暗、母线只走 适配器→负载 的静态段。
 *
 * 流向动画只表达方向，不参与计算；数值一律来自 derive.js 的统一口径。
 */
export function PowerFlow({ pluggedIn, adapterPowerW, systemLoadW, batteryPowerW }) {
  const styles = useStyles()
  const state = chargeStateOf(batteryPowerW)
  const flowActive = pluggedIn
  const batteryActive = state === 'charging' || state === 'discharging'

  return (
    <div className={styles.grid}>
      <div className={mergeClasses(styles.node, !pluggedIn && styles.nodeOff)}>
        <PlugConnected24Regular />
        <Text size={200}>适配器</Text>
        <Text size={400} weight="semibold" className={styles.power}>
          {pluggedIn ? `${formatWatts(adapterPowerW)} W` : '未接入'}
        </Text>
      </div>

      <div className={styles.wireCell}>
        <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
          {pluggedIn ? '供电' : '无外部供电'}
        </Text>
        <div
          className={mergeClasses(
            styles.wire,
            flowActive ? styles.wireActive : styles.wireIdle,
            !pluggedIn && styles.wireReverse,
          )}
        />
      </div>

      <div className={styles.node}>
        <Gauge24Regular />
        <Text size={200}>系统负载</Text>
        <Text size={400} weight="semibold" className={styles.power}>
          {formatWatts(systemLoadW)} W
        </Text>
      </div>

      <div />
      <div
        className={mergeClasses(
          styles.vertical,
          batteryActive ? styles.wireActive : styles.wireIdle,
          state === 'discharging' && styles.wireReverse,
        )}
      />
      <div />

      <div />
      <div className={mergeClasses(styles.node, styles.batteryNode)}>
        <BatteryCharge24Regular />
        <Text size={200}>电池</Text>
        <Text size={400} weight="semibold" className={styles.power}>
          {formatWatts(batteryPowerW, { signed: true })} W
        </Text>
      </div>
      <div />
    </div>
  )
}
