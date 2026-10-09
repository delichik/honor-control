import { useEffect, useState } from 'react'

/**
 * 每秒（默认）返回一次新的时间戳。
 *
 * 用途：示例数据是"时间的纯函数"（见 data/mock/generator.js），需要一个节拍驱动重渲染。
 * 服务可用时不需要它——数据由 TanStack Query 的轮询驱动。
 */
export function useNow(intervalMs = 1000, enabled = true) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!enabled) return undefined
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs, enabled])

  return now
}
