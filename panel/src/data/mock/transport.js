import { COMMANDS, PROTOCOL_VERSION, emptyTelemetry } from '../contract.js'
import { simulateHistory, simulateTelemetry, batteryPercentAt, isChargingAt } from './generator.js'

/**
 * 开发用的假服务。
 *
 * 只在两种情况下被加载（运行时动态 import，不会进入 Tauri 的启动路径）：
 * 1. 在浏览器里跑 `npm run dev`（没有管道可用）；
 * 2. 服务未安装/未启动时，让界面仍能完整演示。
 *
 * 它模拟的是**服务端的响应形状**（{ Version, Error, Telemetry... }），而不是绕过服务直接造数据。
 * 这样服务端一旦接好，前端只需要换成真实 transport，组件一行都不用改。
 */

// 仅开发用：模块级可变状态，让"改阈值 / 切模式"在界面上能看到反馈。
const state = {
  chargeStart: 40,
  chargeStop: 70,
  performanceMode: 1,
  autoReconcile: true,
  trayPolicy: 'OnDemand',
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function telemetry() {
  const simulated = simulateTelemetry(Date.now())
  return {
    ...emptyTelemetry(),
    ...simulated,
    ChargeStartPercent: state.chargeStart,
    ChargeStopPercent: state.chargeStop,
    PerformanceMode: state.performanceMode,
    // 未接适配器时不可能是充入状态，这里保持自洽，避免出现"拔电还在充电"的假象。
    BatteryPowerW: simulated.PluggedIn ? simulated.BatteryPowerW : Math.min(0, simulated.BatteryPowerW ?? 0),
  }
}

function snapshot() {
  const current = telemetry()
  return {
    Desired: {
      ChargeStart: state.chargeStart,
      ChargeEnd: state.chargeStop,
      PerformanceMode: state.performanceMode,
      AutoReconcile: state.autoReconcile,
    },
    Actual: {
      ChargeStart: state.chargeStart,
      ChargeEnd: state.chargeStop,
      PerformanceMode: state.performanceMode,
      PowerSchemeName: state.performanceMode === 2 ? 'Honor Performance' : '平衡',
      IsOnAcPower: current.PluggedIn,
      BatteryPercent: Math.round(current.BatteryPercent),
      PcManagerOpen: false,
      ChargeError: null,
      PerformanceError: null,
      ServiceError: null,
      CheckedAt: current.CheckedAt,
    },
    TrayPolicy: state.trayPolicy,
    ServiceVersion: '0.1.0-mock',
  }
}

export async function mockRequest(command, desired = null) {
  await delay(40)

  switch (command) {
    case COMMANDS.GetTelemetry:
      return { Version: PROTOCOL_VERSION, Error: null, Telemetry: telemetry() }

    case COMMANDS.GetSnapshot:
      return { Version: PROTOCOL_VERSION, Error: null, Snapshot: snapshot() }

    case COMMANDS.GetCapabilities:
      // 注意：Mock 模式下如实声明"哪些指标服务其实拿不到"，
      // 这样"数据来源"面板展示的内容与真实情况一致。
      return {
        Version: PROTOCOL_VERSION,
        Error: null,
        Capabilities: {
          AdapterRatedW: 180,
          FanMaxRpm: 6000,
          Supports: {
            BatteryTemperature: false,
            BatteryPower: false,
            AdapterPower: false,
            Sensors: false,
            Fans: false,
            PowerLimits: false,
            FanCurve: false,
          },
          MissingReason: {
            BatteryTemperature: 'root\\wmi 电池类待真机确认',
            Sensors: 'CPU/GPU/SSD 温度暂无用户态来源',
            Fans: '0x0802 语义未确认',
            FanCurve: '需荣耀内核驱动，无用户态通道',
          },
        },
      }

    case COMMANDS.GetHistory: {
      const metric = desired?.Metric ?? 'BatteryPower'
      const range = desired?.Range ?? '24h'
      const map = { '1h': [1, 180], '24h': [24, 240], '7d': [168, 300] }
      const [hours, points] = map[range] ?? map['24h']
      return {
        Version: PROTOCOL_VERSION,
        Error: null,
        History: { Metric: metric, Range: range, Hours: hours, Samples: simulateHistory(toGeneratorMetric(metric), hours, points) },
      }
    }

    case COMMANDS.SetCharge: {
      const start = desired?.ChargeStart ?? state.chargeStart
      const end = desired?.ChargeEnd ?? state.chargeStop
      if (!(start < end)) {
        return { Version: PROTOCOL_VERSION, Error: '充电阈值必须满足 起始 < 停止。', Snapshot: snapshot() }
      }
      state.chargeStart = start
      state.chargeStop = end
      return { Version: PROTOCOL_VERSION, Error: null, Snapshot: snapshot() }
    }

    case COMMANDS.SetPerformance:
      state.performanceMode = desired?.PerformanceMode ?? state.performanceMode
      return { Version: PROTOCOL_VERSION, Error: null, Snapshot: snapshot() }

    case COMMANDS.SetAutoReconcile:
      state.autoReconcile = desired?.AutoReconcile ?? state.autoReconcile
      return { Version: PROTOCOL_VERSION, Error: null, Snapshot: snapshot() }

    case COMMANDS.SetTrayPolicy:
      state.trayPolicy = desired?.TrayPolicy ?? state.trayPolicy
      return { Version: PROTOCOL_VERSION, Error: null, Snapshot: snapshot() }

    case COMMANDS.ShutdownService:
      return { Version: PROTOCOL_VERSION, Error: '示例数据模式不会真的停止服务。', Snapshot: snapshot() }

    default:
      return { Version: PROTOCOL_VERSION, Error: `未知命令：${command}`, Snapshot: null }
  }
}

/** 把契约里的历史指标名映射到 generator 的命名。 */
function toGeneratorMetric(metric) {
  if (metric === 'BatteryPower') return 'batteryPower'
  if (metric === 'AdapterPower') return 'adapterPower'
  return 'systemLoad'
}

/** 仅供 demo 面板展示用：当前示例电量与充电方向，便于人工核对自洽性。 */
export const mockDebugInfo = () => ({
  batteryPercent: batteryPercentAt(Date.now() / 1000),
  charging: isChargingAt(Date.now() / 1000),
})
