import { useEffect, useMemo, useState } from 'react'
import { webDarkTheme, webLightTheme } from '@fluentui/react-components'
import { useAppStore } from './store.js'

/**
 * 主题：以 Fluent UI 的 web 主题为底，只覆盖品牌色与圆角，贴近 WinUI 3 的观感。
 *
 * 为什么不自己生成整套 brand ramp：面板追求的是"像 WinUI"而不是"是 WinUI"，
 * 覆盖少量语义 token 已经足够，同时避免维护 16 级色阶。
 */
const brandOverrides = {
  colorBrandBackground: '#0F6CBD',
  colorBrandBackgroundHover: '#115EA3',
  colorBrandBackgroundPressed: '#0C3B5E',
  colorBrandForeground1: '#0F6CBD',
  colorCompoundBrandBackground: '#0F6CBD',
  colorCompoundBrandBackgroundHover: '#115EA3',
  borderRadiusMedium: '6px',
  borderRadiusLarge: '10px',
}

const darkOverrides = {
  colorBrandBackground: '#115EA3',
  colorBrandBackgroundHover: '#0F6CBD',
  colorBrandBackgroundPressed: '#0C3B5E',
  colorBrandForeground1: '#479EF5',
  colorCompoundBrandBackground: '#479EF5',
  colorCompoundBrandBackgroundHover: '#62ABF5',
}

const lightTheme = { ...webLightTheme, ...brandOverrides }
const darkTheme = { ...webDarkTheme, ...darkOverrides }

/** 跟随系统时需要订阅 prefers-color-scheme 的变化。 */
function useSystemPrefersDark() {
  const [prefersDark, setPrefersDark] = useState(
    () => window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false,
  )

  useEffect(() => {
    const query = window.matchMedia?.('(prefers-color-scheme: dark)')
    if (!query) return undefined
    const onChange = (event) => setPrefersDark(event.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  return prefersDark
}

export function useAppTheme() {
  const themeMode = useAppStore((state) => state.themeMode)
  const setThemeMode = useAppStore((state) => state.setThemeMode)
  const prefersDark = useSystemPrefersDark()

  const isDark = themeMode === 'dark' || (themeMode === 'system' && prefersDark)
  const theme = useMemo(() => (isDark ? darkTheme : lightTheme), [isDark])

  // 让滚动条、原生控件、Mica 背景也能跟着切换。
  useEffect(() => {
    document.documentElement.dataset.theme = isDark ? 'dark' : 'light'
  }, [isDark])

  return { theme, isDark, themeMode, setThemeMode }
}
