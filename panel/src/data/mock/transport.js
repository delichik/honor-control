import { COMMANDS, PROTOCOL_VERSION, emptyTelemetry } from '../contract.js'
import { simulateTelemetry, batteryPercentAt, isChargingAt } from './generator.js'

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
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function telemetry() {
  const simulated = simulateTelemetry(Date.now())
  return {
    ...emptyTelemetry(),
    ...simulated,
    // 假服务保留空字段；预览数据统一由 data/mock/index.js 标记为全量示例。
    BatteryTemperatureC: null,
    AdapterPowerW: null,
    BatteryHealthPercent: null,
    BatteryDesignCapacityWh: null,
    Sensors: [],
    Fans: [],
    // 系统负载：服务只在拔掉适配器时才能如实给出（插电时整机负载需要适配器功率）。
    SystemLoadW: simulated.PluggedIn ? null : simulated.SystemLoadW,
    ChargeStartPercent: state.chargeStart,
    ChargeStopPercent: state.chargeStop,
    PerformanceMode: state.performanceMode,
    // 功率符号由生成器按插拔状态给出（接着=正、拔掉=负）。这里不要再"修正"一次：
    // 之前把它钳成 min(0, x) 会让未接电源时的示例曲线永远是一条 0 线。
    BatteryPowerW: simulated.BatteryPowerW,
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
    ServiceVersion: '0.1.0-mock',
  }
}

export async function mockRequest(command, desired = null, history = null) {
  await delay(40)

  switch (command) {
    case COMMANDS.GetTelemetry:
      return { Version: PROTOCOL_VERSION, Error: null, Telemetry: telemetry() }

    case COMMANDS.GetState:
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
          },
          MissingReason: {
            BatteryTemperature: 'root\\wmi 电池类待真机确认',
            Sensors: 'CPU/GPU/SSD 温度暂无用户态来源',
            Fans: '0x0802 语义未确认',
          },
        },
      }

    case COMMANDS.GetHistory: {
      const query = history ?? desired
      const metric = query?.Metric ?? 'BatteryPower'
      const range = query?.Range ?? '24h'
      const map = { '1m': [1 / 60, 60], '1h': [1, 60], '24h': [24, 240], '7d': [168, 300] }
      const [hours] = map[range] ?? map['24h']
      return {
        Version: PROTOCOL_VERSION,
        Error: null,
        History: { Metric: metric, Range: range, Hours: hours, Samples: [] },
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

    default:
      return { Version: PROTOCOL_VERSION, Error: `未知命令：${command}`, Snapshot: null }
  }
}

/** 仅供 demo 面板展示用：当前示例电量与充电方向，便于人工核对自洽性。 */
export const mockDebugInfo = () => ({
  batteryPercent: batteryPercentAt(Date.now() / 1000),
  charging: isChargingAt(Date.now() / 1000),
})
