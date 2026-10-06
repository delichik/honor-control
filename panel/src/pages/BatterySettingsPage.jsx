import { useEffect, useState } from 'react'
import { InfoBar } from '../components/InfoBar.jsx'
import { SectionCard } from '../components/SectionCard.jsx'
import { BatteryGauge } from '../components/BatteryGauge.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { SettingRow, SettingValue, Slider, ToggleSwitch } from '../components/Controls.jsx'
import { useServiceSnapshot, useSetChargeThresholds, useTelemetry } from '../data/queries.js'
import { useAppStore } from '../app/store.js'

/** 设计稿把容量写成一位小数（83.0 Wh），读数缺失时给一个破折号而不是 0。 */
const wh = (value) => (typeof value === 'number' ? `${value.toFixed(1)} Wh` : '—')

/**
 * 电池设置。
 *
 * 版式照设计稿：左边 sp7 的「充电阈值」（电池本体 + 两行滑块 + 写入按钮），
 * 右边 sp5 的「电池保养」（设置行：开关 / 滑块 / 只读读数）。
 *
 * 这里只有充电阈值是**真正能写进硬件**的（走服务的 0x1003 + 回读校验）；
 * 保养相关的开关与滑块目前没有对应的服务命令，因此禁用并由卡片附注说明，
 * 而不是做成看起来能点、点了没反应的假开关。
 *
 * 阈值范围：当前按服务端校验规则（0 ≤ 起始 < 停止 ≤ 100）实现。
 * 设计稿里把起始限制在 40–99、停止在 41–100，这个口径差异待产品确认（可行性文档 10.2）。
 */
export function BatterySettingsPage() {
  const { telemetry, fullMock, serviceReachable, error, isLoading } = useTelemetry()
  const { snapshot } = useServiceSnapshot()
  const setCharge = useSetChargeThresholds()
  const forceMock = useAppStore((state) => state.forceMock)

  const [start, setStart] = useState(telemetry.ChargeStartPercent)
  const [stop, setStop] = useState(telemetry.ChargeStopPercent)
  const [dirty, setDirty] = useState(false)

  // 服务端状态更新时同步草稿，但用户正在编辑（dirty）时不覆盖他手上的值
  useEffect(() => {
    if (dirty) return
    setStart(telemetry.ChargeStartPercent)
    setStop(telemetry.ChargeStopPercent)
  }, [telemetry.ChargeStartPercent, telemetry.ChargeStopPercent, dirty])

  const updateStart = (value) => {
    setDirty(true)
    setStart(Math.max(0, Math.min(value, stop - 1)))
  }
  const updateStop = (value) => {
    setDirty(true)
    setStop(Math.min(100, Math.max(value, start + 1)))
  }

  const apply = () => {
    setCharge.mutate(
      { ChargeStart: start, ChargeEnd: stop },
      { onSuccess: () => setDirty(false) },
    )
  }

  const reset = () => {
    setStart(telemetry.ChargeStartPercent)
    setStop(telemetry.ChargeStopPercent)
    setDirty(false)
  }

  return (
    <>
      <div className="hc-sp12">
        <StatusBanner
          serviceReachable={serviceReachable}
          error={error}
          snapshot={snapshot}
          loading={isLoading}
          fullMock={fullMock}
          forceMock={forceMock}
        />
      </div>

      <SectionCard
        span={7}
        icon="battery"
        title="充电阈值"
        actions={
          <span className="hc-pill" data-tone="accent">
            充电窗口 {Math.max(0, stop - start)}%
          </span>
        }
      >
        <BatteryGauge
          percent={telemetry.BatteryPercent}
          startPercent={start}
          stopPercent={stop}
          charging={telemetry.BatteryPowerW > 0.6}
          interactive
          onChangeStart={updateStart}
          onChangeStop={updateStop}
        />

        <div className="hc-thresh-controls">
          <div className="hc-thresh-row">
            <span className="hc-lbl">开始充电</span>
            <Slider
              min={0}
              max={99}
              value={start}
              onChange={updateStart}
              format={(value) => `${value}%`}
            />
          </div>
          <div className="hc-thresh-row">
            <span className="hc-lbl">停止充电</span>
            <Slider
              min={1}
              max={100}
              value={stop}
              onChange={updateStop}
              format={(value) => `${value}%`}
            />
          </div>
        </div>

        <div className="hc-card-actions">
          <button
            type="button"
            className="hc-btn hc-btn--accent"
            onClick={apply}
            disabled={!dirty || setCharge.isPending}
          >
            {setCharge.isPending ? '正在写入…' : '应用到硬件'}
          </button>
          <button type="button" className="hc-btn" onClick={reset} disabled={!dirty}>
            还原
          </button>
        </div>

        {setCharge.isError ? (
          <InfoBar tone="critical" icon="warn" title="写入失败">
            {setCharge.error.message}
          </InfoBar>
        ) : null}

        {setCharge.isSuccess && !dirty ? (
          <InfoBar tone="success" icon="check-circle" title="已生效">
            
              已写入并回读校验：{snapshot?.Actual?.ChargeStart ?? start}% – {snapshot?.Actual?.ChargeEnd ?? stop}%
            
          </InfoBar>
        ) : null}
      </SectionCard>

      <SectionCard
        span={5}
        icon="settings"
        title="电池保养"
        note="服务暂无对应命令"
        actions={
          <MockBadge fields={['BatteryHealthPercent', 'BatteryDesignCapacityWh']} />
        }
      >
        <div className="hc-settings">
          <SettingRow title="长期插电保护" desc="长期接电源时把电量维持在 60% 附近">
            <ToggleSwitch checked={false} disabled ariaLabel="长期插电保护（服务暂无对应命令）" />
          </SettingRow>

          <SettingRow title="充满后断开充电" desc="达到停止阈值后不再向电池送电">
            <ToggleSwitch checked disabled ariaLabel="充满后断开充电（服务暂无对应命令）" />
          </SettingRow>

          <SettingRow title="低电量提醒">
            <div className="hc-setting-ctrl">
              <Slider min={5} max={50} value={20} disabled format={(value) => `${value}%`} />
            </div>
          </SettingRow>

          <SettingRow
            title="电池健康度"
            desc={`设计容量 ${wh(telemetry.BatteryDesignCapacityWh)} · 循环 ${telemetry.BatteryCycleCount ?? '—'} 次`}
          >
            <SettingValue>
              {telemetry.BatteryHealthPercent === null || telemetry.BatteryHealthPercent === undefined
                ? '—'
                : `${telemetry.BatteryHealthPercent}%`}
            </SettingValue>
          </SettingRow>
        </div>
      </SectionCard>
    </>
  )
}
