import { useState } from 'react'
import { SectionCard } from '../components/SectionCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { SelectorBar, StatCard } from '../components/Controls.jsx'
import { HISTORY_RANGES } from '../data/contract.js'
import { summarizeChargeSeries } from '../data/derive.js'
import { useHistory } from '../data/queries.js'

/**
 * 充放电历史（设计稿 L1279–L1301）。
 *
 * 排版照抄设计稿的栅格：第一行是右对齐的时间范围分段控件，第二行是整宽的曲线卡，
 * 第三行是四个各跨 3 列的统计块（它们本身就是卡片，不再套一层"统计"卡）。
 *
 * 数据不在这里算：序列由服务端降采样后返回（等间隔、无时间戳），
 * 点数与服务端的历史记录粒度一致——1 小时 60 点、24 小时 240 点、7 天 300 点。
 * 服务停止期间没有采样，曲线会出现断档，这是真实情况，不做插值掩盖。
 */
export function ChargeHistoryPage() {
  const [rangeId, setRangeId] = useState('24h')
  const range = HISTORY_RANGES.find((item) => item.id === rangeId) ?? HISTORY_RANGES[1]
  const { samples, hours, isLoading, serviceReachable, error } = useHistory('batteryPower', range)
  const hasData = samples.filter(Number.isFinite).length >= 2
  const complete = hasData && samples.every(Number.isFinite)
  const stats = summarizeChargeSeries(samples, hours)
  const historyMessage = isLoading
    ? '读取中…'
    : !serviceReachable
      ? '服务未连接'
      : error
        ? '读取失败'
        : !hasData
          ? '暂无记录'
          : complete
            ? null
            : '记录有缺口'

  return (
    <>
      <div className="hc-sp12 hc-range-head">
        <SelectorBar
          ariaLabel="时间范围"
          items={HISTORY_RANGES.map((item) => ({ id: item.id, label: item.label }))}
          value={rangeId}
          onChange={setRangeId}
        />
      </div>

      <SectionCard
        span={12}
        icon="battery"
        title="电池输入功率"
        note={historyMessage ?? '正值充电，负值放电'}
      >
        <HistoryChart
          hours={hours}
          yDomain={[-100, 100]}
          height={300}
          emptyMessage={historyMessage ?? '暂无记录'}
          series={[
            {
              key: 'batteryPower',
              label: '电池功率',
              samples,
              color: 'var(--accent)',
              fill: true,
            },
          ]}
        />
      </SectionCard>

      <StatCard span={3} label="充电时长" value={complete ? stats.chargeHours.toFixed(1) : '—'} unit={complete ? '小时' : null} />
      <StatCard span={3} label="放电时长" value={complete ? stats.dischargeHours.toFixed(1) : '—'} unit={complete ? '小时' : null} />
      <StatCard span={3} label="充入电量" value={complete ? stats.chargeWh.toFixed(0) : '—'} unit={complete ? 'Wh' : null} />
      <StatCard span={3} label="放出电量" value={complete ? stats.dischargeWh.toFixed(0) : '—'} unit={complete ? 'Wh' : null} />
    </>
  )
}
