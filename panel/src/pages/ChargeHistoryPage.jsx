import { useState } from 'react'
import { Tab, TabList, Text, tokens } from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { StatTile } from '../components/StatTile.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { HISTORY_RANGES } from '../data/contract.js'
import { summarizeChargeSeries } from '../data/derive.js'
import { useHistory } from '../data/queries.js'

/**
 * 充放电历史。
 *
 * 序列由服务端降采样后返回（等间隔、无时间戳），点数是固定的：
 * 1 小时 180 点、24 小时 240 点、7 天 300 点（可行性文档 5.4）。
 *
 * 注意：服务停止期间没有采样，曲线会出现断档——这是真实情况，不做插值掩盖。
 */
export function ChargeHistoryPage() {
  const [rangeId, setRangeId] = useState('24h')
  const range = HISTORY_RANGES.find((item) => item.id === rangeId) ?? HISTORY_RANGES[1]
  const { samples, hours, simulated } = useHistory('batteryPower', range)
  const stats = summarizeChargeSeries(samples, hours)

  return (
    <>
      <SectionCard
        title="充放电历史"
        subtitle="以 0 为界：上方充电、下方放电"
        actions={
          <>
            {simulated ? <MockBadge field="BatteryPowerW" /> : null}
            <TabList selectedValue={rangeId} onTabSelect={(_, data) => setRangeId(data.value)}>
              {HISTORY_RANGES.map((item) => (
                <Tab key={item.id} value={item.id}>{item.label}</Tab>
              ))}
            </TabList>
          </>
        }
      >
        <HistoryChart
          hours={hours}
          unit="W"
          yDomain={[-60, 60]}
          series={[
            {
              key: 'batteryPower',
              label: '电池功率',
              samples,
              colorToken: 'colorBrandStroke1',
              fill: true,
            },
          ]}
        />
        {simulated ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            当前曲线是示例数据：服务端采样器与历史查询属于协议 v2，尚未实现。
          </Text>
        ) : null}
      </SectionCard>

      <SectionCard title="统计" subtitle={`按 ${range.label} 区间累计`}>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <StatTile label="充电时长" value={stats.chargeHours.toFixed(1)} unit="小时" />
          <StatTile label="放电时长" value={stats.dischargeHours.toFixed(1)} unit="小时" />
          <StatTile label="充入电量" value={stats.chargeWh.toFixed(0)} unit="Wh" />
          <StatTile label="放出电量" value={stats.dischargeWh.toFixed(0)} unit="Wh" />
        </div>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          口径：±0.6 W 以内视为未充放（不计入时长），Wh = Σ(功率 × dt)，dt = 区间小时数 ÷ 点数。
        </Text>
      </SectionCard>
    </>
  )
}
