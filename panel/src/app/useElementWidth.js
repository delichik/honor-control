import { useLayoutEffect, useRef, useState } from 'react'

/**
 * 读一个元素的实际宽度。
 *
 * 用途：设计稿是按 1400px 窗口画的，母线行（168 + 自适应 + 430 + 读数）在窄窗口里放不下。
 * 与其用媒体查询猜（媒体查询看的是视口，而卡片宽度还受导航栏与内边距影响），
 * 不如直接量卡片自己的宽度，再决定是否换成上下堆叠的排版。
 *
 * 用 useLayoutEffect 而不是 useEffect：SVG 图表的 viewBox 依赖这个宽度，
 * 绘制之后再量会先按兜底宽度画一帧再跳变（截图里就是"图比容器窄一截"）。
 */
export function useElementWidth() {
  const ref = useRef(null)
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return undefined
    const measure = () => setWidth(element.getBoundingClientRect().width)
    measure()
    if (typeof ResizeObserver === 'undefined') return undefined
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return [ref, width]
}
