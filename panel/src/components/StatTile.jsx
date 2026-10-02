import { Text, makeStyles, tokens } from '@fluentui/react-components'

const useStyles = makeStyles({
  tile: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: tokens.colorNeutralBackground2,
    border: `1px solid ${tokens.colorNeutralStroke3}`,
    minWidth: '92px',
  },
  value: {
    fontVariantNumeric: 'tabular-nums',
    lineHeight: '1.15',
  },
  unit: {
    marginLeft: '3px',
    color: tokens.colorNeutralForeground3,
  },
})

/** 监控页的统计小卡：一个数值 + 单位 + 说明。 */
export function StatTile({ label, value, unit, hint }) {
  const styles = useStyles()
  return (
    <div className={styles.tile}>
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{label}</Text>
      <Text size={500} weight="semibold" className={styles.value}>
        {value}
        {unit ? <span className={styles.unit}>{unit}</span> : null}
      </Text>
      {hint ? <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>{hint}</Text> : null}
    </div>
  )
}
