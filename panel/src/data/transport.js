import { invoke } from '@tauri-apps/api/core'

/**
 * 面板 → 服务的唯一出口。
 *
 * 真实链路：本文件 → Tauri 命令 `service_request`（Rust）→ 命名管道 → Honor Control 服务。
 * 浏览器开发：检测不到 Tauri 运行时，动态 import 假服务（data/mock/transport.js）。
 */

export function isTauriRuntime() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window
}

/**
 * 发一条服务请求。
 * @param {string} command contract.js 的 COMMANDS 之一
 * @param {object|null} desired 写命令的载荷
 * @returns {Promise<{Version:number, Error:string|null, Snapshot?:object, Telemetry?:object, Capabilities?:object, History?:object}>}
 */
export async function serviceRequest(command, desired = null) {
  if (!isTauriRuntime()) {
    const { mockRequest } = await import('./mock/transport.js')
    return mockRequest(command, desired)
  }

  try {
    return await invoke('service_request', { command, desired })
  } catch (error) {
    // Rust 侧把所有失败都映射成可以直接展示给用户的中文串；这里只做兜底。
    throw new Error(typeof error === 'string' ? error : (error?.message ?? '与服务通信失败。'))
  }
}

/**
 * 启动 Honor Control 服务。
 *
 * 注意：这不是管道命令——服务自己没法把自己启动起来，必须走 SCM。安装器已经给服务
 * 安全描述符授予了 Authenticated Users 的 SERVICE_START，所以中完整性的面板可以直接调用。
 *
 * TODO(rust): `start_service` 命令还没在 src-tauri 里实现（需要在 Rust 侧调用 advapi32 的
 * OpenSCManagerW/OpenServiceW/StartServiceW）。在补上之前，这里会明确失败并把原因交给 UI，
 * 而不是静默什么都不做。
 */
export async function startService() {
  if (!isTauriRuntime()) {
    return { started: false, message: '浏览器开发模式下无法启动服务。' }
  }
  try {
    await invoke('start_service')
    return { started: true, message: null }
  } catch (error) {
    const message = typeof error === 'string' ? error : (error?.message ?? '启动服务失败。')
    return { started: false, message }
  }
}

/** 请求服务自行停止（owner-only 管道命令）。托盘菜单的"退出"走同一条路径。 */
export async function shutdownService() {
  return serviceRequest('ShutdownService')
}

/**
 * 拉起托盘进程。
 *
 * 策略 `OnDemand`（默认）的含义是"用户在场时才出现托盘"，而用户在场最直接的信号就是打开了面板，
 * 所以由面板来拉起它。托盘有单实例互斥量，重复调用无害。
 *
 * 浏览器开发模式下没有托盘可拉，直接返回 false（不是错误）。
 */
export async function launchTray() {
  if (!isTauriRuntime()) return false
  try {
    return await invoke('launch_tray')
  } catch (error) {
    const message = typeof error === 'string' ? error : (error?.message ?? '启动托盘进程失败。')
    throw new Error(message)
  }
}
