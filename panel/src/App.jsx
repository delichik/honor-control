import { Suspense } from 'react'
import {
  Button,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components'
import {
  BatteryCharge24Regular,
  Flash24Regular,
  Gauge24Regular,
  History24Regular,
  Settings24Regular,
  DataUsage24Regular,
  WeatherMoon24Regular,
  WeatherSunny24Regular,
} from '@fluentui/react-icons'
import { PAGES, useAppStore } from './app/store.js'
import { useAppTheme } from './app/theme.js'
import { HomePage } from './pages/HomePage.jsx'
import { BatterySettingsPage } from './pages/BatterySettingsPage.jsx'
import { PerformanceSettingsPage } from './pages/PerformanceSettingsPage.jsx'
import { ChargeHistoryPage } from './pages/ChargeHistoryPage.jsx'
import { PowerHistoryPage } from './pages/PowerHistoryPage.jsx'
import { AboutPage } from './pages/AboutPage.jsx'

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    position: 'relative',
  },
  backdrop: {
    position: 'absolute',
    inset: 0,
    zIndex: -1,
    backgroundColor: 'var(--panel-backdrop)',
  },
  body: { display: 'flex', flex: 1, minHeight: 0 },
  nav: {
    width: '216px',
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    padding: '12px 8px',
    borderRight: `1px solid ${tokens.colorNeutralStroke3}`,
  },
  navItem: {
    justifyContent: 'flex-start',
    fontWeight: 400,
  },
  navItemActive: {
    backgroundColor: tokens.colorNeutralBackground3,
    fontWeight: 600,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '14px 20px 10px',
  },
  content: {
    flex: 1,
    minWidth: 0,
    overflowY: 'auto',
    padding: '0 20px 24px',
    display: 'flex',
    flexDirection: 'column',
    gap: '14px',
  },
})

const ICONS = {
  gauge: Gauge24Regular,
  battery: BatteryCharge24Regular,
  flash: Flash24Regular,
  history: History24Regular,
  chart: DataUsage24Regular,
  settings: Settings24Regular,
}

const PAGE_COMPONENTS = {
  home: HomePage,
  battery: BatterySettingsPage,
  performance: PerformanceSettingsPage,
  chargeHistory: ChargeHistoryPage,
  powerHistory: PowerHistoryPage,
  about: AboutPage,
}

/**
 * 应用外壳：左侧导航 + 内容区。
 *
 * 导航用按钮而不是 TabList：面板只有 6 个固定页面，按钮更容易控制图标与选中态，
 * 也省掉 TabList 的键盘语义适配。
 * 窗口边框用系统自带的（tauri.conf.json 里 decorations: true），不自绘标题栏。
 */
export default function App() {
  const styles = useStyles()
  const page = useAppStore((state) => state.page)
  const setPage = useAppStore((state) => state.setPage)
  const { isDark, themeMode, setThemeMode } = useAppTheme()

  const CurrentPage = PAGE_COMPONENTS[page] ?? HomePage
  const currentTitle = PAGES.find((item) => item.id === page)?.label ?? '首页'

  return (
    <div className={styles.root}>
      <div className={styles.backdrop} />

      <header className={styles.header}>
        <div>
          <Text size={500} weight="semibold">Honor Control</Text>
          <br />
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {currentTitle}
          </Text>
        </div>
        <Button
          appearance="subtle"
          icon={isDark ? <WeatherSunny24Regular /> : <WeatherMoon24Regular />}
          onClick={() => setThemeMode(isDark ? 'light' : 'dark')}
          title={`当前：${themeMode === 'system' ? '跟随系统' : themeMode === 'dark' ? '深色' : '浅色'}`}
        >
          {isDark ? '浅色' : '深色'}
        </Button>
      </header>

      <div className={styles.body}>
        <nav className={styles.nav}>
          {PAGES.map((item) => {
            const Icon = ICONS[item.icon] ?? Gauge24Regular
            const active = item.id === page
            return (
              <Button
                key={item.id}
                appearance={active ? 'subtle' : 'transparent'}
                icon={<Icon />}
                className={mergeClasses(styles.navItem, active && styles.navItemActive)}
                onClick={() => setPage(item.id)}
              >
                {item.label}
              </Button>
            )
          })}
        </nav>

        <main className={styles.content}>
          <Suspense fallback={<Spinner label="正在加载…" />}>
            <CurrentPage />
          </Suspense>
        </main>
      </div>
    </div>
  )
}
