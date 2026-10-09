import {
  MOCK_FANS,
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
 *   - 服务连接正常时原样返回；缺失字段保持 null/空数组，不用示例值冒充实测数据。
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

  return { telemetry, fullMock: false, mockFields: [] }
}

/** 供"数据来源"面板展示：这个字段是真实值还是示例值，以及为什么。 */
export function describeField(key) {
  const source = MOCK_SOURCES.find((item) => item.key === key)
  if (source) {
    return { kind: 'mock', label: source.label, reason: source.reason, reference: source.reference }
  }
  if (key === 'Sensors') return { kind: 'mock', label: '温度传感器', ...MOCK_SENSORS }
  if (key === 'Fans') return { kind: 'mock', label: '风扇转速', ...MOCK_FANS }
  return { kind: 'live', label: key, reason: '来自 Honor Control 服务', reference: null }
}
