import { useEffect, useRef } from 'react'
import { useServiceSnapshot } from '../data/queries.js'
import { isTauriRuntime, launchTray } from '../data/transport.js'
import { useAppStore } from './store.js'

/**
 * 确保托盘进程在用户打开面板时出现。
 *
 * 这是 `OnDemand` 策略的落地方式：服务不主动拉起（登录后不会自己冒出来），
 * 用户在面板上"到场"时才由面板拉起。`Always` 也会调一次——托盘有单实例互斥量，重复无害。
 *
 * 只在拿到策略之后触发一次（用 ref 守住），避免每次快照轮询都去 spawn 一个进程。
 */
export function useEnsureTray() {
  const { snapshot, serviceReachable } = useServiceSnapshot()
  const forceMock = useAppStore((state) => state.forceMock)
  const attempted = useRef(false)

  const policy = snapshot?.TrayPolicy ?? null

  useEffect(() => {
    if (attempted.current) return
    // 服务没连上时策略未知，等连上再说
    if (!serviceReachable || policy === null) return
    // 浏览器开发模式没有托盘可拉
    if (!isTauriRuntime()) return
    // 策略 Off：用户明确不要托盘
    if (policy === 'Off') return
    if (forceMock) return

    attempted.current = true
    launchTray().catch((error) => {
      // 托盘拉不起来不影响面板使用；设置页会显示服务状态，这里不打断用户。
      console.warn('启动托盘进程失败：', error.message)
    })
  }, [policy, serviceReachable, forceMock])
}
