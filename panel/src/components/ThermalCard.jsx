import { Text, makeStyles, tokens } from '@fluentui/react-components'
import { SectionCard } from './SectionCard.jsx'
import { FanCurve } from './FanCurve.jsx'
import { SensorBar } from './SensorBar.jsx'
import { FanRotor } from './FanRotor.jsx'
import { MockBadge } from './MockBadge.jsx'
import { temperatureTone } from '../data/derive.js'

const useStyles = makeStyles({
  stack: { display: 'flex', flexDirection: 'column', gap: '14px' },
  sensors: { display: 'flex', flexDirection: 'column', gap: '8px' },
  fans: { display: 'flex', gap: '24px', flexWrap: 'wrap', alignItems: 'center' },
  empty: { color: tokens.colorNeutralForeground3 },
})

/**
 * 性能与散热卡。
 *
 * 这三件事本来是一条因果链：性能模式 → 散热策略（曲线）→ 各传感器落在曲线的哪个位置 → 风扇转速。
 * 所以卡片围绕风扇曲线组织，传感器和风扇分别作为"曲线上的读数"与"执行结果"呈现。
 *
 * 传感器与风扇都是**数量可变**的：服务报几个就渲染几个，读不到就不显示这一行
 * （服务端的"能力探测"决定这里有什么，见可行性文档 5.2）。
 */
export function ThermalCard({ telemetry, mockFields = [], performanceMode = 1 }) {
  const styles = useStyles()
  const sensors = telemetry.Sensors ?? []
  const fans = telemetry.Fans ?? []
  const hasAny = sensors.length > 0 || fans.length > 0

  return (
    <SectionCard
      title="性能与散热"
      subtitle={sensors.length || fans.length ? `${sensors.length} 个温度传感器 · ${fans.length} 个风扇` : null}
      actions={
        <>
          <MockBadge field="Sensors" />
          <MockBadge field="Fans" />
        </>
      }
    >
      {hasAny ? (
        <div className={styles.stack}>
          <FanCurve sensors={sensors} fans={fans} performanceMode={performanceMode} />

          {sensors.length > 0 ? (
            <div className={styles.sensors}>
              {sensors.map((sensor) => (
                <SensorBar
                  key={sensor.Id}
                  label={sensor.Label}
                  tempC={sensor.TempC}
                  tone={temperatureTone(sensor.TempC, sensor.WarnC, sensor.HotC)}
                />
              ))}
            </div>
          ) : null}

          {fans.length > 0 ? (
            <div className={styles.fans}>
              {fans.map((fan) => (
                <FanRotor key={fan.Id} rpm={fan.Rpm} maxRpm={fan.MaxRpm ?? 6000} />
              ))}
            </div>
          ) : null}
        </div>
      ) : (
        <Text className={styles.empty} size={300}>
          服务未报告任何温度传感器或风扇。
        </Text>
      )}
    </SectionCard>
  )
}
