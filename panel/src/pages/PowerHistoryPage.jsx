import { useState } from 'react'
import { Tab, TabList, Text, tokens } from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { StatTile } from '../components/StatTile.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { HISTORY_RANGES } from '../data/contract.js'
import { summarizeSeries } from '../data/derive.js'
import { useHistory } from '../data/queries.js'

/**
 * 功耗历史。
 *
 * 两条序列共用一条横轴：适配器输出功率（实线+填充）与系统负载（虚线）。
 * 系统负载是**派生值**（适配器功率 − 充入电池的功率），口径见 data/derive.js，
 * 与服务端保持一致，避免与首页的实时值出现两个数字。
 */
export function PowerHistoryPage() {
  const [rangeId, setRangeId] = useState('24h')
  const range = HISTORY_RANGES.find((item) => item.id === rangeId) ?? HISTORY_RANGES[1]

  const adapter = useHistory('adapterPower', range)
  const load = useHistory('systemLoad', range)

  // 适配器序列决定点数，负载序列按同一长度对齐（服务端返回等长序列）
  const length = Math.min(adapter.samples.length, load.samples.length)
  const adapterSamples = adapter.samples.slice(0, length)
  const loadSamples = load.samples.slice(0, length)
  const simulated = adapter.simulated || load.simulated

  const adapterStats = summarizeSeries(adapterSamples, adapter.hours)
  const loadStats = summarizeSeries(loadSamples, load.hours)


  return (
    <>
      <SectionCard
        title="功耗历史"
        subtitle="适配器输出功率与系统负载"
        actions={
          <>
            {simulated ? <MockBadge reason="服务未连接或历史数据不足，这条曲线由前端的模拟模型生成，不代表真实硬件。" /> : null}
            <TabList selectedValue={rangeId} onTabSelect={(_, data) => setRangeId(data.value)}>
              {HISTORY_RANGES.map((item) => (
                <Tab key={item.id} value={item.id}>{item.label}</Tab>
              ))}
            </TabList>
          </>
        }
      >
        <HistoryChart
          hours={adapter.hours}
          unit="W"
          yDomain={[0, 'auto']}
          series={[
            {
              key: 'adapterPower',
              label: '适配器功率',
              samples: adapterSamples,
              colorToken: 'colorBrandStroke1',
              fill: true,
            },
            {
              key: 'systemLoad',
              label: '系统负载',
              samples: loadSamples,
              colorToken: 'colorPaletteDarkOrangeForeground1',
              dash: '5 4',
            },
          ]}
        />
        {simulated ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            当前曲线是示例数据。适配器功率需要 0x0902 电压 × 0x10902/0x110902 电流，
            尚未在真机验证（可行性文档 5.1）。
          </Text>
        ) : null}
      </SectionCard>

      <SectionCard title="统计" subtitle={`按 ${range.label} 区间累计`}>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <StatTile label="峰值取电" value={adapterStats.peak.toFixed(0)} unit="W" />
          <StatTile label="平均取电" value={adapterStats.average.toFixed(0)} unit="W" />
          <StatTile label="累计取电" value={adapterStats.wh.toFixed(0)} unit="Wh" />
          <StatTile label="平均负载" value={loadStats.average.toFixed(0)} unit="W" />
        </div>
      </SectionCard>
    </>
  )
}
