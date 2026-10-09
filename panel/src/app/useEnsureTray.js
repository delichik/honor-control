import { useEffect, useRef } from 'react'
import { isTauriRuntime, launchTray } from '../data/transport.js'

/**
 * 面板打开时拉起独立托盘。托盘单实例运行，不依赖服务状态。
 */
export function useEnsureTray() {
  const attempted = useRef(false)

  useEffect(() => {
    if (attempted.current) return
    // 浏览器开发模式没有托盘可拉
    if (!isTauriRuntime()) return

    attempted.current = true
    launchTray().catch((error) => {
      // 托盘拉不起来不影响面板使用；设置页会显示服务状态，这里不打断用户。
      console.warn('启动托盘进程失败：', error.message)
    })
  }, [])
}
