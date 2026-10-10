import { useState } from 'react'
import { InfoBar } from '../components/InfoBar.jsx'
import { SectionCard } from '../components/SectionCard.jsx'
import { SettingRow } from '../components/Controls.jsx'
import {
  useWindowsPower, useSetWindowsPower, useRestoreWindowsPower,
  useServiceSnapshot, useSetAutoReconcile,
} from '../data/queries.js'
import { isTauriRuntime } from '../data/transport.js'
import '../styles/pages-windows-power.css'

const TIMEOUTS = [0, 60, 120, 180, 300, 600, 900, 1200, 1800, 3600, 7200, 14400, 28800, 86400]
const INTEL_PLANS = [{ value: 0, label: '省电' }, { value: 1, label: '平衡' }, { value: 2, label: '性能' }]
const EMPTY_DRAFT = { schemeId: null, fields: {}, originals: {} }
const secondsLabel = (value) => value === 0 ? '永不' : value % 3600 === 0 ? `${value / 3600} 小时`
  : value % 60 === 0 ? `${value / 60} 分钟` : `${value} 秒`

export function WindowsPowerSettingsPage() {
  const { settings, serviceReachable, error, isLoading } = useWindowsPower()
  const { snapshot, serviceReachable: snapshotReachable } = useServiceSnapshot()
  const setPower = useSetWindowsPower()
  const restorePower = useRestoreWindowsPower()
  const setAuto = useSetAutoReconcile()
  const [draft, setDraft] = useState(EMPTY_DRAFT)
  const [schemeDraft, setSchemeDraft] = useState(null)
  const [feedback, setFeedback] = useState(null)
  const [localError, setLocalError] = useState(null)
  const preview = !isTauriRuntime()
  const activeId = settings?.ActiveSchemeId ?? null
  const plans = settings?.Schemes ?? []
  const activeName = plans.find((plan) => plan.Id === activeId)?.Name ?? (activeId ? '名称不可读' : '未知')
  const dirtyKeys = Object.keys(draft.fields)
  const dirty = dirtyKeys.length > 0
  const draftStale = dirty && draft.schemeId !== activeId
  const schemeStale = schemeDraft && schemeDraft.expected !== activeId
  const busy = setPower.isPending || restorePower.isPending || setAuto.isPending
  const blocked = busy || !serviceReachable || Boolean(error) || !activeId
  const autoMaintainsPerformance = snapshot?.Desired?.AutoReconcile === true && snapshot?.Desired?.PerformanceMode != null
  const pendingPerformance = snapshot?.Actual?.PerformancePending === true || snapshot?.Actual?.PendingPerformanceMode != null
  const schemeBlocked = blocked || !snapshotReachable || autoMaintainsPerformance || pendingPerformance
  const operationError = localError ?? setPower.error?.message ?? restorePower.error?.message ?? setAuto.error?.message
  const feedbackVerified = feedback && settings && feedback.schemeId === activeId
    && Object.entries(feedback.values).every(([key, value]) => settings[key] === value)

  const clearResult = () => {
    setFeedback(null)
    setLocalError(null)
    setPower.reset()
    restorePower.reset()
    setAuto.reset()
  }

  const edit = (key, value) => {
    clearResult()
    setDraft((current) => {
      const original = Object.hasOwn(current.originals, key) ? current.originals[key] : settings?.[key]
      const fields = { ...current.fields, [key]: value }
      const originals = { ...current.originals, [key]: original }
      if (value === original) { delete fields[key]; delete originals[key] }
      return Object.keys(fields).length ? { schemeId: current.schemeId ?? activeId, fields, originals } : EMPTY_DRAFT
    })
  }

  const applySettings = () => {
    if (blocked || !dirty || draftStale) return
    clearResult()
    const fields = { ...draft.fields }
    const expected = draft.schemeId
    setPower.mutate({ ...fields, ExpectedActiveSchemeId: expected }, {
      onSuccess: (result) => {
        if (!result || result.ActiveSchemeId !== expected || Object.entries(fields).some(([key, value]) => result[key] !== value)) {
          setLocalError('服务回读与提交的设置不一致，尚不能确认生效；草稿已保留。')
          return
        }
        setDraft(EMPTY_DRAFT)
        setFeedback({ schemeId: result.ActiveSchemeId, values: fields, message: '所修改的设置已由服务回读验证。' })
      },
    })
  }

  const applyScheme = () => {
    if (schemeBlocked || !schemeDraft || schemeStale || dirty) return
    clearResult()
    const selection = { ...schemeDraft }
    setPower.mutate({ SchemeId: selection.id, ExpectedActiveSchemeId: selection.expected }, {
      onSuccess: (result) => {
        if (!result || result.ActiveSchemeId !== selection.id) {
          setLocalError('服务未回读到所选方案，尚不能确认切换生效。')
          return
        }
        setSchemeDraft(null)
        setFeedback({ schemeId: result.ActiveSchemeId, values: {}, message: '所选电源方案已由服务回读验证。' })
      },
    })
  }

  const restore = () => {
    if (schemeBlocked || !settings?.CanRestore || dirty || schemeDraft) return
    clearResult()
    restorePower.mutate(undefined, {
      onSuccess: (result) => {
        if (!result?.ActiveSchemeId || result.CanRestore !== false) {
          setLocalError('服务未确认恢复完成，恢复备份仍可能保留，请刷新状态。')
          return
        }
        setFeedback({ schemeId: result.ActiveSchemeId, values: {}, message: '已恢复备份中的原值，服务已完成回读验证。' })
      },
    })
  }

  const fieldControl = (key, label, intel = false) => {
    const actual = settings?.[key]
    const available = Number.isInteger(actual) && (!intel || actual >= 0 && actual <= 2)
    const value = draft.fields[key] ?? actual ?? ''
    const reason = available ? null : settings?.MissingReason?.[key] ?? '当前设备或方案无法读取此设置。'
    const options = intel ? INTEL_PLANS : [...new Set([...TIMEOUTS, ...(available ? [actual] : []), ...(Number.isInteger(value) ? [value] : [])])]
      .sort((a, b) => a - b).map((seconds) => ({ value: seconds, label: secondsLabel(seconds) }))
    return (
      <SettingRow key={key} title={label} desc={reason ?? (Object.hasOwn(draft.fields, key) ? '待应用' : undefined)}>
        <select
          className="hc-windows-power-select"
          aria-label={label}
          value={available ? value : ''}
          disabled={blocked || !available || draftStale || Boolean(schemeDraft)}
          onChange={(event) => edit(key, Number(event.target.value))}
        >
          {!available ? <option value="">不可用</option> : null}
          {options.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
        </select>
      </SettingRow>
    )
  }

  return (
    <>
      <div className="hc-sp12 hc-windows-power-messages">
        {preview ? <InfoBar title="浏览器示例预览">当前设置与操作结果来自模拟服务，不会改变这台电脑的电源设置。</InfoBar> : null}
        {isLoading ? <InfoBar title="正在读取 Windows 电源设置…" /> : null}
        {!serviceReachable || error ? <InfoBar tone="critical" icon="warn" title="Windows 电源设置不可用">{error?.message ?? '服务未连接，暂不能应用或恢复设置。'}</InfoBar> : null}
        {settings?.MissingReason?.ActiveSchemeId ? <InfoBar tone="caution" title="当前方案不可读">{settings.MissingReason.ActiveSchemeId}</InfoBar> : null}
        {operationError ? <InfoBar tone="critical" icon="warn" title="操作未完成">{operationError}</InfoBar> : null}
        {feedbackVerified ? <InfoBar tone="success" icon="check-circle" title={preview ? '示例已更新' : '已生效'}>{feedback.message}</InfoBar> : null}
        {draftStale || schemeStale ? <InfoBar tone="caution" title="当前电源方案已改变">已保留编辑草稿。请放弃草稿并重新读取当前方案，避免把旧方案的设置写到新方案。</InfoBar> : null}
      </div>

      <SectionCard span={12} icon="settings" title="Windows 电源方案" note={`当前：${activeName}`} className="hc-windows-power-card">
        <div className="hc-settings">
          <SettingRow title="选择已有方案" desc="方案切换与下方设置分别应用；荣耀智能／高能模式在性能设置中调整。">
            <select
              className="hc-windows-power-select hc-windows-power-plan"
              aria-label="选择 Windows 电源方案"
              value={schemeDraft?.id ?? activeId ?? ''}
              disabled={schemeBlocked || dirty || plans.length === 0}
              onChange={(event) => {
                clearResult()
                setSchemeDraft(event.target.value === activeId ? null : { id: event.target.value, expected: schemeDraft?.expected ?? activeId })
              }}
            >
              {!activeId ? <option value="">当前方案未知</option> : null}
              {activeId && !plans.some((plan) => plan.Id === activeId) ? <option value={activeId}>{activeName}</option> : null}
              {schemeDraft && !plans.some((plan) => plan.Id === schemeDraft.id) ? <option value={schemeDraft.id}>原先选择的方案（当前不可读）</option> : null}
              {plans.map((plan) => <option key={plan.Id} value={plan.Id}>{plan.Name}</option>)}
            </select>
          </SettingRow>
        </div>
        <div className="hc-card-actions hc-windows-power-actions">
          <button type="button" className="hc-btn hc-btn--accent" disabled={schemeBlocked || !schemeDraft || schemeStale || dirty} onClick={applyScheme}>
            {setPower.isPending && schemeDraft ? '切换中…' : '应用方案'}
          </button>
          {schemeDraft ? <button type="button" className="hc-btn" disabled={busy} onClick={() => { clearResult(); setSchemeDraft(null) }}>放弃方案选择</button> : null}
        </div>
        {settings?.MissingReason?.Schemes ? <InfoBar tone="caution" title="方案列表不可用">{settings.MissingReason.Schemes}</InfoBar> : null}
        {autoMaintainsPerformance || pendingPerformance ? (
          <InfoBar tone="caution" title={autoMaintainsPerformance ? '性能自动维护正在同步方案' : '性能切换尚未完成'}
            actions={snapshot?.Desired?.AutoReconcile ? (
              <button type="button" className="hc-btn" disabled={blocked || !snapshotReachable} onClick={() => { clearResult(); setAuto.mutate({ AutoReconcile: false }) }}>
                {setAuto.isPending ? '关闭中…' : '关闭自动维护'}
              </button>
            ) : undefined}>
            {autoMaintainsPerformance ? '选择或恢复方案前需关闭自动维护；这会同时停止充电阈值和性能模式的自动校正。' : '请等待性能模式完成回读后再选择或恢复方案。'}
          </InfoBar>
        ) : null}
      </SectionCard>

      {[{ ac: true, title: '接通电源（AC）', icon: 'plug' }, { ac: false, title: '使用电池（DC）', icon: 'battery' }].map(({ ac, title, icon }) => {
        const prefix = ac ? 'Ac' : 'Dc'
        const intelAvailable = Number.isInteger(settings?.AcIntelGraphicsPowerPlan) || Number.isInteger(settings?.DcIntelGraphicsPowerPlan)
        return (
          <SectionCard key={prefix} span={6} icon={icon} title={title} note="编辑当前方案" className="hc-windows-power-card">
            <div className="hc-settings">
              {fieldControl(`${prefix}DisplayTimeoutSeconds`, `${title}关闭屏幕`)}
              {fieldControl(`${prefix}SleepTimeoutSeconds`, `${title}进入睡眠`)}
              {fieldControl(`${prefix}DiskTimeoutSeconds`, `${title}硬盘空闲后关闭`)}
              {intelAvailable ? fieldControl(`${prefix}IntelGraphicsPowerPlan`, `${title}Intel 显卡策略`, true) : null}
            </div>
            {!intelAvailable ? <p className="hc-edit-hint">Intel 显卡策略不可用：{settings?.MissingReason?.[`${prefix}IntelGraphicsPowerPlan`] ?? '当前设备或方案无法读取此设置。'}</p> : null}
          </SectionCard>
        )
      })}

      <SectionCard span={12} icon="sliders" title="应用与恢复" note={dirty ? `${dirtyKeys.length} 项待应用` : '没有待应用的修改'} className="hc-windows-power-card">
        <p className="hc-edit-hint">应用会保存可恢复的原值，并仅修改编辑过的项目。恢复会撤销本工具保存的电源设置及方案修改；检测到其他程序修改时会停止恢复。</p>
        <div className="hc-card-actions hc-windows-power-actions">
          <button type="button" className="hc-btn hc-btn--accent" disabled={blocked || !dirty || draftStale || Boolean(schemeDraft)} onClick={applySettings}>
            {setPower.isPending && dirty ? '应用中…' : '应用修改'}
          </button>
          <button type="button" className="hc-btn" disabled={busy || !dirty} onClick={() => { clearResult(); setDraft(EMPTY_DRAFT) }}>放弃设置草稿</button>
          <button type="button" className="hc-btn" disabled={schemeBlocked || !settings?.CanRestore || dirty || Boolean(schemeDraft)} onClick={restore}>
            {restorePower.isPending ? '恢复中…' : '恢复备份中的原值'}
          </button>
        </div>
        {settings?.MissingReason?.CanRestore ? <InfoBar tone="caution" title="恢复备份不可读">{settings.MissingReason.CanRestore}</InfoBar> : null}
        {!settings?.CanRestore && !settings?.MissingReason?.CanRestore ? <p className="hc-edit-hint">尚无可恢复的修改备份。</p> : null}
      </SectionCard>
    </>
  )
}
