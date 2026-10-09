import { Spinner } from '@fluentui/react-components'
import { Icon } from './Icon.jsx'
import { InfoBar } from './InfoBar.jsx'
import { useStartService } from '../data/queries.js'

/**
 * 服务状态横幅 —— 设计稿的 InfoBar。
 *
 * 只报告**服务的客观状态**，不做任何"自动修复"以外的花活：
 * - 连不上：提供启动入口；
 * - 连上了但有异常（电脑管家占用、写入失败）：照实转述服务返回的错误串。
 *
 * 设计稿的规则：正常充放电状态**不**出文字，只有异常才出现这一条。
 * 启停服务由 SCM 执行，并由 Windows 显示 UAC。
 */
export function StatusBanner({ serviceReachable, error, snapshot, loading, fullMock, forceMock }) {
  const startService = useStartService()

  if (loading) {
    return (
      <div className="hc-infobar" data-tone="accent">
        <Spinner size="tiny" />
        <div className="hc-infobar-body">
          <div className="hc-infobar-title">正在连接服务…</div>
        </div>
      </div>
    )
  }

  if (!serviceReachable) {
    return (
      <InfoBar
        tone="critical"
        icon="warn"
        title="服务未连接"
        actions={
          <button type="button" className="hc-btn hc-btn--accent" disabled={startService.isPending} onClick={() => startService.mutate()}>
            <Icon name="refresh" />
            {startService.isPending ? '正在启动…' : '启动服务'}
          </button>
        }
      >
        {error?.message ?? '服务可能未安装或未启动。'}
        {fullMock ? ' 下面显示的数值全部是示例数据，仅用于预览界面。' : null}
      </InfoBar>
    )
  }

  const messages = []

  if (snapshot?.Actual?.PcManagerOpen) {
    messages.push(
      <InfoBar key="pcmanager" tone="caution" icon="info" title="荣耀电脑管家正在运行">
        服务此刻只读：等电脑管家关闭后才会继续执行待处理的配置。
      </InfoBar>,
    )
  }

  if (snapshot?.Actual?.ChargeError) {
    messages.push(
      <InfoBar key="charge" tone="caution" icon="warn" title="充电阈值">
        {snapshot.Actual.ChargeError}
      </InfoBar>,
    )
  }

  if (snapshot?.Actual?.PerformanceError) {
    messages.push(
      <InfoBar key="performance" tone="caution" icon="warn" title="性能模式">
        {snapshot.Actual.PerformanceError}
      </InfoBar>,
    )
  }

  if (snapshot?.Actual?.ServiceError) {
    messages.push(
      <InfoBar key="service" tone="accent" icon="info" title="服务状态">
        {snapshot.Actual.ServiceError}
      </InfoBar>,
    )
  }

  if (fullMock || forceMock) {
    messages.push(
      <InfoBar key="mock" tone="accent" icon="info" title="已打开全量示例数据">
        界面上的所有读数都来自模拟模型，不会反映真实硬件状态。
      </InfoBar>,
    )
  }

  if (startService.isError) {
    messages.push(
      <InfoBar key="starterror" tone="critical" icon="warn" title="启动服务失败">
        {startService.error?.message}
      </InfoBar>,
    )
  }

  if (messages.length === 0) return null
  return <div className="hc-infobar-stack">{messages}</div>
}
