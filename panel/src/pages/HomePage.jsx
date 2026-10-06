import { SectionCard } from '../components/SectionCard.jsx'
import { PowerCard } from '../components/PowerCard.jsx'
import { ThermalCard } from '../components/ThermalCard.jsx'
import { PowerChart } from '../components/PowerChart.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { useServiceSnapshot, useTelemetry } from '../data/queries.js'
import { useRecentSamples } from '../app/useRecentSamples.js'
import { useAppStore } from '../app/store.js'

/**
 * 首页：电源卡 + 性能与散热卡 + 最近一分钟的电池功率。
 *
 * 页面本身不做任何取数逻辑，数据全部来自 queries.js（含示例数据注入），
 * 这样"哪些是真实值、哪些是占位"只在一个地方决定（data/mock/index.js）。
 *
 * 排版照抄设计稿的 12 列栅格：三张卡都是整宽（sp12），异常状态横幅占最上面一行，
 * 正常充放电状态由母线流向与电池填充表达，不出文字提示。
 */
export function HomePage() {
  const { telemetry, mockFields, fullMock, serviceReachable, error, isLoading } = useTelemetry()
  const { snapshot } = useServiceSnapshot()
  const forceMock = useAppStore((state) => state.forceMock)

  // 首页的秒级小图用本地滚动窗口，不走服务端历史接口
  const recentBatteryPower = useRecentSamples(telemetry.BatteryPowerW, 60)

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

      <PowerCard telemetry={telemetry} mockFields={mockFields} />

      <ThermalCard
        telemetry={telemetry}
        mockFields={mockFields}
        performanceMode={snapshot?.Actual?.PerformanceMode ?? telemetry.PerformanceMode}
      />

      <SectionCard
        span={12}
        icon="chart"
        title="电池输入功率"
        note="最近 60 秒"
        actions={<MockBadge field="BatteryPowerW" />}
      >
        <PowerChart samples={recentBatteryPower} />
      </SectionCard>
    </>
  )
}
