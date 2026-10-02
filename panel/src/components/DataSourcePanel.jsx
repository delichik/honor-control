import { Badge, Switch, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Text, tokens } from '@fluentui/react-components'
import { SectionCard } from './SectionCard.jsx'
import { MOCK_SENSORS, MOCK_FANS, MOCK_SOURCES, SERVICE_FIELDS } from '../data/mock/sources.js'
import { useAppStore } from '../app/store.js'

/**
 * 数据来源面板（开发/诊断用）。
 *
 * 面板里有一批指标服务当前拿不到，前端先用示例数据占位（见 data/mock/sources.js）。
 * 这个面板把"哪些是真的、哪些是假的、为什么"摊开给使用者看，
 * 避免出现"界面很漂亮但不知道哪些数字能信"的情况。
 */
export function DataSourcePanel() {
  const forceMock = useAppStore((state) => state.forceMock)
  const setForceMock = useAppStore((state) => state.setForceMock)

  const mockRows = [
    ...MOCK_SOURCES.map((source) => ({ label: source.label, reason: source.reason, reference: source.reference })),
    { label: '温度传感器（CPU/GPU/SSD）', ...MOCK_SENSORS },
    { label: '风扇转速', ...MOCK_FANS },
  ]

  return (
    <SectionCard
      title="数据来源"
      subtitle="服务已提供的字段与当前用示例数据占位的字段"
      actions={
        <Switch
          checked={forceMock}
          onChange={(_, data) => setForceMock(data.checked)}
          label="全量示例数据"
        />
      }
    >
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
        打开"全量示例数据"后，界面完全由模拟模型驱动，便于在没有服务时预览与开发。
        服务接上对应指标后，删除 data/mock/sources.js 里的条目即可，组件无需改动。
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
          {SERVICE_FIELDS.map((field) => (
            <TableRow key={field.key}>
              <TableCell>{field.label}</TableCell>
              <TableCell>
                <Badge appearance="tint" color="success" size="small">服务</Badge>
              </TableCell>
              <TableCell>
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  由 Honor Control 服务通过 ACPI-WMI 读取
                </Text>
              </TableCell>
            </TableRow>
          ))}
          {mockRows.map((row) => (
            <TableRow key={row.label}>
              <TableCell>{row.label}</TableCell>
              <TableCell>
                <Badge appearance="tint" color="warning" size="small">示例</Badge>
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
