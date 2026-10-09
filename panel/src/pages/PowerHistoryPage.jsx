import { useState } from 'react'
import { SectionCard } from '../components/SectionCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { SelectorBar, StatCard } from '../components/Controls.jsx'
import { HISTORY_RANGES } from '../data/contract.js'
import { summarizeSeries } from '../data/derive.js'
import { useHistory } from '../data/queries.js'

/**
 * 系统负载的配色，用本项目 token 里真实存在的颜色。
 *
 * 设计稿这里写的是 `var(--violet)`，而原稿并没有定义 `--violet`（图例在浏览器里退回
 * 继承色，成了一条灰线）；Fluent 的 marigold 是同一套色板里真实存在的强调色，
 * 深色 #f2c661 / 浅色 #eaa300（实测），都能和 cyan 的 `--accent` 拉开距离，因此改用它。
 * 图例与曲线的颜色是同一个值：HistoryChart 直接把它写进 stroke。
 */
const LOAD_COLOR = 'var(--colorPaletteMarigoldBorderActive)'

/**
 * 功耗历史（设计稿 L1304–L1328）。
 *
 * 两条历史序列都由服务记录并查询。服务目前无法读取适配器功率；系统负载只在电池供电时可由真实电池功率得到，
 * 其余时间桶留空，面板不补造曲线或统计值。
 *
 * 排版照抄设计稿：第一行右对齐的时间范围分段控件，第二行整宽的曲线卡
 * （头右侧是两个色块图例），第三行是三个各跨 4 列的统计块。
 */
export function PowerHistoryPage() {
  const [rangeId, setRangeId] = useState('24h')
  const range = HISTORY_RANGES.find((item) => item.id === rangeId) ?? HISTORY_RANGES[1]

  const adapter = useHistory('adapterPower', range)
  const load = useHistory('systemLoad', range)

  const adapterSamples = adapter.samples
  const loadSamples = load.samples
  const adapterCount = adapterSamples.filter(Number.isFinite).length
  const loadCount = loadSamples.filter(Number.isFinite).length
  const hasData = adapterCount >= 2 || loadCount >= 2
  const adapterComplete = adapterSamples.length > 0 && adapterSamples.every(Number.isFinite)
  const historyMessage = adapter.isLoading || load.isLoading
    ? '读取中…'
    : !adapter.serviceReachable || !load.serviceReachable
      ? '服务未连接'
      : adapter.error || load.error
        ? '读取失败'
        : !hasData
          ? '暂无记录'
          : adapterComplete
            ? null
            : '记录有缺口'

  const adapterStats = summarizeSeries(adapterSamples, adapter.hours)

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
        icon="chart"
        title="电源功率与系统负载"
        note={historyMessage}
        actions={
          <div className="hc-chart-key">
            <span style={{ color: 'var(--accent)' }}>
              <i />
              电源功率
            </span>
            <span style={{ color: LOAD_COLOR }}>
              <i />
              系统负载
            </span>
          </div>
        }
      >
        <HistoryChart
          hours={adapter.hours}
          yDomain={[0, 'auto']}
          height={300}
          emptyMessage={historyMessage ?? '暂无记录'}
          series={[
            {
              key: 'adapterPower',
              label: '电源功率',
              samples: adapterSamples,
              color: 'var(--accent)',
              fill: true,
            },
            {
              key: 'systemLoad',
              label: '系统负载',
              samples: loadSamples,
              color: LOAD_COLOR,
              dash: '5 4',
            },
          ]}
        />
      </SectionCard>

      <StatCard span={4} label="电源功率峰值" value={adapterComplete ? adapterStats.peak.toFixed(0) : '—'} unit={adapterComplete ? 'W' : null} />
      <StatCard span={4} label="电源功率平均" value={adapterComplete ? adapterStats.average.toFixed(0) : '—'} unit={adapterComplete ? 'W' : null} />
      <StatCard span={4} label="累计取电" value={adapterComplete ? adapterStats.wh.toFixed(0) : '—'} unit={adapterComplete ? 'Wh' : null} />
    </>
  )
}
