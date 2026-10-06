import { Icon } from './Icon.jsx'

/**
 * 提示条 —— 设计稿的 `.infobar`。
 *
 * 全站统一的提示外观：服务状态（StatusBanner）与页面内的写入结果都用它，
 * 避免出现"这一处是 Fluent MessageBar、那一处是设计稿 InfoBar"的两套观感。
 * 语气决定底色与图标色，标题一行、正文一行，都不换行堆叠。
 */
export function InfoBar({ tone = 'accent', icon = 'info', title, children, actions }) {
  return (
    <div className="hc-infobar" data-tone={tone}>
      <Icon name={icon} />
      <div className="hc-infobar-body">
        <div className="hc-infobar-title">{title}</div>
        {children ? <div className="hc-infobar-msg">{children}</div> : null}
      </div>
      {actions}
    </div>
  )
}
