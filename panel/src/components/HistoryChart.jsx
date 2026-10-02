import { useMemo } from 'react'
import {
  Area,
  AreaChart,
  CartesianGrid,
  Legend,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip as ChartTooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { makeStyles, useFluent } from '@fluentui/react-components'

const useStyles = makeStyles({
  wrap: { width: '100%', height: '320px' },
})

/**
 * 历史曲线。
 *
 * 数据是**等间隔的数值数组**（服务端降采样后返回，见可行性文档 5.4），不带时间戳，
 * 因此横轴用下标换算成"相对现在的时间"，纵轴单位由调用方给出。
 * 多个序列共用同一条横轴（长度必须一致），用于把适配器功率与系统负载叠在一张图上。
 *
 * 颜色用 `colorToken`（Fluent token 名）传入，在这里解析成字面量：
 * Recharts 把颜色写成 SVG 表现属性，而 token 是 var(--…) 字符串，属性里不生效。
 */
export function HistoryChart({ series, hours, yDomain = ['auto', 'auto'], unit = '' }) {
  const styles = useStyles()
  const { theme } = useFluent()

  const resolveColor = (item) => theme[item.colorToken] ?? item.color ?? theme.colorBrandStroke1

  const data = useMemo(() => {
    const length = series[0]?.samples?.length ?? 0
    const points = []
    for (let index = 0; index < length; index += 1) {
      const point = { index }
      for (const item of series) point[item.key] = item.samples[index]
      points.push(point)
    }
    return points
  }, [series])

  // 横轴刻度：按范围给 5 个相对时间点
  const ticks = useMemo(() => {
    const length = series[0]?.samples?.length ?? 0
    if (!length) return []
    const step = Math.max(1, Math.floor((length - 1) / 4))
    const result = []
    for (let index = 0; index < length; index += step) {
      result.push(index)
    }
    if (result[result.length - 1] !== length - 1) result.push(length - 1)
    return result
  }, [series, hours])

  const formatTick = (index) => {
    const length = series[0]?.samples?.length ?? 1
    const backHours = hours * (1 - index / Math.max(1, length - 1))
    if (backHours < 1 / 60) return '现在'
    if (backHours < 1) return `-${Math.round(backHours * 60)} 分`
    if (backHours < 48) return `-${backHours.toFixed(backHours < 10 ? 1 : 0)} 小时`
    return `-${(backHours / 24).toFixed(1)} 天`
  }

  return (
    <div className={styles.wrap}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: -14 }}>
          <defs>
            {series.map((item) => (
              <linearGradient key={item.key} id={`fill-${item.key}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor={resolveColor(item)} stopOpacity={0.45} />
                <stop offset="95%" stopColor={resolveColor(item)} stopOpacity={0.04} />
              </linearGradient>
            ))}
          </defs>
          <CartesianGrid stroke={theme.colorNeutralStroke3} strokeDasharray="3 4" />
          <XAxis
            dataKey="index"
            ticks={ticks}
            tickFormatter={formatTick}
            tick={{ fontSize: 11, fill: theme.colorNeutralForeground3 }}
            interval={0}
          />
          <YAxis
            domain={yDomain}
            tick={{ fontSize: 11, fill: theme.colorNeutralForeground3 }}
            width={52}
            unit={unit}
          />
          <ChartTooltip
            contentStyle={{ fontSize: 12 }}
            labelFormatter={(index) => formatTick(index)}
            formatter={(value, name) => [`${Number(value).toFixed(1)} ${unit}`, name]}
          />
          <Legend wrapperStyle={{ fontSize: 12 }} />
          {/* 0 线：充放电历史以它为界，一眼看出是充还是放 */}
          <ReferenceLine y={0} stroke={theme.colorNeutralStroke1} strokeDasharray="4 4" />
          {series.map((item) => (
            <Area
              key={item.key}
              type="monotone"
              dataKey={item.key}
              name={item.label}
              stroke={resolveColor(item)}
              strokeWidth={2}
              strokeDasharray={item.dash}
              fill={item.fill ? `url(#fill-${item.key})` : 'transparent'}
              isAnimationActive={false}
            />
          ))}
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}
