import { useState } from 'react'
import { SectionCard } from '../components/SectionCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { SelectorBar, StatCard } from '../components/Controls.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
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

const SIMULATED_REASON =
  '服务未连接或历史数据不足，这两条曲线由前端的模拟模型生成，不代表真实硬件（适配器功率需要 0x0902 电压 × 0x10902/0x110902 电流，尚未在真机验证）。'

/**
 * 功耗历史（设计稿 L1304–L1328）。
 *
 * 两条序列共用一条横轴：适配器输出功率（实线 + 填充）与系统负载（虚线）。
 * 系统负载是**派生值**（适配器功率 − 充入电池的功率），口径见 data/derive.js，
 * 与服务端保持一致，避免与首页的实时值出现两个数字。
 *
 * 排版照抄设计稿：第一行右对齐的时间范围分段控件，第二行整宽的曲线卡
 * （头右侧是两个色块图例），第三行是三个各跨 4 列的统计块。
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
        actions={
          <>
            {simulated ? <MockBadge reason={SIMULATED_REASON} /> : null}
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
          </>
        }
      >
        <HistoryChart
          hours={adapter.hours}
          yDomain={[0, 'auto']}
          height={300}
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

      <StatCard span={4} label="电源功率峰值" value={adapterStats.peak.toFixed(0)} unit="W" />
      <StatCard span={4} label="电源功率平均" value={adapterStats.average.toFixed(0)} unit="W" />
      <StatCard span={4} label="累计取电" value={adapterStats.wh.toFixed(0)} unit="Wh" />
    </>
  )
}
