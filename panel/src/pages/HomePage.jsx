import { Text, tokens } from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { PowerCard } from '../components/PowerCard.jsx'
import { ThermalCard } from '../components/ThermalCard.jsx'
import { HistoryChart } from '../components/HistoryChart.jsx'
import { StatusBanner } from '../components/StatusBanner.jsx'
import { MockBadge } from '../components/MockBadge.jsx'
import { useServiceSnapshot, useTelemetry } from '../data/queries.js'
import { useRecentSamples } from '../app/useRecentSamples.js'
import { useAppStore } from '../app/store.js'

/**
 * 首页：服务状态 + 电源卡 + 性能与散热卡 + 最近一分钟的电池功率。
 *
 * 页面本身不做任何取数逻辑，数据全部来自 queries.js（含示例数据注入），
 * 这样"哪些是真实值、哪些是占位"只在一个地方决定（data/mock/index.js）。
 */
export function HomePage() {
  const { telemetry, mockFields, fullMock, serviceReachable, error, isLoading } =
    useTelemetry()
  const { snapshot } = useServiceSnapshot()
  const forceMock = useAppStore((state) => state.forceMock)

  // 首页的秒级小图用本地滚动窗口，不走服务端历史接口
  const recentBatteryPower = useRecentSamples(telemetry.BatteryPowerW, 60)

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

      <PowerCard telemetry={telemetry} mockFields={mockFields} />

      <ThermalCard
        telemetry={telemetry}
        mockFields={mockFields}
        performanceMode={snapshot?.Actual?.PerformanceMode ?? telemetry.PerformanceMode}
      />

      <SectionCard
        title="电池功率 · 最近 60 秒"
        subtitle="本地滚动窗口，1 秒一个采样点"
        actions={<MockBadge field="BatteryPowerW" />}
      >
        {recentBatteryPower.length < 2 ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            正在采集…
          </Text>
        ) : (
          <HistoryChart
            hours={1 / 60}
            unit="W"
            yDomain={[-60, 60]}
            series={[
              {
                key: 'batteryPower',
                label: '电池功率',
                samples: recentBatteryPower,
                colorToken: 'colorBrandStroke1',
                fill: true,
              },
            ]}
          />
        )}
      </SectionCard>
    </>
  )
}
