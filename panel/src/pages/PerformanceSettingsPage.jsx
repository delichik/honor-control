import { useEffect, useState } from 'react'
import {
  Button,
  MessageBar,
  MessageBarBody,
  Radio,
  RadioGroup,
  Slider,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { FanCurve } from '../components/FanCurve.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { PERFORMANCE_MODES } from '../data/contract.js'
import { useServiceSnapshot, useSetPerformanceMode, useTelemetry } from '../data/queries.js'
import { useAppStore } from '../app/store.js'
import { exampleModeProfiles } from '../data/modeProfiles.js'

const useStyles = makeStyles({
  cards: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '12px' },
  card: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: '12px',
    padding: '14px',
    borderRadius: '10px',
    border: `1px solid ${tokens.colorNeutralStroke3}`,
    backgroundColor: tokens.colorNeutralBackground2,
    cursor: 'pointer',
  },
  cardActive: {
    border: `2px solid ${tokens.colorBrandStroke1}`,
    padding: '13px',
  },
  metrics: { display: 'flex', gap: '16px', flexWrap: 'wrap', marginTop: '6px' },
  sliders: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: '18px' },
})

/**
 * 性能设置。
 *
 * 性能模式（智能/高能）是真正能写的：服务发 0x0C07，并在写前复核 AC 供电、电量 ≥ 20%、
 * 目标电源方案是否存在，写后回读 0x0E04 与电源方案。
 *
 * 功耗限制（PL1/PL2/整机上限）与风扇曲线目前**不可写**：
 * 写路径要么没定位（PL 的 GVNT/WVST），要么需要荣耀内核驱动（风扇）。
 * 因此这里把它们渲染成只读展示并标注"示例"，不做成假的可写控件。
 */
export function PerformanceSettingsPage() {
  const styles = useStyles()
  const { telemetry, mockFields, fullMock, serviceReachable, error, isLoading } =
    useTelemetry()
  const { snapshot } = useServiceSnapshot()
  const setPerformanceMode = useSetPerformanceMode()
  const forceMock = useAppStore((state) => state.forceMock)

  const actualMode = snapshot?.Actual?.PerformanceMode ?? telemetry.PerformanceMode ?? 1
  const [mode, setMode] = useState(actualMode)

  useEffect(() => {
    if (!setPerformanceMode.isPending) setMode(actualMode)
  }, [actualMode, setPerformanceMode.isPending])

  const profile = exampleModeProfiles(mode)

  return (
    <>
      <StatusBanner
        serviceReachable={serviceReachable}
        error={error}
        snapshot={snapshot}
        loading={isLoading}
        fullMock={fullMock}
        forceMock={forceMock}
      />

      <SectionCard title="性能模式" subtitle={`当前生效：${PERFORMANCE_MODES.find((item) => item.id === actualMode)?.label ?? '未知'}`}>
        <RadioGroup value={String(mode)} onChange={(_, data) => setMode(Number(data.value))}>
          <div className={styles.cards}>
            {PERFORMANCE_MODES.map((item) => (
              <label
                key={item.id}
                className={[styles.card, mode === item.id ? styles.cardActive : null].filter(Boolean).join(' ')}
              >
                <Radio value={String(item.id)} />
                <div>
                  <Text size={400} weight="semibold">{item.label}</Text>
                  <br />
                  <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{item.hint}</Text>
                  <div className={styles.metrics}>
                    <Text size={200}>功耗墙 {exampleModeProfiles(item.id).pl1W} W</Text>
                    <Text size={200}>整机 {exampleModeProfiles(item.id).tdpW} W</Text>
                    <Text size={200}>风扇 {exampleModeProfiles(item.id).noiseDb} dB</Text>
                  </div>
                </div>
              </label>
            ))}
          </div>
        </RadioGroup>

        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <Button
            appearance="primary"
            disabled={mode === actualMode || setPerformanceMode.isPending}
            onClick={() => setPerformanceMode.mutate({ PerformanceMode: mode })}
          >
            {setPerformanceMode.isPending ? '正在切换…' : '切换性能模式'}
          </Button>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            切换会同时调整 Windows 电源方案；失败时服务会尝试回滚。
          </Text>
        </div>

        {setPerformanceMode.isError ? (
          <MessageBar intent="error">
            <MessageBarBody>{setPerformanceMode.error.message}</MessageBarBody>
          </MessageBar>
        ) : null}
        {setPerformanceMode.isSuccess && mode === actualMode ? (
          <MessageBar intent="success">
            <MessageBarBody>已切换到 {PERFORMANCE_MODES.find((item) => item.id === actualMode)?.label}，并回读确认。</MessageBarBody>
          </MessageBar>
        ) : null}
      </SectionCard>

      <SectionCard
        title="功耗限制"
        subtitle="当前只读：写路径尚未定位，先按示例值展示"
        actions={<MockBadge field="Sensors" />}
      >
        <div className={styles.sliders}>
          <div>
            <Text size={200}>CPU 持续功耗（{profile.pl1W} W）</Text>
            <Slider min={15} max={80} step={5} value={profile.pl1W} disabled />
          </div>
          <div>
            <Text size={200}>CPU 峰值功耗（{profile.pl2W} W）</Text>
            <Slider min={20} max={120} step={5} value={profile.pl2W} disabled />
          </div>
          <div>
            <Text size={200}>整机功耗上限（{profile.tdpW} W）</Text>
            <Slider min={40} max={180} step={10} value={profile.tdpW} disabled />
          </div>
        </div>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          这些数值来自机型档案的示例表（data/modeProfiles.js）。服务的 GVNT/WVST 读取可能可行，
          写入路径尚未定位（可行性文档 5.1），确认后再把滑块改成可写。
        </Text>
      </SectionCard>

      <SectionCard title="风扇曲线" subtitle="示例策略：横轴温度、纵轴转速" actions={<MockBadge field="Fans" />}>
        <FanCurve sensors={telemetry.Sensors} fans={telemetry.Fans} performanceMode={mode} />
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          曲线的自定义编辑需要荣耀内核驱动通道，当前不可写；这里展示的是内置示例策略，
          用于说明"性能模式 → 散热策略"的关系。传感器实际温度与转速见首页的性能与散热卡。
          {mockFields.includes('Fans') ? '（风扇转速当前为示例数据）' : ''}
        </Text>
      </SectionCard>
    </>
  )
}
