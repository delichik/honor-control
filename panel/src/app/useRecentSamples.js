import { useRef } from 'react'

/**
 * 保留最近 N 个采样值，用于首页"最近一分钟"这类实时小图。
 *
 * 为什么不用 TanStack Query 缓存：这是**渲染期的滚动窗口**，不是服务端数据。
 * 服务端 v3 的 GetHistory 用的是降采样后的长区间序列，不适合画秒级曲线。
 */
export function useRecentSamples(value, size = 60) {
  const bufferRef = useRef([])
  const lastRef = useRef(null)

  // 同一个值重复渲染时不重复入队，避免窗口被同一秒的多次渲染挤满。
  if (value !== lastRef.current) {
    lastRef.current = value
    const buffer = bufferRef.current
    buffer.push(value ?? 0)
    if (buffer.length > size) buffer.splice(0, buffer.length - size)
  }

  return bufferRef.current
}
