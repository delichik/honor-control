import { create } from 'zustand'
import { persist } from 'zustand/middleware'

/**
 * 面板的纯 UI 状态。
 *
 * 注意：这里不放任何来自服务的数据。服务数据统一由 TanStack Query 管理（见 data/queries.js），
 * 因为那些数据需要轮询、缓存、失效与错误状态，用全局 store 存会失去这些能力。
 */
export const PAGES = [
  { id: 'home', label: '首页', icon: 'gauge' },
  { id: 'battery', label: '电池设置', icon: 'battery' },
  { id: 'performance', label: '性能设置', icon: 'flash' },
  { id: 'chargeHistory', label: '充放电历史', icon: 'history' },
  { id: 'powerHistory', label: '功耗历史', icon: 'chart' },
  { id: 'about', label: '设置', icon: 'settings' },
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
