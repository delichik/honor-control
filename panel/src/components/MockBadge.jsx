import { Badge, Tooltip } from '@fluentui/react-components'
import { describeField } from '../data/mock/index.js'

/**
 * "示例数据"角标。
 *
 * 规则：凡是页面上出现的假数据都必须带这个角标，鼠标悬停能看到**为什么**是假的
 * （原因写在 data/mock/sources.js，等同于把可行性文档 5.1 的实测结论贴在 UI 上）。
 * 这样开发期不会出现"看着像真数据"的误导。
 *
 * 三种用法：
 * - `field="BatteryTemperatureC"`：单个字段（字段变成真实值后角标自动消失）；
 * - `fields={[...]}`：一张卡片里有多个字段是示例数据时合成一个角标，悬停逐条列出——
 *   设计稿的卡片里没有地方放一排角标，合成一个既守住"必须标注"的规则又不破坏排版；
 * - `reason="..."`：整块内容都是示例数据（例如整条历史曲线来自模拟）。
 */
export function MockBadge({ field, fields, reason, reference, inline = false }) {
  const keys = fields ?? (field ? [field] : [])
  const described = keys.map((key) => ({ key, ...describeField(key) })).filter((item) => item.kind === 'mock')

  if (reason) {
    return (
      <Shell inline={inline}>
        <span>
          {reason}
          {reference ? (
            <>
              <br />
              依据：{reference}
            </>
          ) : null}
        </span>
      </Shell>
    )
  }

  if (described.length === 0) return null

  return (
    <Shell inline={inline}>
      <span>
        {described.length === 1
          ? described[0].reason
          : described.map((item) => (
              <span key={item.key} style={{ display: 'block', marginBottom: 4 }}>
                <b>{item.label}</b>：{item.reason}
              </span>
            ))}
        {described.length === 1 && described[0].reference ? (
          <>
            <br />
            依据：{described[0].reference}
          </>
        ) : null}
      </span>
    </Shell>
  )
}

function Shell({ children, inline }) {
  return (
    <Tooltip relationship="description" content={children}>
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
