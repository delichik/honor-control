import { SectionCard } from './SectionCard.jsx'
import { SensorBar } from './SensorBar.jsx'
import { FanRotor } from './FanRotor.jsx'
import { ModeBar } from './ModeBar.jsx'
import { MockBadge } from './MockBadge.jsx'
import { temperatureTone } from '../data/derive.js'

/**
 * 性能与散热卡。
 *
 * 只展示服务读到的传感器温度与风扇 RPM，不推测风扇策略。
 *
 * 传感器与风扇都是**数量可变**的：服务报几个就渲染几个，读不到就不显示这一行；
 * 两者都没有时整张卡不渲染（设计稿的"读不到就不展示"规则）。
 *
 * 未从服务读到数据时不生成示例温度或 RPM。
 */
export function ThermalCard({ telemetry, mockFields = [], performanceMode = 1 }) {
  const sensors = telemetry.Sensors ?? []
  const fans = telemetry.Fans ?? []
  const hasAny = sensors.length > 0 || fans.length > 0
  if (!hasAny) return null

  const mocked = ['Sensors', 'Fans'].filter((field) => mockFields.includes(field))

  return (
    <SectionCard
      span={12}
      icon="gauge"
      title="性能与散热"
      actions={
        <>
          <MockBadge fields={mocked} />
          <ModeBar mode={performanceMode} />
        </>
      }
    >
      {sensors.length > 0 ? (
        <div className="hc-sensor-list">
          {sensors.map((sensor) => (
            <SensorBar
              key={sensor.Id}
              label={sensor.Label}
              tempC={sensor.TempC}
              tone={temperatureTone(sensor.TempC, sensor.WarnC, sensor.HotC)}
              warnC={sensor.WarnC}
              hotC={sensor.HotC}
            />
          ))}
        </div>
      ) : null}

      {fans.length > 0 ? (
        <div className="hc-fan-row">
          {fans.map((fan) => (
            <FanRotor key={fan.Id} label={fan.Label} rpm={fan.Rpm} maxRpm={fan.MaxRpm} />
          ))}
        </div>
      ) : null}
    </SectionCard>
  )
}
