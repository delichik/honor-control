import { useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  COMMANDS,
  HISTORY_METRICS,
  PROTOCOL_VERSION,
  emptyTelemetry,
} from './contract.js'
import { isTauriRuntime, serviceRequest, startService, stopService } from './transport.js'
import { applyMockPolicy } from './mock/index.js'
import { useAppStore } from '../app/store.js'
import { useNow } from '../app/useNow.js'

/**
 * 服务数据统一走这里。
 *
 * 面板与服务按同一份 v4 契约发布：版本不一致时直接报错让用户重装，不做降级兼容。
 * 唯一"降级"的是**服务拿不到的指标**——那些由 data/mock 补示例数据（见 5.1 矩阵）。
 *
 * 轮询频率按"服务端很便宜"设计：快照与遥测都只读服务端内存缓存，不打 WMI（可行性文档 5.3）。
 */

/** 服务返回 Error 字段时抛出去，让 Query 进入 error 状态，UI 就能显示具体原因。 */
function unwrap(response) {
  if (!response) throw new Error('服务返回了空响应。')
  if (response.Version !== PROTOCOL_VERSION) {
    throw new Error(
      `应用与服务的通信版本不一致（应用 v${PROTOCOL_VERSION}，服务 v${response.Version}），请重新安装应用。`,
    )
  }
  if (response.Error) throw new Error(response.Error)
  return response
}

/**
 * 服务快照查询。
 * 同一 key 在多处调用只会发一次请求，TanStack Query 会自动去重。
 */
function useSnapshotQuery() {
  return useQuery({
    queryKey: ['snapshot'],
    queryFn: async () => unwrap(await serviceRequest(COMMANDS.GetState)).Snapshot,
    refetchInterval: 5000,
    retry: 0,
  })
}

/** 服务健康状况：连接是否可用，以及用于诊断展示的协议版本。 */
export function useServiceHealth() {
  const query = useSnapshotQuery()

  return {
    serviceReachable: query.isSuccess,
    protocolVersion: PROTOCOL_VERSION,
    error: query.error,
    isLoading: query.isLoading,
  }
}

/** 快照本身（阈值、性能模式、电脑管家状态、托盘策略）。 */
export function useServiceSnapshot() {
  const query = useSnapshotQuery()
  return {
    snapshot: query.data ?? null,
    serviceReachable: query.isSuccess,
    error: query.error,
    isLoading: query.isLoading,
  }
}

/**
 * 实时遥测（1 秒）。
 *
 * 原生面板只显示服务读数；浏览器预览或用户主动开启示例模式时才显示整份模拟数据。
 */
export function useTelemetry() {
  const forceMock = useAppStore((state) => state.forceMock)
  const now = useNow(1000)
  const health = useServiceHealth()

  const query = useQuery({
    queryKey: ['telemetry'],
    queryFn: async () => unwrap(await serviceRequest(COMMANDS.GetTelemetry)).Telemetry,
    enabled: health.serviceReachable,
    refetchInterval: 1000,
    retry: 0,
    staleTime: 0,
  })

  const payload = query.data ?? null
  // 原生面板连接服务前后都不显示模拟电量；浏览器预览和用户显式开启的示例模式才使用模拟数据。
  const demoMode = forceMock || (!isTauriRuntime() && !health.isLoading && !health.serviceReachable)

  const result = useMemo(
    () => {
      if (demoMode) {
        return applyMockPolicy(emptyTelemetry(), { forceMock: true, serviceReachable: false, tMs: now })
      }
      if (!query.isSuccess || !payload) {
        return { telemetry: emptyTelemetry(), fullMock: false, mockFields: [] }
      }
      return applyMockPolicy(payload, { forceMock: false, serviceReachable: true, tMs: now })
    },
    [payload, demoMode, query.isSuccess, now],
  )

  return {
    telemetry: result.telemetry,
    mockFields: result.mockFields,
    fullMock: result.fullMock,
    serviceReachable: health.serviceReachable,
    error: query.error ?? health.error,
    isLoading: health.isLoading || query.isLoading,
  }
}

/** 服务能力（哪些传感器/指标真的存在，由服务的启动期探测给出）。 */
export function useCapabilities() {
  const health = useServiceHealth()
  const query = useQuery({
    queryKey: ['capabilities'],
    queryFn: async () => unwrap(await serviceRequest(COMMANDS.GetCapabilities)).Capabilities,
    enabled: health.serviceReachable,
    staleTime: 5 * 60 * 1000,
    retry: 0,
  })

  return { capabilities: query.data ?? null, error: query.error }
}

/**
 * 历史序列。
 *
 * 服务端返回记录或实时采样环中的数值；服务未连接或所选范围没有记录时返回空序列。
 * 面板不合成或补齐历史数据。
 */
export function useHistory(metricKey, range) {
  const health = useServiceHealth()
  const metric = HISTORY_METRICS[metricKey] ?? HISTORY_METRICS.batteryPower
  const query = useQuery({
    queryKey: ['history', metric, range.id],
    queryFn: async () =>
      unwrap(
        await serviceRequest(COMMANDS.GetHistory, null, { Metric: metric, Range: range.id, Points: range.points }),
      ).History,
    enabled: health.serviceReachable,
    refetchInterval: range.id === '1m' ? 1000 : 60 * 1000,
    retry: 0,
  })

  const samples = query.data?.Samples ?? []
  return {
    samples,
    hours: query.data?.Hours ?? range.hours,
    hasData: samples.some(Number.isFinite),
    hasGaps: samples.some((value) => !Number.isFinite(value)),
    serviceReachable: health.serviceReachable,
    isLoading: health.isLoading || query.isLoading,
    error: query.error ?? health.error,
  }
}

/** 写命令的统一收尾：成功后让快照/遥测立即失效，失败时把服务返回的原因交给调用方。 */
function useServiceMutation(command) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (desired) => unwrap(await serviceRequest(command, desired)).Snapshot,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['snapshot'] })
      queryClient.invalidateQueries({ queryKey: ['telemetry'] })
    },
  })
}

export function useSetChargeThresholds() {
  return useServiceMutation(COMMANDS.SetCharge)
}

export function useSetPerformanceMode() {
  return useServiceMutation(COMMANDS.SetPerformance)
}

export function useSetAutoReconcile() {
  return useServiceMutation(COMMANDS.SetAutoReconcile)
}

/**
 * 启动服务（SCM，不是管道命令）。
 * SCM 操作通过显式 UAC 授权执行。
 */
export function useStartService() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const result = await startService()
      if (!result.started) throw new Error(result.message ?? '启动服务失败。')
      return result
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['snapshot'] })
      queryClient.invalidateQueries({ queryKey: ['telemetry'] })
    },
  })
}

/** 停止服务也走 SCM，并显式请求 UAC；托盘退出只关闭托盘进程。 */
export function useStopService() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const result = await stopService()
      if (!result.stopped) throw new Error(result.message ?? '停止服务失败。')
      return result
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['snapshot'] })
      queryClient.invalidateQueries({ queryKey: ['telemetry'] })
    },
  })
}
