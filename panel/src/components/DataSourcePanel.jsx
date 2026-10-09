import { Badge, Switch, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Text, tokens } from '@fluentui/react-components'
import { SectionCard } from './SectionCard.jsx'
import { MOCK_SENSORS, MOCK_FANS, MOCK_SOURCES, SERVICE_FIELDS } from '../data/mock/sources.js'
import { useAppStore } from '../app/store.js'
import { useServiceSnapshot, useTelemetry } from '../data/queries.js'
import { deriveSystemLoadW } from '../data/derive.js'

/**
 * 数据来源面板（开发/诊断用）。
 *
 * 按当前服务响应逐项标出实时值、示例值或暂不可用字段。
 * 这个面板把"哪些是真的、哪些是假的、为什么"摊开给使用者看，
 * 避免出现"界面很漂亮但不知道哪些数字能信"的情况。
 */
export function DataSourcePanel() {
  const forceMock = useAppStore((state) => state.forceMock)
  const setForceMock = useAppStore((state) => state.setForceMock)
  const { telemetry, mockFields } = useTelemetry()
  const { snapshot } = useServiceSnapshot()

  const status = (key, value) => {
    const derivedMock = key === 'SystemLoadW' && telemetry.PluggedIn && mockFields.includes('AdapterPowerW')
    if (mockFields.includes(key) || derivedMock) return 'example'
    return value === null || value === undefined ? 'unavailable' : 'service'
  }

  const serviceRows = SERVICE_FIELDS.map((field) => {
    let value = telemetry[field.key]
    if (field.key === 'SystemLoadW') {
      value = deriveSystemLoadW({
        serviceValue: telemetry.SystemLoadW,
        adapterPowerW: telemetry.AdapterPowerW,
        batteryPowerW: telemetry.BatteryPowerW,
        pluggedIn: telemetry.PluggedIn,
      })
    } else if (field.key === 'ChargeStartPercent') {
      value = snapshot?.Actual?.ChargeStart
    } else if (field.key === 'ChargeStopPercent') {
      value = snapshot?.Actual?.ChargeEnd
    } else if (field.key === 'PerformanceMode') {
      value = snapshot?.Actual?.PerformanceMode
    }
    const kind = status(field.key, value)
    return {
      key: field.key,
      label: field.label,
      kind,
      reason: kind === 'example'
        ? field.key === 'SystemLoadW' && telemetry.PluggedIn
          ? '当前系统负载由示例适配器功率推算。'
          : '服务不可用时显示全量示例数据。'
        : kind === 'unavailable'
          ? '服务尚未返回实际读数。'
          : '当前值来自 Honor Control 服务。',
    }
  })

  const hardwareRows = MOCK_SOURCES.map((source) => {
    const kind = status(source.key, telemetry[source.key])
    return {
      key: source.key,
      label: source.label,
      kind,
      reason: kind === 'service'
        ? '当前值由 Honor Control 服务实时返回。'
        : `${source.reason}${source.reference ? `（${source.reference}）` : ''}`,
    }
  })

  const sensorRows = [
    {
      key: 'Sensors',
      label: '温度传感器（CPU/GPU/SSD）',
      kind: status('Sensors', telemetry.Sensors?.length ? telemetry.Sensors : null),
      reason: mockFields.includes('Sensors') ? MOCK_SENSORS.reason : '服务未从此设备返回温度传感器。',
    },
    {
      key: 'Fans',
      label: '风扇转速',
      kind: status('Fans', telemetry.Fans?.length ? telemetry.Fans : null),
      reason: mockFields.includes('Fans') ? MOCK_FANS.reason : '服务未从此设备返回风扇 RPM。',
    },
  ]
  const rows = [...serviceRows, ...hardwareRows, ...sensorRows]

  return (
    <SectionCard
      title="数据来源"
      subtitle="当前每项数据的实际来源、示例值或缺失状态"
      actions={
        <Switch
          checked={forceMock}
          onChange={(_, data) => setForceMock(data.checked)}
          label="全量示例数据"
        />
      }
    >
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
        打开"全量示例数据"后，界面完全由模拟模型驱动。其他情况下每行按服务当前返回的值标记。
      </Text>

      <Table size="small" aria-label="数据来源">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>字段</TableHeaderCell>
            <TableHeaderCell>来源</TableHeaderCell>
            <TableHeaderCell>说明</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.key}>
              <TableCell>{row.label}</TableCell>
              <TableCell>
                <Badge
                  appearance="tint"
                  color={row.kind === 'service' ? 'success' : row.kind === 'example' ? 'warning' : 'danger'}
                  size="small"
                >
                  {row.kind === 'service' ? '服务' : row.kind === 'example' ? '示例' : '暂无数据'}
                </Badge>
              </TableCell>
              <TableCell>
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  {row.reason}
                  {row.reference ? `（${row.reference}）` : ''}
                </Text>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </SectionCard>
  )
}
