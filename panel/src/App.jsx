import { Suspense } from 'react'
import { Spinner, makeStyles } from '@fluentui/react-components'
import { useQueryClient } from '@tanstack/react-query'
import { PAGES, useAppStore } from './app/store.js'
import { useAppTheme } from './app/theme.js'
import { useEnsureTray } from './app/useEnsureTray.js'
import { useServiceSnapshot } from './data/queries.js'
import { Icon } from './components/Icon.jsx'
import { HomePage } from './pages/HomePage.jsx'
import { BatterySettingsPage } from './pages/BatterySettingsPage.jsx'
import { PerformanceSettingsPage } from './pages/PerformanceSettingsPage.jsx'
import { ChargeHistoryPage } from './pages/ChargeHistoryPage.jsx'
import { PowerHistoryPage } from './pages/PowerHistoryPage.jsx'
import { AboutPage } from './pages/AboutPage.jsx'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', height: '100%', position: 'relative' },
  backdrop: { position: 'absolute', inset: 0, zIndex: -1, backgroundColor: 'var(--panel-backdrop)' },
  body: { display: 'flex', flex: 1, minHeight: 0 },
  loading: { display: 'flex', justifyContent: 'center', paddingTop: '48px' },
})

const PAGE_COMPONENTS = {
  home: HomePage,
  battery: BatterySettingsPage,
  performance: PerformanceSettingsPage,
  chargeHistory: ChargeHistoryPage,
  powerHistory: PowerHistoryPage,
  about: AboutPage,
}

/**
 * 应用外壳：左侧导航 + 页面头 + 内容区。
 *
 * 结构照设计稿：导航栏（分组标题 + 贴底的设置 + 设备页脚）与内容区并列，
 * 内容区上方是固定的页面头（标题 / 副标题 / 主题切换 + 刷新），下面是可滚动的页面本体。
 * 窗口边框用系统自带的（tauri.conf.json 里 decorations: true），不自绘标题栏。
 */
export default function App() {
  const styles = useStyles()
  const page = useAppStore((state) => state.page)
  const setPage = useAppStore((state) => state.setPage)
  const { isDark, themeMode, setThemeMode } = useAppTheme()
  const queryClient = useQueryClient()
  // 面板打开时单独拉起托盘；托盘有单实例互斥量，重复调用无害。
  useEnsureTray()

  const CurrentPage = PAGE_COMPONENTS[page] ?? HomePage
  const current = PAGES.find((item) => item.id === page) ?? PAGES[0]
  const pinned = PAGES.filter((item) => item.pinned)
  const groups = PAGES.filter((item) => !item.pinned).reduce((acc, item) => {
    const key = item.group ?? ''
    acc[key] = acc[key] ?? []
    acc[key].push(item)
    return acc
  }, {})

  const renderItem = (item) => (
    <button
      key={item.id}
      type="button"
      className="hc-nav-item"
      aria-selected={item.id === page}
      aria-current={item.id === page ? 'page' : undefined}
      onClick={() => setPage(item.id)}
    >
      <Icon name={item.icon} />
      <span className="hc-nav-label">{item.label}</span>
    </button>
  )

  return (
    <div className={styles.root}>
      <div className={styles.backdrop} />

      <div className={styles.body}>
        <nav className="hc-nav" aria-label="主导航">
          <div className="hc-nav-list">
            {groups['']?.map(renderItem)}

            {Object.entries(groups)
              .filter(([group]) => group)
              .map(([group, items]) => (
                <div key={group} className="hc-nav-group">
                  <div className="hc-nav-group-title">{group}</div>
                  {items.map(renderItem)}
                </div>
              ))}

            <div style={{ flex: 1 }} />
            {pinned.map(renderItem)}
          </div>

          <ServiceFooter />
        </nav>

        <main className="hc-content">
          <div className="hc-page-head">
            <div>
              <div className="hc-page-title">{current.label}</div>
              <div className="hc-page-sub">{current.subtitle}</div>
            </div>
            <div className="hc-page-actions">
              <button
                type="button"
                className="hc-tbtn"
                aria-pressed={isDark}
                title={`当前：${themeMode === 'system' ? '跟随系统' : themeMode === 'dark' ? '深色' : '浅色'}`}
                onClick={() => setThemeMode(isDark ? 'light' : 'dark')}
              >
                <Icon name={isDark ? 'sun' : 'moon'} />
                {isDark ? '浅色' : '深色'}
              </button>
              <button
                type="button"
                className="hc-btn"
                onClick={() => queryClient.invalidateQueries()}
              >
                <Icon name="refresh" />
                刷新
              </button>
            </div>
          </div>

          <div className="hc-page-scroll">
            <div className="hc-page">
              <div className="hc-grid">
                <Suspense
                  fallback={
                    <div className={styles.loading}>
                      <Spinner label="正在加载…" />
                    </div>
                  }
                >
                  <CurrentPage />
                </Suspense>
              </div>
            </div>
          </div>
        </main>
      </div>
    </div>
  )
}

/** 服务状态单独订阅，快照轮询只更新导航页脚。 */
function ServiceFooter() {
  const { snapshot, serviceReachable } = useServiceSnapshot()
  return (
    <div className="hc-nav-foot">
      <span className="hc-avatar" aria-hidden="true">HC</span>
      <span className="hc-nav-foot-text">
        <b>Honor Control</b>
        {serviceReachable ? serviceFooterText(snapshot) : '后台服务未连接'}
      </span>
    </div>
  )
}

/** 导航页脚第二行：供电状态 + 电脑管家是否占用（都来自服务快照，不是猜的）。 */
function serviceFooterText(snapshot) {
  const parts = []
  if (snapshot?.Actual?.IsOnAcPower === true) parts.push('已接通电源')
  else if (snapshot?.Actual?.IsOnAcPower === false) parts.push('电池供电')
  if (snapshot?.Actual?.PcManagerOpen) parts.push('电脑管家运行中')
  return parts.length > 0 ? parts.join(' · ') : '服务已连接'
}
