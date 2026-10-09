import { SectionCard } from '../components/SectionCard.jsx'
import { PowerCard } from '../components/PowerCard.jsx'
import { ThermalCard } from '../components/ThermalCard.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { useActualPerformanceMode, useHomeStatusSnapshot, useTelemetry } from '../data/queries.js'
import { useAppStore } from '../app/store.js'

/**
 * 首页：电源卡 + 性能与散热卡。
 *
 * 页面本身不做任何取数逻辑，数据全部来自 queries.js（含示例数据注入），
 * 这样"哪些是真实值、哪些是占位"只在一个地方决定（data/mock/index.js）。
 *
 * 排版照抄设计稿的 12 列栅格：三张卡都是整宽（sp12），异常状态横幅占最上面一行，
 * 正常充放电状态由母线流向与电池填充表达，不出文字提示。
 */
export function HomePage() {
  const { telemetry, mockFields, fullMock, serviceReachable, error, isLoading } = useTelemetry()
  const statusSnapshot = useHomeStatusSnapshot()
  const actualPerformanceMode = useActualPerformanceMode()
  const forceMock = useAppStore((state) => state.forceMock)

  return (
    <>
      <div className="hc-sp12">
        <StatusBanner
          serviceReachable={serviceReachable}
          error={error}
          snapshot={statusSnapshot}
          loading={isLoading}
          fullMock={fullMock}
          forceMock={forceMock}
        />
      </div>

      <PowerCard telemetry={telemetry} mockFields={mockFields} />

      <ThermalCard
        telemetry={telemetry}
        mockFields={mockFields}
        performanceMode={actualPerformanceMode ?? telemetry.PerformanceMode ?? null}
      />
    </>
  )
}
