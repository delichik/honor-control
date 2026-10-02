import { Badge, Tooltip } from '@fluentui/react-components'
import { describeField } from '../data/mock/index.js'

/**
 * "示例数据"角标。
 *
 * 规则：凡是页面上出现的假数据都必须带这个角标，鼠标悬停能看到**为什么**是假的
 * （原因写在 data/mock/sources.js，等同于把可行性文档 5.1 的结论贴在 UI 上）。
 * 这样开发期不会出现"看着像真数据"的误导。
 */
export function MockBadge({ field, inline = false }) {
  const description = describeField(field)
  if (description.kind !== 'mock') return null

  return (
    <Tooltip
      relationship="description"
      content={
        <span>
          {description.reason}
          {description.reference ? <><br />依据：{description.reference}</> : null}
        </span>
      }
    >
      <Badge appearance="tint" color="warning" size="small" style={inline ? undefined : { flexShrink: 0 }}>
        示例
      </Badge>
    </Tooltip>
  )
}

/** 值 + 角标的组合，供各处小读数复用。 */
export function MockableValue({ field, mocked, children, className }) {
  return (
    <span className={className} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
      {children}
      {mocked ? <MockBadge field={field} inline /> : null}
    </span>
  )
}
