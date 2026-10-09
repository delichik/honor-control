import { Badge } from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { SettingRow, SettingValue, SelectorBar, ToggleSwitch } from '../components/Controls.jsx'
import { MockableValue } from '../components/MockBadge.jsx'
import { DataSourcePanel } from '../components/DataSourcePanel.jsx'
import {
  useServiceHealth,
  useServiceSnapshot,
  useStartService,
  useStopService,
  useTelemetry,
} from '../data/queries.js'
import { useAppStore } from '../app/store.js'
import { useAppTheme } from '../app/theme.js'
import panelPackage from '../../package.json'

const THEME_OPTIONS = [
  { id: 'system', label: '跟随系统' },
  { id: 'light', label: '浅色' },
  { id: 'dark', label: '深色' },
]

/**
 * 设置页（设计稿 L1331–L1356）。
 *
 * 前两行照设计稿：左「设备」跨 7 列、右「关于」跨 5 列，行内是"标题在左、值在右"的设置行。
 * **只写本项目真的拿得到的信息**：设计稿里的型号、系统版本、适配器额定功率在本项目没有来源
 * （服务端 GetCapabilities 明确返回 AdapterRatedW = null），这几行直接不出现，而不是编一个数填进去；
 * 由示例数据补的字段（电池设计容量、传感器、风扇）按既有规则挂「示例」角标，服务接上后角标自动消失。
 *
 * 只保留用户会用到的设备、外观与服务操作；服务启停走 SCM 并由 Windows 显示 UAC。
 */
export function AboutPage() {
  const health = useServiceHealth()
  const { snapshot } = useServiceSnapshot()
  const { telemetry, mockFields } = useTelemetry()
  const startService = useStartService()
  const stopService = useStopService()
  const forceMock = useAppStore((state) => state.forceMock)
  const setForceMock = useAppStore((state) => state.setForceMock)
  const { themeMode, setThemeMode, isDark } = useAppTheme()

  const serviceActionPending = startService.isPending || stopService.isPending
  const serviceActionError = startService.error ?? stopService.error
  const actual = snapshot?.Actual ?? null
  const sensors = telemetry.Sensors ?? []
  const fans = telemetry.Fans ?? []
  const designCapacityWh = telemetry.BatteryDesignCapacityWh
  const fullChargeCapacityWh = telemetry.BatteryFullChargeCapacityWh
  const cycleCount = telemetry.BatteryCycleCount

  return (
    <>
      <SectionCard span={7} icon="settings" title="设备">
        <div className="hc-settings">
          <SettingRow title="电源方案">
            <SettingValue>{actual?.PowerSchemeName ?? '—'}</SettingValue>
          </SettingRow>
          <SettingRow title="供电状态">
            <SettingValue>
              {actual?.IsOnAcPower === true ? '已接通电源' : actual?.IsOnAcPower === false ? '电池供电' : '—'}
            </SettingValue>
          </SettingRow>
          <SettingRow title="电池设计容量">
            <SettingValue>
              <MockableValue field="BatteryDesignCapacityWh" mocked={mockFields.includes('BatteryDesignCapacityWh')}>
                {designCapacityWh ? `${Number(designCapacityWh).toFixed(1)} Wh` : '—'}
              </MockableValue>
            </SettingValue>
          </SettingRow>
          <SettingRow title="电池满充容量">
            <SettingValue>
              <MockableValue field="BatteryFullChargeCapacityWh" mocked={mockFields.includes('BatteryFullChargeCapacityWh')}>
                {typeof fullChargeCapacityWh === 'number' ? `${fullChargeCapacityWh.toFixed(1)} Wh` : '—'}
              </MockableValue>
            </SettingValue>
          </SettingRow>
          <SettingRow title="电池循环次数">
            <SettingValue>{cycleCount === null || cycleCount === undefined ? '—' : `${cycleCount} 次`}</SettingValue>
          </SettingRow>
        </div>
      </SectionCard>

      <SectionCard span={5} icon="info" title="关于">
        <div className="hc-settings">
          <SettingRow title="面板版本">
            <SettingValue>{panelPackage.version}</SettingValue>
          </SettingRow>
          <SettingRow title="服务版本">
            <SettingValue>{snapshot?.ServiceVersion ?? '服务未连接'}</SettingValue>
          </SettingRow>
          <SettingRow title="传感器接入">
            <SettingValue>
              <MockableValue field="Sensors" mocked={mockFields.includes('Sensors')}>
                {sensors.length > 0 ? sensors.map((sensor) => sensor.Label).join(' · ') : '未检测到'}
              </MockableValue>
            </SettingValue>
          </SettingRow>
          <SettingRow title="风扇数量">
            <SettingValue>
              <MockableValue field="Fans" mocked={mockFields.includes('Fans')}>
                {fans.length > 0 ? `${fans.length} 个` : '—'}
              </MockableValue>
            </SettingValue>
          </SettingRow>
        </div>
      </SectionCard>

      <SectionCard span={12} icon="plug" title="服务">
        <div className="hc-settings">
          <SettingRow title="运行状态">
            <Badge appearance="tint" color={health.isLoading ? 'informative' : health.serviceReachable ? 'success' : 'danger'}>
              {health.isLoading ? '检查中' : health.serviceReachable ? '已连接' : '未连接'}
            </Badge>
          </SettingRow>
          <SettingRow title="后台服务">
            <button
              type="button"
              className="hc-btn"
              disabled={health.isLoading || serviceActionPending}
              onClick={() => health.serviceReachable ? stopService.mutate() : startService.mutate()}
            >
              {serviceActionPending
                ? '处理中…'
                : health.serviceReachable
                  ? '停止服务'
                  : '启动服务'}
            </button>
          </SettingRow>
        </div>
        {serviceActionError ? <div className="hc-setting-error">{serviceActionError.message}</div> : null}
      </SectionCard>

      <SectionCard span={7} icon="sun" title="外观" note="窗口材质为 Mica，不可用时用回退底色">
        <div className="hc-settings">
          <SettingRow title="主题" desc={`当前为${isDark ? '深色' : '浅色'}`}>
            <SelectorBar ariaLabel="主题" items={THEME_OPTIONS} value={themeMode} onChange={setThemeMode} />
          </SettingRow>
        </div>
      </SectionCard>

      <SectionCard span={5} icon="chip" title="开发" note="仅用于开发期">
        <div className="hc-settings">
          <SettingRow title="全量示例数据" desc="与下方数据来源面板的开关是同一个">
            <ToggleSwitch checked={forceMock} onChange={setForceMock} ariaLabel="全量示例数据" />
          </SettingRow>
        </div>
      </SectionCard>

      <DataSourcePanel />
    </>
  )
}
