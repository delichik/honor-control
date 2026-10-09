import { useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  COMMANDS,
  HISTORY_METRICS,
  PROTOCOL_VERSION,
  emptyTelemetry,
} from './contract.js'
import { serviceRequest, startService } from './transport.js'
import { applyMockPolicy } from './mock/index.js'
import { simulateHistory } from './mock/generator.js'
import { useAppStore } from '../app/store.js'
import { useNow } from '../app/useNow.js'

/**
 * 服务数据统一走这里。
 *
 * 面板与服务按同一份 v3 契约发布：版本不一致时直接报错让用户重装，不做降级兼容。
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
 * 服务连不上时整份走示例数据，并由 `fullMock` 标注"连电量这类本该来自服务的字段也是假的"。
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

  const telemetryAvailable = query.isSuccess
  const payload = query.data ?? null

  const result = useMemo(
    () =>
      applyMockPolicy(payload ?? emptyTelemetry(), {
        forceMock,
        serviceReachable: telemetryAvailable,
        tMs: now,
      }),
    [payload, forceMock, telemetryAvailable, now],
  )

  return {
    telemetry: result.telemetry,
    mockFields: result.mockFields,
    fullMock: result.fullMock,
    serviceReachable: health.serviceReachable,
    error: health.error,
    isLoading: health.isLoading,
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
 * 服务端返回的是**等间隔的数值数组**（不带时间戳）+ Hours，聚合口径见 data/derive.js。
 * 服务不可用时用同一套模型合成序列，保证监控页在开发期也有内容可看。
 */
export function useHistory(metricKey, range) {
  const health = useServiceHealth()
  const metric = HISTORY_METRICS[metricKey] ?? HISTORY_METRICS.batteryPower
  const now = useNow(5000)

  const query = useQuery({
    queryKey: ['history', metric, range.id],
    queryFn: async () =>
      unwrap(
        await serviceRequest(COMMANDS.GetHistory, { Metric: metric, Range: range.id, Points: range.points }),
      ).History,
    enabled: health.serviceReachable,
    refetchInterval: 60 * 1000,
    retry: 0,
  })

  const simulated = !query.isSuccess
  const samples = useMemo(() => {
    if (query.data?.Samples?.length) return query.data.Samples
    return simulateHistory(metricKey, range.hours, range.points, now)
  }, [query.data, metricKey, range.hours, range.points, now])

  return { samples, hours: query.data?.Hours ?? range.hours, simulated, error: query.error }
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

/** 托盘策略是 v3 命令；服务没升级时写不进去，UI 需要禁用该项。 */
export function useSetTrayPolicy() {
  return useServiceMutation(COMMANDS.SetTrayPolicy)
}

/**
 * 启动服务（SCM，不是管道命令）。
 * 安装器给服务授予了 Users SERVICE_START，所以面板不需要提权。
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

/** 请求服务自行停止（owner-only）。面板上的"停止服务"与托盘菜单的"退出"是同一条路径。 */
export function useShutdownService() {
  return useServiceMutation(COMMANDS.ShutdownService)
}
