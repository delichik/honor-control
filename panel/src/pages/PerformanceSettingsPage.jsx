import { useEffect, useState } from 'react'
import { InfoBar } from '../components/InfoBar.jsx'
import { SectionCard } from '../components/SectionCard.jsx'
import { FanCurve } from '../components/FanCurve.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { RadioCard, SelectorBar, SettingRow, Slider } from '../components/Controls.jsx'
import { PERFORMANCE_MODES } from '../data/contract.js'
import { useServiceSnapshot, useSetPerformanceMode, useTelemetry } from '../data/queries.js'
import { useAppStore } from '../app/store.js'
import { exampleModeProfiles } from '../data/modeProfiles.js'
import { EXAMPLE_FAN_CURVE } from '../data/fanCurve.js'

/** 设计稿里的模式名比契约里的短，图标同首页的分段控件。 */
const MODE_CARDS = {
  1: { label: '智能', icon: 'sparkle' },
  2: { label: '高性能', icon: 'bolt-filled' },
}

/** 风扇策略的分段控件：自动（跟性能模式）/ 最大 / 自定义（示例控制点）。 */
const FAN_MODES = [
  { id: 'auto', label: '自动', icon: 'sparkle' },
  { id: 'max', label: '最大', icon: 'bolt-filled' },
  { id: 'custom', label: '自定义', icon: 'sliders' },
]

const watts = (value) => `${value} W`

/**
 * 性能设置。
 *
 * 版式照设计稿：左边 sp5 的「性能模式」单选卡，右边 sp7 的「功耗限制」设置行，
 * 下面整宽 sp12 的「风扇曲线」（头部是策略分段控件）。
 *
 * 性能模式（智能/高能）是真正能写的：服务发 0x0C07，并在写前复核 AC 供电、电量 ≥ 20%、
 * 目标电源方案是否存在，写后回读 0x0E04 与电源方案。
 *
 * 功耗限制（PL1/PL2/整机上限）与风扇曲线目前**不可写**：
 * 写路径要么没定位（PL 的 GVNT/WVST），要么需要荣耀内核驱动（风扇）。
 * 因此这里把它们渲染成只读展示并标注"示例"，不做成假的可写控件。
 */
export function PerformanceSettingsPage() {
  const { telemetry, fullMock, serviceReachable, error, isLoading } = useTelemetry()
  const { snapshot } = useServiceSnapshot()
  const setPerformanceMode = useSetPerformanceMode()
  const forceMock = useAppStore((state) => state.forceMock)

  const actualMode = snapshot?.Actual?.PerformanceMode ?? telemetry.PerformanceMode ?? null
  const configuredMode = snapshot?.Desired?.PerformanceMode ?? actualMode ?? 1
  const [mode, setMode] = useState(configuredMode)
  const [fanMode, setFanMode] = useState('auto')

  useEffect(() => {
    if (!setPerformanceMode.isPending) setMode(configuredMode)
  }, [actualMode, configuredMode, setPerformanceMode.isPending])

  const profile = exampleModeProfiles(mode)
  const modeLabel = (value) => MODE_CARDS[value]?.label ?? PERFORMANCE_MODES.find((item) => item.id === value)?.label ?? '未知'

  // 曲线只做预览：自动策略跟着当前模式走，最大是满转，自定义用示例控制点。
  const policy =
    fanMode === 'max'
      ? { kind: 'max' }
      : fanMode === 'custom'
        ? { kind: 'custom', points: EXAMPLE_FAN_CURVE }
        : { kind: 'auto', mode }

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
        span={5}
        icon="gauge"
        title="性能模式"
        note={`当前生效：${modeLabel(actualMode)}`}
        actions={
          <MockBadge
            reason="每个模式的功耗墙、整机功耗与噪声来自机型档案示例表（data/modeProfiles.js），服务端尚未返回机型参数。"
            reference="可行性文档 10.7（机型档案）"
          />
        }
      >
        <div className="hc-radio-cards">
          {PERFORMANCE_MODES.map((item) => {
            const card = MODE_CARDS[item.id] ?? { label: item.label, icon: 'gauge' }
            const itemProfile = exampleModeProfiles(item.id)
            return (
              <RadioCard
                key={item.id}
                selected={mode === item.id}
                onSelect={() => setMode(item.id)}
                title={card.label}
                icon={card.icon}
                metrics={
                  <>
                    <b>{itemProfile.pl1W} W</b> CPU · <b>{itemProfile.tdpW} W</b> 整机 ·{' '}
                    <b>{itemProfile.noiseDb} dB</b>
                  </>
                }
              />
            )
          })}
        </div>

        <div className="hc-card-actions">
          <button
            type="button"
            className="hc-btn hc-btn--accent"
            disabled={mode === actualMode || setPerformanceMode.isPending}
            onClick={() => setPerformanceMode.mutate({ PerformanceMode: mode })}
          >
            {setPerformanceMode.isPending ? '正在切换…' : '切换性能模式'}
          </button>
        </div>

        {setPerformanceMode.isError ? (
          <InfoBar tone="critical" icon="warn" title="写入失败">
            {setPerformanceMode.error.message}
          </InfoBar>
        ) : null}
        {setPerformanceMode.isSuccess && mode === actualMode ? (
          <InfoBar tone="success" icon="check-circle" title="已生效">
            已切换到 {modeLabel(actualMode)}，并回读确认。
          </InfoBar>
        ) : null}
      </SectionCard>

      <SectionCard
        span={7}
        icon="sliders"
        title="功耗限制"
        note="随性能模式自动设定"
        actions={
          <MockBadge
            reason="功耗墙来自机型档案示例表（data/modeProfiles.js）；服务的 GVNT/WVST 写入路径尚未定位，因此这里只读。"
            reference="可行性文档 5.1（功耗限制写路径）"
          />
        }
      >
        <div className="hc-settings">
          <SettingRow title="CPU 持续功耗">
            <div className="hc-setting-ctrl">
              <Slider min={15} max={80} step={5} value={profile.pl1W} disabled format={watts} />
            </div>
          </SettingRow>

          <SettingRow title="CPU 峰值功耗">
            <div className="hc-setting-ctrl">
              <Slider min={20} max={120} step={5} value={profile.pl2W} disabled format={watts} />
            </div>
          </SettingRow>

          <SettingRow title="整机功耗上限">
            <div className="hc-setting-ctrl">
              <Slider min={40} max={180} step={10} value={profile.tdpW} disabled format={watts} />
            </div>
          </SettingRow>
        </div>
      </SectionCard>

      <SectionCard
        span={12}
        icon="fan"
        title="风扇曲线"
        actions={
          <>
            <MockBadge field="FanCurve" />
            <SelectorBar
              items={FAN_MODES}
              value={fanMode}
              onChange={setFanMode}
              ariaLabel="风扇策略"
            />
          </>
        }
      >
        <FanCurve sensors={[]} fans={[{ Id: 'preview', Rpm: 3000, MaxRpm: 6000 }]} policy={policy} />
        <div className="hc-edit-hint">曲线是示例策略：服务暂无风扇写通道，这里只预览三种策略的形态。</div>
      </SectionCard>
    </>
  )
}
