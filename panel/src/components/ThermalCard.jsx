import { SectionCard } from './SectionCard.jsx'
import { FanCurve } from './FanCurve.jsx'
import { SensorBar } from './SensorBar.jsx'
import { FanRotor } from './FanRotor.jsx'
import { ModeBar } from './ModeBar.jsx'
import { MockBadge } from './MockBadge.jsx'
import { temperatureTone } from '../data/derive.js'

/**
 * 性能与散热卡。
 *
 * 这三件事本来是一条因果链：性能模式 → 散热策略（曲线）→ 各传感器落在曲线的哪个位置 → 风扇转速。
 * 所以卡片围绕风扇曲线组织，左边是传感器列表（与曲线上的标记同色），下面是风扇本体。
 *
 * 传感器与风扇都是**数量可变**的：服务报几个就渲染几个，读不到就不显示这一行；
 * 两者都没有时整张卡不渲染（设计稿的"读不到就不展示"规则）。
 *
 * ⚠️ 曲线形态与传感器/风扇读数当前都是示例数据，卡片头部合成一个"示例"角标。
 */
export function ThermalCard({ telemetry, mockFields = [], performanceMode = 1 }) {
  const sensors = telemetry.Sensors ?? []
  const fans = telemetry.Fans ?? []
  const hasAny = sensors.length > 0 || fans.length > 0
  if (!hasAny) return null

  const policy = { kind: 'auto', mode: performanceMode }
  const mocked = ['Sensors', 'Fans', 'FanCurve'].filter(
    (field) => field === 'FanCurve' || mockFields.includes(field),
  )

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
      <div className="hc-thermal-body">
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

        <FanCurve sensors={sensors} fans={fans} policy={policy} />
      </div>

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
