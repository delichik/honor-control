import { Text, makeStyles, tokens } from '@fluentui/react-components'
import { SectionCard } from './SectionCard.jsx'
import { BatteryGauge } from './BatteryGauge.jsx'
import { PowerFlow } from './PowerFlow.jsx'
import { MockBadge } from './MockBadge.jsx'
import {
  CHARGE_STATE_LABEL,
  chargeStateOf,
  deriveSystemLoadW,
  estimateMinutes,
  formatTemperature,
  formatWatts,
} from '../data/derive.js'

const useStyles = makeStyles({
  readouts: {
    display: 'flex',
    gap: '28px',
    flexWrap: 'wrap',
    paddingTop: '4px',
    borderTop: `1px solid ${tokens.colorNeutralStroke3}`,
  },
  readout: { display: 'flex', flexDirection: 'column', gap: '2px' },
  value: { fontVariantNumeric: 'tabular-nums' },
})

function humanizeMinutes(minutes) {
  if (minutes === null || minutes === undefined) return '—'
  if (minutes < 60) return `≈ ${minutes} 分钟`
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return `≈ ${hours} 小时 ${rest} 分`
}

/**
 * 电源卡：电池图形 + 功率流向 + 关键读数。
 *
 * 这张卡是首页的主视图，所有数值都走 data/derive.js 的统一口径，
 * 保证与监控页的历史曲线对得上（历史上原型这里有两套"系统负载"来源，会显示不同数字）。
 */
export function PowerCard({ telemetry, mockFields = [] }) {
  const styles = useStyles()
  const mocked = (field) => mockFields.includes(field)

  const chargeState = chargeStateOf(telemetry.BatteryPowerW)
  const systemLoadW = deriveSystemLoadW({
    serviceValue: telemetry.SystemLoadW,
    adapterPowerW: telemetry.AdapterPowerW,
    batteryPowerW: telemetry.BatteryPowerW,
    pluggedIn: telemetry.PluggedIn,
  })
  const etaMinutes = estimateMinutes({
    chargeState,
    batteryPercent: telemetry.BatteryPercent,
    batteryPowerW: telemetry.BatteryPowerW,
    designCapacityWh: telemetry.BatteryDesignCapacityWh,
    chargeStopPercent: telemetry.ChargeStopPercent,
  })

  return (
    <SectionCard
      title="电源"
      subtitle={CHARGE_STATE_LABEL[chargeState]}
      actions={<MockBadge field="AdapterPowerW" />}
    >
      <BatteryGauge
        percent={telemetry.BatteryPercent}
        startPercent={telemetry.ChargeStartPercent}
        stopPercent={telemetry.ChargeStopPercent}
        charging={chargeState === 'charging'}
      />

      <PowerFlow
        pluggedIn={telemetry.PluggedIn}
        adapterPowerW={telemetry.AdapterPowerW}
        systemLoadW={systemLoadW}
        batteryPowerW={telemetry.BatteryPowerW}
      />

      <div className={styles.readouts}>
        <div className={styles.readout}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>电池温度</Text>
          <Text size={500} weight="semibold" className={styles.value}>
            {formatTemperature(telemetry.BatteryTemperatureC)} °C
          </Text>
          <MockBadge field="BatteryTemperatureC" />
        </div>

        <div className={styles.readout}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>电池功率</Text>
          <Text size={500} weight="semibold" className={styles.value}>
            {formatWatts(telemetry.BatteryPowerW, { signed: true })} W
          </Text>
          <MockBadge field="BatteryPowerW" />
        </div>

        <div className={styles.readout}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {chargeState === 'charging' ? '预计充满' : '预计可用'}
          </Text>
          <Text size={500} weight="semibold" className={styles.value}>{humanizeMinutes(etaMinutes)}</Text>
          {mocked('BatteryDesignCapacityWh') ? <MockBadge field="BatteryDesignCapacityWh" /> : null}
        </div>

        <div className={styles.readout}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>适配器</Text>
          <Text size={500} weight="semibold" className={styles.value}>
            {telemetry.PluggedIn ? `${formatWatts(telemetry.AdapterPowerW)} W` : '未接入'}
          </Text>
          <MockBadge field="AdapterPowerW" />
        </div>
      </div>
    </SectionCard>
  )
}
