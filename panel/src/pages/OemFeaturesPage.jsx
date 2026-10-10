import { InfoBar } from '../components/InfoBar.jsx'
import { SectionCard } from '../components/SectionCard.jsx'
import { SettingRow, SettingValue, ToggleSwitch } from '../components/Controls.jsx'
import { useOemFeatures, useOemControl, useOpenOemPage } from '../data/queries.js'

export function OemFeaturesPage() {
  const query = useOemFeatures()
  const control = useOemControl()
  const openPage = useOpenOemPage()
  const display = query.features?.display
  const audio = query.features?.audio
  const liveAudio = audio?.live
  const error = query.error ?? control.error ?? openPage.error
  const busy = query.isLoading || control.isPending || openPage.isPending
  const changeDisplay = (feature, enabled) => control.mutate({ type: 'display', feature, enabled })
  const changeAudio = (feature, value) => control.mutate({ type: 'audio', feature, value })
  const openingButtons = (page, available) => (
    <button type="button" className="hc-btn" disabled={!available || busy} onClick={() => openPage.mutate(page)}>打开官方页面</button>
  )

  return (
    <>
      <div className="hc-sp12">
        {query.isPreview ? <InfoBar title="浏览器示例">下面的开关与音频读数用于预览，不会操作本机设备。</InfoBar> : null}
        {query.isLoading ? <InfoBar title="正在读取显示与音频组件…" /> : null}
        {error ? <InfoBar tone="critical" icon="warn" title="操作未完成">{error.message}</InfoBar> : null}
        {control.isSuccess ? <InfoBar tone={control.data?.settingsConfirmed ? 'success' : 'accent'} icon="info"
          title={control.data?.settingsConfirmed ? '组件配置已确认' : '请求已提交'}>
          {control.data?.message ?? '请观察实际显示或音频效果。'}
        </InfoBar> : null}
        {openPage.isSuccess ? <InfoBar title="官方页面">{openPage.data?.message ?? '已打开官方程序。'}</InfoBar> : null}
      </div>

      <SectionCard span={7} title="健康显示" icon="sun"
        note="使用本机荣耀 LCD 组件" actions={openingButtons('healthDisplay', display?.healthPageAvailable)}>
        <div className="hc-settings">
          {(display?.features ?? []).filter((item) => item.id !== 'colorManagement').map((item) => (
            <SettingRow key={item.id} title={item.name} desc={!item.controlAvailable ? item.reason : item.enabled == null ? '当前配置未知' : null}>
              {item.id === 'colorTemperature' ? openingButtons('healthDisplay', display?.healthPageAvailable)
                : <ToggleSwitch checked={item.enabled === true} disabled={!item.controlAvailable || busy || Boolean(query.error)}
                  ariaLabel={item.name} onChange={(enabled) => changeDisplay(item.id, enabled)} />}
            </SettingRow>
          ))}
          {!display?.features?.length && !query.isLoading ? <SettingRow title="健康显示"><SettingValue>{query.error ? '暂无法读取组件状态' : '未检测到可用组件'}</SettingValue></SettingRow> : null}
        </div>
        <p className="hc-edit-hint">显示状态来自组件保存的配置；设置确认后请观察实际效果。</p>
      </SectionCard>

      <SectionCard span={5} title="色彩管理" icon="sliders" note="校色文件与显卡色彩管线联动">
        <InfoBar title={display?.colorPageAvailable ? '使用官方校准模式' : '当前设备未开放色彩管理'}>
          {display?.colorPageAvailable ? '色域与校色由官方组件管理。' : '本机支持信息或已安装组件不满足色彩管理要求。'}
        </InfoBar>
        <div className="hc-card-actions">{openingButtons('colorManagement', display?.colorPageAvailable)}</div>
      </SectionCard>

      <SectionCard span={12} title="智慧音频" icon="settings" note="按当前默认音频端点控制"
        actions={openingButtons('smartAudio', audio?.pageAvailable ?? audio?.officialPageAvailable)}>
        <div className="hc-settings">
          <SettingRow title="麦克风场景" desc={liveAudio?.microphoneControlAvailable ? '控制本机 Senary 麦克风效果' : audio?.reason ?? '未检测到已验证的麦克风控制接口'}>
            <select className="hc-windows-power-select" aria-label="麦克风场景" value={liveAudio?.captureMode ?? ''}
              disabled={!liveAudio?.microphoneControlAvailable || busy || Boolean(query.error)}
              onChange={(event) => changeAudio('microphoneScene', { 0: 'bypass', 1: 'multiplayer', 2: 'single' }[event.target.value])}>
              {liveAudio?.captureMode == null ? <option value="">未知</option> : null}
              <option value="0">关闭场景处理</option><option value="1">多人场景</option><option value="2">单人场景</option>
            </select>
          </SettingRow>
          <SettingRow title="通话对端降噪" desc={liveAudio?.callNoiseControlAvailable ? null : audio?.reason ?? '当前播放端点不支持该效果'}>
            <ToggleSwitch checked={liveAudio?.renderStatus === 1} disabled={!liveAudio?.callNoiseControlAvailable || busy || Boolean(query.error)}
              ariaLabel="通话对端降噪" onChange={(enabled) => changeAudio('callNoiseReduction', enabled ? 'on' : 'off')} />
          </SettingRow>
          <SettingRow title="读数来源"><SettingValue>{liveAudio?.stateSource ?? '暂无实时读数'}</SettingValue></SettingRow>
        </div>
        <p className="hc-edit-hint">音效场景、声纹与空间音效等其他功能可在官方智慧音频页面设置。</p>
      </SectionCard>
    </>
  )
}
