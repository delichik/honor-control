import { Badge, Tooltip } from '@fluentui/react-components'
import { describeField } from '../data/mock/index.js'

/**
 * "示例数据"角标。
 *
 * 规则：凡是页面上出现的假数据都必须带这个角标，鼠标悬停能看到**为什么**是假的
 * （原因写在 data/mock/sources.js，等同于把可行性文档 5.1 的实测结论贴在 UI 上）。
 * 这样开发期不会出现"看着像真数据"的误导。
 *
 * 两种用法：
 * - `field="BatteryTemperatureC"`：查 sources.js 里该字段的原因（字段变成真实值后角标自动消失）；
 * - `reason="..."`：整块内容都是示例数据时（例如整条历史曲线来自模拟），直接给原因。
 */
export function MockBadge({ field, reason, reference, inline = false }) {
  const description = reason ? { kind: 'mock', reason, reference } : describeField(field)
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
