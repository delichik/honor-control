const displayFeatures = [
  ['eyeProtection', '护眼模式'], ['ebook', '电子书模式'], ['comfortDisplay', '舒适显示'],
  ['defocusEye', '离焦视力舒缓'], ['naturalLight', '类自然光护眼'],
].map(([id, name]) => ({ id, name, supported: true, enabled: false, controlAvailable: true,
  reason: null, stateSource: '浏览器示例配置', effectsVerified: false }))
const data = {
  display: { features: displayFeatures, healthPageAvailable: false, colorPageAvailable: false },
  audio: { live: { captureAbility: 8195, captureMode: 1, renderAbility: 16384, renderStatus: 1,
    microphoneControlAvailable: true, callNoiseControlAvailable: true,
    stateSource: '浏览器示例状态', effectsVerified: false }, reason: null },
}
export async function mockOemRequest(command, args) {
  await new Promise((resolve) => setTimeout(resolve, 80))
  if (command === 'get_oem_features') return structuredClone(data)
  if (command === 'open_oem_page') throw new Error('浏览器预览无法打开本机荣耀程序。')
  if (command === 'oem_set_display') {
    const target = displayFeatures.find((item) => item.id === args.feature)
    if (!target) throw new Error('未知显示功能。')
    target.enabled = args.enabled
    return { submitted: true, settingsConfirmed: true, effectsVerified: false, message: '示例配置已更新。' }
  }
  if (args.feature === 'microphoneScene') data.audio.live.captureMode = { bypass: 0, multiplayer: 1, single: 2 }[args.value]
  else if (args.feature === 'callNoiseReduction') data.audio.live.renderStatus = args.value === 'on' ? 1 : 0
  else throw new Error('未知音频功能。')
  return { settingsConfirmed: true, effectsVerified: false, message: '示例音频状态已更新。' }
}
