import {
  Badge,
  Button,
  Radio,
  RadioGroup,
  Switch,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import { SectionCard } from '../components/SectionCard.jsx'
import { DataSourcePanel } from '../components/DataSourcePanel.jsx'
import { PIPE_NAME, TRAY_POLICY } from '../data/contract.js'
import { useServiceHealth, useServiceSnapshot, useSetTrayPolicy, useStartService } from '../data/queries.js'
import { useAppStore } from '../app/store.js'
import { useAppTheme } from '../app/theme.js'

const useStyles = makeStyles({
  rows: { display: 'flex', flexDirection: 'column', gap: '10px' },
  row: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '16px',
    padding: '10px 12px',
    borderRadius: '8px',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  mono: { fontFamily: 'Consolas, "Cascadia Mono", monospace', fontSize: 12 },
})

/**
 * 设置页：服务状态、托盘行为、外观、以及开发用的数据来源面板。
 *
 * 托盘策略写进服务（服务是唯一权威），托盘进程只读。策略要等协议 v2 才有，
 * 因此在服务未升级前这一项是禁用的——宁可禁用也不做成"点了没反应"。
 */
export function AboutPage() {
  const styles = useStyles()
  const health = useServiceHealth()
  const { snapshot } = useServiceSnapshot()
  const setTrayPolicy = useSetTrayPolicy()
  const startService = useStartService()
  const forceMock = useAppStore((state) => state.forceMock)
  const setForceMock = useAppStore((state) => state.setForceMock)
  const { themeMode, setThemeMode, isDark } = useAppTheme()

  const policy = snapshot?.TrayPolicy ?? TRAY_POLICY.OnDemand

  return (
    <>
      <SectionCard title="服务" subtitle="Honor Control 服务是配置与状态的唯一权威">
        <div className={styles.rows}>
          <div className={styles.row}>
            <Text>运行状态</Text>
            <Text weight="semibold">
              {health.serviceReachable ? (
                <Badge appearance="tint" color="success">已连接</Badge>
              ) : (
                <Badge appearance="tint" color="danger">未连接</Badge>
              )}
            </Text>
          </div>
          <div className={styles.row}>
            <Text>通信协议</Text>
            <Text weight="semibold">v{health.protocolVersion}</Text>
          </div>
          <div className={styles.row}>
            <Text>命名管道</Text>
            <Text className={styles.mono}>{PIPE_NAME}</Text>
          </div>
          <div className={styles.row}>
            <Text>操作</Text>
            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                appearance="secondary"
                disabled={health.serviceReachable || startService.isPending}
                onClick={() => startService.mutate()}
              >
                启动服务
              </Button>
            </div>
          </div>
        </div>
        {startService.isError ? (
          <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
            {startService.error.message}
          </Text>
        ) : null}
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          退出服务请使用系统托盘菜单的"退出（停止后台服务）"——托盘与服务是成对的，
          退出托盘会停止服务，这一点在托盘菜单里会二次确认。
        </Text>
      </SectionCard>

      <SectionCard title="托盘" subtitle="托盘进程按这里的策略被拉起；策略保存在服务端">
        <RadioGroup
          value={policy}
          onChange={(_, data) => setTrayPolicy.mutate({ TrayPolicy: data.value })}
          disabled={!health.serviceReachable || setTrayPolicy.isPending}
        >
          <Radio value={TRAY_POLICY.Off} label="不显示托盘图标" />
          <Radio value={TRAY_POLICY.OnDemand} label="按需显示（打开面板时出现，默认）" />
          <Radio value={TRAY_POLICY.Always} label="始终显示（登录后自动出现）" />
        </RadioGroup>
        {!health.serviceReachable ? (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            服务未连接，无法读写托盘策略。
          </Text>
        ) : null}
        {setTrayPolicy.isError ? (
          <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
            {setTrayPolicy.error.message}
          </Text>
        ) : null}
      </SectionCard>

      <SectionCard title="外观">
        <RadioGroup value={themeMode} onChange={(_, data) => setThemeMode(data.value)}>
          <Radio value="system" label={`跟随系统（当前为${isDark ? '深色' : '浅色'}）`} />
          <Radio value="light" label="浅色" />
          <Radio value="dark" label="深色" />
        </RadioGroup>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          窗口材质由 Tauri 的 Mica 提供；Mica 不可用时用 CSS 回退底色。
        </Text>
      </SectionCard>

      <SectionCard title="开发" subtitle="仅用于开发期">
        <Switch
          checked={forceMock}
          onChange={(_, data) => setForceMock(data.checked)}
          label="全量示例数据（不依赖服务即可看到完整界面）"
        />
      </SectionCard>

      <DataSourcePanel />
    </>
  )
}
