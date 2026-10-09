import { Badge, Radio } from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { SettingRow, SettingValue, SelectorBar, ToggleSwitch } from '../components/Controls.jsx'
import { MockableValue } from '../components/MockBadge.jsx'
import { DataSourcePanel } from '../components/DataSourcePanel.jsx'
import { PIPE_NAME, TRAY_POLICY } from '../data/contract.js'
import {
  useServiceHealth,
  useServiceSnapshot,
  useSetTrayPolicy,
  useStartService,
  useTelemetry,
} from '../data/queries.js'
import { useAppStore } from '../app/store.js'
import { useAppTheme } from '../app/theme.js'
import panelPackage from '../../package.json'

/** 托盘策略的三个选项：设计稿里那种括号说明下沉成一行 desc。 */
const TRAY_OPTIONS = [
  { value: TRAY_POLICY.Off, title: '不显示托盘图标' },
  { value: TRAY_POLICY.OnDemand, title: '按需显示', desc: '打开面板时出现（默认）' },
  { value: TRAY_POLICY.Always, title: '始终显示', desc: '登录后自动出现' },
]

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
 * 设计稿没有、但确实存在的设置项（服务、托盘、外观、开发、数据来源）全部保留，
 * 按同一套设置行重排；原来一段段的解释文字压成一行 desc 或卡片 note。
 *
 * 数据逻辑一律没动：托盘策略仍然写进服务（服务是唯一权威），启动服务仍然走 SCM。
 */
export function AboutPage() {
  const health = useServiceHealth()
  const { snapshot } = useServiceSnapshot()
  const { telemetry, mockFields } = useTelemetry()
  const setTrayPolicy = useSetTrayPolicy()
  const startService = useStartService()
  const forceMock = useAppStore((state) => state.forceMock)
  const setForceMock = useAppStore((state) => state.setForceMock)
  const { themeMode, setThemeMode, isDark } = useAppTheme()

  const policy = snapshot?.TrayPolicy ?? TRAY_POLICY.OnDemand
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
          <SettingRow title="数据刷新间隔" desc="快照 5 s，历史序列按分钟级">
            <SettingValue>1 s</SettingValue>
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
                {fans.length} 个
              </MockableValue>
            </SettingValue>
          </SettingRow>
        </div>
      </SectionCard>

      <SectionCard span={7} icon="plug" title="服务" note="配置与状态的唯一权威">
        <div className="hc-settings">
          <SettingRow title="运行状态">
            <Badge appearance="tint" color={health.serviceReachable ? 'success' : 'danger'}>
              {health.serviceReachable ? '已连接' : '未连接'}
            </Badge>
          </SettingRow>
          <SettingRow title="通信协议">
            <SettingValue>v{health.protocolVersion}</SettingValue>
          </SettingRow>
          <SettingRow title="命名管道">
            <SettingValue>
              <span className="hc-mono">{PIPE_NAME}</span>
            </SettingValue>
          </SettingRow>
          <SettingRow title="后台服务" desc="退出请用托盘菜单的「退出（停止后台服务）」">
            <button
              type="button"
              className="hc-btn"
              disabled={health.serviceReachable || startService.isPending}
              onClick={() => startService.mutate()}
            >
              {startService.isPending ? '正在启动…' : '启动服务'}
            </button>
          </SettingRow>
        </div>
        {startService.isError ? <div className="hc-setting-error">{startService.error.message}</div> : null}
      </SectionCard>

      <SectionCard
        span={5}
        icon="menu"
        title="托盘"
        note={health.serviceReachable ? undefined : '服务未连接，无法读写'}
      >
        <div className="hc-settings" role="radiogroup" aria-label="托盘显示策略">
          {TRAY_OPTIONS.map((option) => (
            <SettingRow key={option.value} title={option.title} desc={option.desc}>
              <Radio
                value={option.value}
                checked={policy === option.value}
                aria-label={option.title}
                disabled={!health.serviceReachable || setTrayPolicy.isPending}
                onChange={() => setTrayPolicy.mutate({ TrayPolicy: option.value })}
              />
            </SettingRow>
          ))}
        </div>
        {setTrayPolicy.isError ? <div className="hc-setting-error">{setTrayPolicy.error.message}</div> : null}
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
