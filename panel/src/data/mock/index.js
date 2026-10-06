import {
  MOCK_FANS,
  MOCK_FAN_CURVE,
  MOCK_SENSORS,
  MOCK_SOURCES,
  MOCK_FAN_MAX_RPM,
  SERVICE_FIELDS,
} from './sources.js'
import { simulateTelemetry } from './generator.js'

/**
 * 示例数据的注入点。
 *
 * 调用时机：拿到服务返回值之后、交给组件之前。规则很简单——
 *   - `forceMock`（用户显式打开"全量示例数据"）→ 整份 Telemetry 用模拟值；
 *   - 否则只补**空值**：服务没给或给了 null 的字段才填示例值，真实值永远不会被覆盖。
 *
 * 返回的 `mockFields` 会一路传到 UI，用于打"示例"角标和"数据来源"面板，
 * 这样界面上不会出现"看不出是真数据还是假数据"的情况。
 */
export function applyMockPolicy(telemetry, options = {}) {
  const { forceMock = false, serviceReachable = true, tMs = Date.now() } = options

  // 服务连不上时也必须给出一份完整数据，否则首页会显示成"电量 0%、没有传感器"，
  // 看起来像坏了。这种情况下整份走示例数据，并由 UI 明确标注"未连接服务"。
  if (forceMock || !serviceReachable) {
    const simulated = simulateTelemetry(tMs, { maxRpm: MOCK_FAN_MAX_RPM })
    return {
      telemetry: simulated,
      // fullMock 表示"连电量、阈值这些本来由服务提供的字段也是假的"，UI 需要提示得更醒目。
      fullMock: true,
      mockFields: [...MOCK_SOURCES.map((source) => source.key), 'Sensors', 'Fans', ...SERVICE_FIELDS.map((f) => f.key)],
    }
  }

  const next = { ...telemetry }
  const mockFields = []

  for (const source of MOCK_SOURCES) {
    // 只补空值：服务给了 0 也要保留（0 是合法读数，不是"没有数据"）。
    if (next[source.key] === null || next[source.key] === undefined) {
      next[source.key] = source.fill(tMs / 1000)
      mockFields.push(source.key)
    }
  }

  // 传感器与风扇是数组：服务返回空数组时用示例数据把卡片撑起来。
  if (!next.Sensors || next.Sensors.length === 0) {
    const simulated = simulateTelemetry(tMs, { maxRpm: MOCK_FAN_MAX_RPM })
    next.Sensors = simulated.Sensors
    mockFields.push('Sensors')
  }
  if (!next.Fans || next.Fans.length === 0) {
    const simulated = simulateTelemetry(tMs, { maxRpm: MOCK_FAN_MAX_RPM })
    next.Fans = simulated.Fans
    mockFields.push('Fans')
  }

  return { telemetry: next, fullMock: false, mockFields }
}

/** 供"数据来源"面板展示：这个字段是真实值还是示例值，以及为什么。 */
export function describeField(key) {
  const source = MOCK_SOURCES.find((item) => item.key === key)
  if (source) {
    return { kind: 'mock', label: source.label, reason: source.reason, reference: source.reference }
  }
  if (key === 'Sensors') return { kind: 'mock', label: '温度传感器', ...MOCK_SENSORS }
  if (key === 'Fans') return { kind: 'mock', label: '风扇转速', ...MOCK_FANS }
  if (key === 'FanCurve') return { kind: 'mock', label: '风扇策略曲线', ...MOCK_FAN_CURVE }
  return { kind: 'live', label: key, reason: '来自 Honor Control 服务', reference: null }
}
