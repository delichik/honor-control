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
 * @param {object|null} history 历史查询参数
 * @returns {Promise<{Version:number, Error:string|null, Snapshot?:object, Telemetry?:object, Capabilities?:object, History?:object}>}
 */
export async function serviceRequest(command, desired = null, history = null, windowsPower = null) {
  if (!isTauriRuntime()) {
    const { mockRequest } = await import('./mock/transport.js')
    return mockRequest(command, desired, history, windowsPower)
  }

  try {
    return await invoke('service_request', { command, desired, history, windowsPower })
  } catch (error) {
    // Rust 侧把所有失败都映射成可以直接展示给用户的中文串；这里只做兜底。
    throw new Error(typeof error === 'string' ? error : (error?.message ?? '与服务通信失败。'))
  }
}

/**
 * 启动 Honor Control 服务。
 *
 * 这不是管道命令，而是 SCM 操作；Rust 侧会显式请求 UAC。
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

/** 停止服务（SCM），显式请求 UAC；托盘退出只退出托盘进程。 */
export async function stopService() {
  if (!isTauriRuntime()) {
    return { stopped: false, message: '浏览器开发模式下无法停止服务。' }
  }
  try {
    await invoke('stop_service')
    return { stopped: true, message: null }
  } catch (error) {
    const message = typeof error === 'string' ? error : (error?.message ?? '停止服务失败。')
    return { stopped: false, message }
  }
}

/**
 * 拉起托盘进程。
 *
 * 面板打开时单独拉起托盘，不依赖服务状态或配置；托盘有单实例互斥量，重复调用无害。
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

/** OEM 显示/声音组件在当前交互会话执行；不通过 Session 0 的服务改变显示上下文。 */
export async function oemRequest(command, args = {}) {
  if (!['get_oem_features', 'oem_set_display', 'oem_set_audio', 'open_oem_page'].includes(command)) {
    throw new Error('未知的显示或音频操作。')
  }
  if (!isTauriRuntime()) {
    const { mockOemRequest } = await import('./mock/oem.js')
    return mockOemRequest(command, args)
  }
  try {
    return await invoke(command, args)
  } catch (error) {
    throw new Error(typeof error === 'string' ? error : (error?.message ?? '荣耀显示或音频组件操作失败。'))
  }
}
