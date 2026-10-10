import { create } from 'zustand'
import { persist } from 'zustand/middleware'

/**
 * 面板的纯 UI 状态。
 *
 * 注意：这里不放任何来自服务的数据。服务数据统一由 TanStack Query 管理（见 data/queries.js），
 * 因为那些数据需要轮询、缓存、失效与错误状态，用全局 store 存会失去这些能力。
 */
/**
 * 页面清单。
 *
 * `subtitle` 与 `group` 直接对应设计稿的导航结构：首页独立，其余按「控制 / 监控」分组，
 * 设置贴底；页面头的副标题也来自这里（设计稿的 data-sub）。
 * `icon` 是 components/Icon.jsx 里的图标名。
 */
export const PAGES = [
  { id: 'home', label: '首页', subtitle: '实时监控电池、电源与性能状态', icon: 'home' },
  { id: 'battery', label: '电池设置', subtitle: '充电阈值与电池保养', icon: 'sliders', group: '控制' },
  { id: 'performance', label: '性能设置', subtitle: '性能模式与功耗限制', icon: 'gauge', group: '控制' },
  { id: 'windowsPower', label: '系统电源', subtitle: 'Windows 电源方案与插电、电池设置', icon: 'plug', group: '控制' },
  { id: 'oemFeatures', label: '显示与音频', subtitle: '健康显示与智慧音频', icon: 'sun', group: '控制' },
  { id: 'chargeHistory', label: '充放电历史', subtitle: '电池输入功率随时间变化', icon: 'battery', group: '监控' },
  { id: 'powerHistory', label: '功耗历史', subtitle: '电源功率与系统负载随时间变化', icon: 'chart', group: '监控' },
  { id: 'about', label: '设置', subtitle: '设备与关于信息', icon: 'settings', pinned: true },
]

export const useAppStore = create(
  persist(
    (set) => ({
      page: 'home',
      setPage: (page) => set({ page }),

      /** 'system' | 'light' | 'dark' */
      themeMode: 'system',
      setThemeMode: (themeMode) => set({ themeMode }),

      /**
       * 全量示例数据开关。
       *
       * 服务未安装、未启动，或者只是想在没有服务的情况下看界面时打开：所有指标都走
       * data/mock 里的模拟值。默认关闭——真实值可用时不应该用假数据覆盖。
       */
      forceMock: false,
      setForceMock: (forceMock) => set({ forceMock }),

      /** 开发用：显示"数据来源"面板，列出哪些字段是示例数据 */
      showDataSources: false,
      toggleDataSources: () => set((state) => ({ showDataSources: !state.showDataSources })),
    }),
    {
      name: 'honor-control-panel',
      // 只持久化偏好，不持久化临时 UI 状态。
      partialize: (state) => ({
        themeMode: state.themeMode,
        forceMock: state.forceMock,
        page: state.page,
      }),
    },
  ),
)
