import { SelectorBar } from './Controls.jsx'

/**
 * 性能模式分段控件 —— 设计稿里散热卡头部的那一个。
 *
 * 只读指示器：首页只负责"现在是什么模式"，改模式要走「性能设置」页
 * （那边有风险提示与二次确认）。做成可点的按钮会让人以为点一下就切了，
 * 而那是要写固件的操作。
 */
export function ModeBar({ mode }) {
  const label = mode === 2 ? '高性能' : mode === 1 ? '智能' : '未知'
  return (
    <SelectorBar
      ariaLabel={`当前性能模式：${label}`}
      value={mode}
      items={[
        { id: 1, label: '智能', icon: 'sparkle' },
        { id: 2, label: '高性能', icon: 'bolt-filled' },
      ]}
    />
  )
}
