/**
 * 示例数据生成器。
 *
 * 目标：在没有服务、或者服务拿不到某些指标时，界面依然能完整、**自洽**地渲染。
 * 因此这里不是一个随机数池，而是一个小的物理模型：
 *   充电功率 → 电池温度 → CPU/GPU 温度 → 风扇转速，
 * 并且适配器功率 = 系统负载 + 充入电池的功率，各数值之间不会互相矛盾。
 *
 * 所有函数都是**时间的纯函数**（不持有状态），这样：
 * - 每秒轮询重新渲染时会自然连续，不需要维护内部游标；
 * - 历史曲线直接用同一组函数在过去的时间点上求值即可，曲线与当前值天然一致。
 *
 * 替换时机：服务端把对应指标接上以后，删除 sources.js 里的条目即可，
 * 本文件只应该被 mock 层引用，真实链路不依赖它。
 */

/** 电池充放电循环参数，与实际阈值（40/70）保持一致，避免示例数据看起来违反配置。 */
const SOC_LOW = 40
const SOC_HIGH = 70
const CYCLE_SECONDS = 3600

/**
 * 平滑伪噪声：几个不可通约的正弦叠加，看起来像噪声但连续可导。
 * 用途只是让数值别像机器人在打字，不追求统计学意义上的随机。
 */
function wobble(t, seed) {
  return (
    Math.sin(t * 0.13 + seed * 1.7) * 0.5 +
    Math.sin(t * 0.37 + seed * 3.1) * 0.3 +
    Math.sin(t * 0.71 + seed * 5.3) * 0.2
  )
}

/** 电量：在 40%~70% 之间做三角波，模拟"降到开始阈值就充、到停止阈值就停"。 */
export function batteryPercentAt(t) {
  const u = ((t / CYCLE_SECONDS) % 1 + 1) % 1
  const triangle = u < 0.5 ? u * 2 : 2 - u * 2
  return SOC_LOW + triangle * (SOC_HIGH - SOC_LOW)
}

/** 是否处于充入阶段（三角波的上升沿）。 */
export function isChargingAt(t) {
  return batteryPercentAt(t + 2) > batteryPercentAt(t - 2)
}

/** 适配器是否接着：慢速方波，约 70% 时间接着。 */
export function isPluggedAt(t) {
  const u = ((t / 2400) % 1 + 1) % 1
  return u < 0.7
}

/** 系统负载功率：平滑波动 + 偶发峰值。 */
export function systemLoadWAt(t) {
  return Math.max(8, 46 + wobble(t, 1) * 24 + Math.sin(t / 90) * 6)
}

/** 电池功率（带符号，正=充入）。停止充电时回落到 0，与真实行为一致。 */
export function batteryPowerWAt(t) {
  const percent = batteryPercentAt(t)
  if (isChargingAt(t) && percent < SOC_HIGH - 0.5) {
    return 42 + wobble(t, 2) * 4
  }
  // 未充电时：接着适配器就基本不动（0 附近），拔电则放电。
  if (isPluggedAt(t)) return wobble(t, 3) * 0.4
  return -(16 + Math.abs(wobble(t, 4)) * 6)
}

/** 适配器输出功率：系统负载 + 充入电池的部分；未接适配器时为 0。 */
export function adapterPowerWAt(t) {
  if (!isPluggedAt(t)) return 0
  return systemLoadWAt(t) + Math.max(0, batteryPowerWAt(t))
}

export function batteryTemperatureCAt(t) {
  const charging = isChargingAt(t) && isPluggedAt(t)
  return 31 + (charging ? 3.2 : 0) + wobble(t, 5) * 1.4
}

/** 温度与负载挂钩：负载越高温度越高，再叠一点漂移。 */
function coreTemperatureAt(t, base, loadFactor, seed) {
  const load = systemLoadWAt(t)
  return base + load * loadFactor + wobble(t, seed) * 2.5
}

export function sensorTemperaturesAt(t) {
  return {
    cpu: coreTemperatureAt(t, 34, 0.42, 6),
    gpu: coreTemperatureAt(t, 30, 0.38, 7),
    ssd: coreTemperatureAt(t, 33, 0.12, 8),
  }
}

/** 风扇转速：按最高核心温度走一条非线性曲线，笔记本只有一组风扇，不区分 CPU/GPU。 */
export function fanRpmAt(t, maxRpm = 6000) {
  const { cpu, gpu } = sensorTemperaturesAt(t)
  const hottest = Math.max(cpu, gpu)
  const low = 45
  const high = 95
  const k = Math.min(1, Math.max(0, (hottest - low) / (high - low)))
  return Math.round(1100 + Math.pow(k, 1.15) * (maxRpm - 1100))
}

/**
 * 生成一整份示例 Telemetry。
 * 只在"服务完全不可用"或用户显式打开"全量示例数据"时使用。
 */
export function simulateTelemetry(tMs = Date.now(), { maxRpm = 6000 } = {}) {
  const t = tMs / 1000
  const temps = sensorTemperaturesAt(t)
  return {
    CheckedAt: new Date(tMs).toISOString(),
    PluggedIn: isPluggedAt(t),
    BatteryPercent: batteryPercentAt(t),
    BatteryTemperatureC: batteryTemperatureCAt(t),
    BatteryPowerW: batteryPowerWAt(t),
    AdapterPowerW: adapterPowerWAt(t),
    // 系统负载交给上层用统一口径派生（见 data/derive.js），这里显式给 null 以免出现第二个口径。
    SystemLoadW: null,
    PerformanceMode: 1,
    ChargeStartPercent: SOC_LOW,
    ChargeStopPercent: SOC_HIGH,
    BatteryHealthPercent: 96,
    BatteryDesignCapacityWh: 83,
    BatteryFullChargeCapacityWh: 79.68,
    BatteryCycleCount: 214,
    Sensors: [
      { Id: 'cpu', Label: 'CPU', TempC: temps.cpu, WarnC: 70, HotC: 85 },
      { Id: 'gpu', Label: 'GPU', TempC: temps.gpu, WarnC: 75, HotC: 88 },
      { Id: 'ssd', Label: 'SSD', TempC: temps.ssd, WarnC: 55, HotC: 70 },
    ],
    Fans: [{ Id: 'fan1', Label: '风扇', Rpm: fanRpmAt(t, maxRpm), MaxRpm: maxRpm }],
    PcManagerOpen: false,
    ChargeError: null,
    ServiceError: null,
  }
}
