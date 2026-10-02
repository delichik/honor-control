import { useMemo } from 'react'
import {
  CartesianGrid,
  Line,
  LineChart,
  ReferenceDot,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip as ChartTooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Text, makeStyles, tokens, useFluent } from '@fluentui/react-components'
import { EXAMPLE_CURVES_BY_MODE, rpmForTemperature } from '../data/fanCurve.js'

const useStyles = makeStyles({
  wrap: { width: '100%', height: '200px' },
  legend: {
    display: 'flex',
    gap: '14px',
    flexWrap: 'wrap',
    color: tokens.colorNeutralForeground3,
  },
  swatch: {
    display: 'inline-block',
    width: '10px',
    height: '10px',
    borderRadius: '2px',
    marginRight: '5px',
  },
})

/**
 * 风扇曲线：横轴温度、纵轴转速。
 *
 * 之所以画曲线而不是并排列几个数字：性能模式决定的就是这条曲线。
 * 各传感器是曲线上的竖线+点（当前温度、该温度下策略要求的转速），
 * 实心点是实际工作点（最高温度 × 实际转速）——它应该落在曲线上，偏离说明风扇没跟上策略。
 *
 * ⚠️ 曲线本身是示例策略（见 data/fanCurve.js 的说明）；温度和转速按当前数据来源可能是示例数据，
 * 卡片上会单独标注。
 */
export function FanCurve({ sensors = [], fans = [], performanceMode = 1 }) {
  const styles = useStyles()
  // Recharts 把 stroke/fill 写成 SVG 属性，而 token 是 var(--…) 字符串——属性里不生效，
  // 所以图表颜色统一用 useFluent() 解析出的字面量（CSS 里的 tokens 不受影响）。
  const { theme } = useFluent()
  const curve = EXAMPLE_CURVES_BY_MODE[performanceMode] ?? EXAMPLE_CURVES_BY_MODE[1]
  const maxRpm = Math.max(...curve.map((point) => point.r), ...fans.map((fan) => fan.MaxRpm ?? 0), 6000)

  const data = useMemo(() => {
    // 把折线加密成采样点，recharts 的 monotone 曲线会更平滑
    const step = 2.5
    const points = []
    for (let t = 25; t <= 100; t += step) {
      points.push({ t, r: rpmForTemperature(curve, t) })
    }
    return points
  }, [curve])

  const hottest = sensors.reduce(
    (acc, sensor) => (acc === null || sensor.TempC > acc.TempC ? sensor : acc),
    null,
  )
  const averageRpm = fans.length
    ? fans.reduce((sum, fan) => sum + fan.Rpm, 0) / fans.length
    : null

  return (
    <div>
      <div className={styles.wrap}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 12, bottom: 4, left: -18 }}>
            <CartesianGrid stroke={theme.colorNeutralStroke3} strokeDasharray="3 4" />
            <XAxis
              dataKey="t"
              type="number"
              domain={[25, 100]}
              ticks={[30, 45, 60, 75, 90]}
              tick={{ fontSize: 11, fill: theme.colorNeutralForeground3 }}
              unit="°"
            />
            <YAxis
              domain={[0, maxRpm]}
              tick={{ fontSize: 11, fill: theme.colorNeutralForeground3 }}
              width={54}
            />
            <ChartTooltip
              contentStyle={{ fontSize: 12 }}
              formatter={(value, name) => (name === 'r' ? [`${Math.round(value)} RPM`, '策略转速'] : [value, name])}
              labelFormatter={(label) => `${label} °C`}
            />
            <Line
              type="monotone"
              dataKey="r"
              stroke={theme.colorBrandStroke1}
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />

            {/* 每个传感器一条竖线：读出"当前温度落在策略的什么位置" */}
            {sensors.map((sensor) => (
              <ReferenceLine
                key={sensor.Id}
                x={sensor.TempC}
                stroke={theme.colorNeutralStroke1}
                strokeDasharray="3 3"
              />
            ))}

            {/* 实际工作点：最高温 × 实际平均转速 */}
            {hottest && averageRpm !== null && hottest.TempC <= 100 ? (
              <ReferenceDot
                x={hottest.TempC}
                y={averageRpm}
                r={6}
                fill={theme.colorPaletteGreenBackground3}
                stroke={theme.colorNeutralBackground1}
                strokeWidth={2}
              />
            ) : null}
          </LineChart>
        </ResponsiveContainer>
      </div>

      <div className={styles.legend}>
        <Text size={200}>
          <span className={styles.swatch} style={{ background: tokens.colorBrandStroke1 }} />
          策略曲线（示例）
        </Text>
        <Text size={200}>
          <span className={styles.swatch} style={{ background: tokens.colorNeutralStroke1 }} />
          传感器当前温度
        </Text>
        <Text size={200}>
          <span className={styles.swatch} style={{ background: tokens.colorPaletteGreenBackground3 }} />
          实际工作点
        </Text>
      </div>
    </div>
  )
}
