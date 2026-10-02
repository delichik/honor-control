/**
 * 机型档案（示例）。
 *
 * ⚠️ 全是示例值：功耗墙、噪声、适配器额定功率、设计容量这类数据在 Windows 上没有通用 API，
 * 需要按机型维护一张表（可行性文档 10.7）。在表落地之前，这里提供一份最小的示例档案，
 * 让界面有东西可展示，并让"值从哪来"这件事集中在一个文件里。
 *
 * 服务端接上机型识别后，把这张表搬到 Capabilities 里返回即可。
 */

const PROFILES = {
  '1': { label: '智能模式', pl1W: 45, pl2W: 55, tdpW: 80, noiseDb: 36 },
  '2': { label: '高能模式', pl1W: 65, pl2W: 100, tdpW: 130, noiseDb: 44 },
}

export function exampleModeProfiles(mode) {
  return PROFILES[String(mode)] ?? PROFILES['1']
}

/** 适配器额定功率（示例）：真机上应从机型表或安装时的配置读取。 */
export const EXAMPLE_ADAPTER_RATED_W = 180
