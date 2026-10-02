import { useEffect, useState } from 'react'
import {
  Button,
  MessageBar,
  MessageBarBody,
  Slider,
  Switch,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { BatteryGauge } from '../components/BatteryGauge.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { useServiceSnapshot, useSetChargeThresholds, useTelemetry } from '../data/queries.js'
import { useAppStore } from '../app/store.js'

const useStyles = makeStyles({
  row: { display: 'flex', gap: '28px', flexWrap: 'wrap' },
  field: { display: 'flex', flexDirection: 'column', gap: '6px', minWidth: '260px', flex: '1 1 260px' },
  actions: { display: 'flex', gap: '10px', alignItems: 'center' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px' },
  setting: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: tokens.colorNeutralBackground2,
  },
})

/**
 * 电池设置。
 *
 * 这里只有充电阈值是**真正能写进硬件**的（走服务的 0x1003 + 回读校验）；
 * 保养相关的开关与电池信息目前没有对应的服务命令，因此禁用并标注"示例"，
 * 而不是做成看起来能点、点了没反应的假开关。
 *
 * 阈值范围：当前按服务端校验规则（0 ≤ 起始 < 停止 ≤ 100）实现。
 * 设计稿里把起始限制在 40–99、停止在 41–100，这个口径差异待产品确认（可行性文档 10.2）。
 */
export function BatterySettingsPage() {
  const styles = useStyles()
  const { telemetry, mockFields, fullMock, serviceReachable, error, isLoading } =
    useTelemetry()
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

      <SectionCard title="充电阈值" subtitle="在电池图形上拖动标记，或用下面的滑块精确设置">
        <BatteryGauge
          percent={telemetry.BatteryPercent}
          startPercent={start}
          stopPercent={stop}
          charging={telemetry.BatteryPowerW > 0.6}
          interactive
          onChangeStart={updateStart}
          onChangeStop={updateStop}
        />

        <div className={styles.row}>
          <div className={styles.field}>
            <Text size={200}>开始充电（{start}%）</Text>
            <Slider min={0} max={99} value={start} onChange={(_, data) => updateStart(data.value)} />
          </div>
          <div className={styles.field}>
            <Text size={200}>停止充电（{stop}%）</Text>
            <Slider min={1} max={100} value={stop} onChange={(_, data) => updateStop(data.value)} />
          </div>
        </div>

        <div className={styles.actions}>
          <Button appearance="primary" onClick={apply} disabled={!dirty || setCharge.isPending}>
            {setCharge.isPending ? '正在写入…' : '应用到硬件'}
          </Button>
          <Button
            onClick={() => {
              setStart(telemetry.ChargeStartPercent)
              setStop(telemetry.ChargeStopPercent)
              setDirty(false)
            }}
            disabled={!dirty}
          >
            还原
          </Button>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            保存成功不代表硬件已同步：服务会回读 0x1103 并与请求值比对。
          </Text>
        </div>

        {setCharge.isError ? (
          <MessageBar intent="error">
            <MessageBarBody>{setCharge.error.message}</MessageBarBody>
          </MessageBar>
        ) : null}

        {setCharge.isSuccess && !dirty ? (
          <MessageBar intent="success">
            <MessageBarBody>
              已写入并回读校验：{snapshot?.Actual?.ChargeStart ?? start}% – {snapshot?.Actual?.ChargeEnd ?? stop}%
            </MessageBarBody>
          </MessageBar>
        ) : null}
      </SectionCard>

      <SectionCard
        title="电池保养"
        subtitle="服务暂无对应命令，以下开关仅为界面占位"
        actions={<MockBadge field="BatteryCycleCount" />}
      >
        <div className={styles.grid}>
          <div className={styles.setting}>
            <div>
              <Text size={300}>长期插电保护</Text>
              <br />
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                长时间接入电源时降低满充压力
              </Text>
            </div>
            <Switch checked={false} disabled />
          </div>
          <div className={styles.setting}>
            <div>
              <Text size={300}>充满后停止充电</Text>
              <br />
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                达到停止阈值后不再涓流补电
              </Text>
            </div>
            <Switch checked disabled />
          </div>
        </div>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          说明：这两个开关在服务端没有对应的 ACPI-WMI 命令，等确认可写入的通道后再启用，
          避免做成"点了没反应"的假开关。
        </Text>
      </SectionCard>

      <SectionCard title="电池信息" actions={<MockBadge field="BatteryHealthPercent" />}>
        <div className={styles.grid}>
          <div className={styles.setting}>
            <Text size={200}>健康度</Text>
            <Text size={500} weight="semibold">{telemetry.BatteryHealthPercent}%</Text>
          </div>
          <div className={styles.setting}>
            <Text size={200}>设计容量</Text>
            <Text size={500} weight="semibold">
              {telemetry.BatteryDesignCapacityWh} Wh
            </Text>
          </div>
          <div className={styles.setting}>
            <Text size={200}>循环次数</Text>
            <Text size={500} weight="semibold">{telemetry.BatteryCycleCount}</Text>
          </div>
        </div>
        {mockFields.includes('BatteryHealthPercent') ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            这三项当前是示例数据：候选来源是 root\wmi 的电池静态数据类，需要在真机确认。
          </Text>
        ) : null}
      </SectionCard>
    </>
  )
}
