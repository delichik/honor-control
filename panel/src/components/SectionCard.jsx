import { Icon } from './Icon.jsx'

/**
 * 卡片外壳：设计稿里的 `.card`。
 *
 * 规格照抄设计稿：7px 圆角、1px 卡片描边、16px 内边距、子项间距 14px，
 * 标题行左边是 16×16 强调色图标 + 14px/600 标题，右边是 12px 次要文字附注与操作区。
 *
 * `subtitle` 是旧调用点的写法，等价于设计稿放在右上角的 `note`（保留它，
 * 这样还没重做的页面不用跟着改）。
 */
export function SectionCard({
  title,
  subtitle,
  note,
  icon,
  actions,
  children,
  className,
  span,
  style,
  rootRef,
}) {
  const classes = ['hc-card']
  if (span) classes.push(`hc-sp${span}`)
  if (className) classes.push(className)
  const noteText = note ?? subtitle

  return (
    <section className={classes.join(' ')} style={style} ref={rootRef}>
      {(title || noteText || actions) && (
        <div className="hc-card-head">
          <div className="hc-card-title">
            {icon ? <Icon name={icon} /> : null}
            {title}
          </div>
          {(noteText || actions) && (
            <div className="hc-card-head-right">
              {noteText ? <span className="hc-card-note">{noteText}</span> : null}
              {actions}
            </div>
          )}
        </div>
      )}
      {children}
    </section>
  )
}
