import { SectionCard } from './SectionCard.jsx'
import { Icon } from './Icon.jsx'
import { BatteryGauge } from './BatteryGauge.jsx'
import { PowerBus, busStateOf } from './PowerBus.jsx'
import { MockBadge } from './MockBadge.jsx'
import { useElementWidth } from '../app/useElementWidth.js'
import {
  chargeStateOf,
  deriveSystemLoadW,
  estimateMinutes,
  formatTemperature,
} from '../data/derive.js'

/**
 * 电源卡 —— 首页的主视图，一张图讲完整件事。
 *
 * 排布照抄设计稿：左侧两个节点（电源适配器 / 系统负载）挂在供电母线上，
 * 中间是母线（功率数字标在各自线路上），右侧是电池本体，最右是电量与温度读数。
 * 电池只承载"电量 + 充电阈值"，读数不进电池内部。
 *
 * 窄窗口（卡片宽度 < 900px）时母线行改成上下堆叠：母线本身画不出来就隐藏，
 * 但节点、电池、读数一个都不少。
 */
export function PowerCard({ telemetry, mockFields = [] }) {
  const [cardRef, cardWidth] = useElementWidth()
  // 卡片窄到 760px 以下才改成上下堆叠（面板窗口最小 960px，此时内容区约 640px）：
  // 母线图是这张卡的主视觉，能缩就不该丢。
  const stacked = cardWidth > 0 && cardWidth < 760

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

  const temperature = telemetry.BatteryTemperatureC
  const temperatureTone =
    temperature === null || temperature === undefined
      ? undefined
      : temperature >= 55
        ? 'var(--critical)'
        : temperature >= 45
          ? 'var(--caution)'
          : undefined

  // 卡片里凡是来自示例数据的字段，合成一个角标放在卡片头部
  const mockedHere = ['AdapterPowerW', 'SystemLoadW', 'BatteryTemperatureC', 'BatteryDesignCapacityWh'].filter(
    (field) => mockFields.includes(field),
  )

  return (
    <SectionCard
      span={12}
      icon="plug"
      title="电源"
      className="hc-power-card"
      rootRef={cardRef}
      actions={
        <>
          <MockBadge fields={mockedHere} />
          <span className="hc-eta">{formatEta(etaMinutes, chargeState)}</span>
        </>
      }
    >
      <div className={stacked ? 'hc-bus hc-bus--stacked' : 'hc-bus'}>
        <div className="hc-bus-side">
          <div className="hc-bus-node" data-off={telemetry.PluggedIn ? undefined : 'true'}>
            <Icon name="plug" />
            <span>电源适配器</span>
          </div>
          <div className="hc-bus-node">
            <Icon name="gauge" />
            <span>系统负载</span>
          </div>
        </div>

        <PowerBus
          state={busStateOf({ pluggedIn: telemetry.PluggedIn, chargeState })}
          adapterPowerW={telemetry.AdapterPowerW}
          systemLoadW={systemLoadW}
          batteryPowerW={telemetry.BatteryPowerW}
        />

        <BatteryGauge
          percent={telemetry.BatteryPercent}
          startPercent={telemetry.ChargeStartPercent}
          stopPercent={telemetry.ChargeStopPercent}
          charging={chargeState === 'charging'}
        />

        <div className="hc-battery-info">
          <div className="hc-binfo">
            <span className="hc-binfo-v">
              <b>{Math.round(telemetry.BatteryPercent ?? 0)}</b>
              <i>%</i>
            </span>
            <span className="hc-binfo-k">当前电量</span>
          </div>
          <div className="hc-binfo">
            <span className="hc-binfo-v" style={{ color: temperatureTone }}>
              <b>{formatTemperature(temperature)}</b>
              <i>°C</i>
            </span>
            <span className="hc-binfo-k">电池温度</span>
          </div>
        </div>
      </div>
    </SectionCard>
  )
}

/** 预计时间：充电中给"还要多久充满"，放电中给"还能用多久"，其余不显示（设计稿同此）。 */
function formatEta(minutes, chargeState) {
  if (minutes === null || minutes === undefined) return ''
  if (chargeState === 'charging') return `≈ ${Math.max(1, minutes)} 分钟`
  if (chargeState === 'discharging') {
    if (minutes >= 60) return `${Math.floor(minutes / 60)} 小时 ${minutes % 60} 分`
    return `${minutes} 分钟`
  }
  return ''
}
