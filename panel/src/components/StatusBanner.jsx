import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  makeStyles,
} from '@fluentui/react-components'
import { ArrowSync24Regular, Warning24Regular } from '@fluentui/react-icons'
import { useStartService } from '../data/queries.js'

const useStyles = makeStyles({
  stack: { display: 'flex', flexDirection: 'column', gap: '8px' },
  spinner: { display: 'flex', alignItems: 'center', gap: '8px' },
})

/**
 * 服务状态横幅。
 *
 * 这里只报告**服务的客观状态**，不做任何"自动修复"以外的花活：
 * - 连不上：说明原因（服务未安装/未启动/权限不足），并提供启动入口；
 * - 连上了但有异常（电脑管家占用、写入失败）：照实转述服务返回的错误串。
 *
 * 面板是中完整性进程，启动服务靠安装器授予的 SERVICE_START 权限，不会弹 UAC。
 */
export function StatusBanner({ serviceReachable, error, snapshot, loading, fullMock, forceMock }) {
  const styles = useStyles()
  const startService = useStartService()

  if (loading) {
    return (
      <div className={styles.spinner}>
        <Spinner size="tiny" />
        <span>正在连接 Honor Control 服务…</span>
      </div>
    )
  }

  if (!serviceReachable) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>
          <MessageBarTitle>未连接到 Honor Control 服务</MessageBarTitle>
          {error?.message ?? '服务可能未安装或未启动。'}
          {fullMock ? ' 下面显示的数值全部是示例数据，仅用于预览界面。' : null}
        </MessageBarBody>
        <MessageBarActions
          containerAction={
            <Button
              appearance="primary"
              icon={startService.isPending ? <Spinner size="tiny" /> : <ArrowSync24Regular />}
              disabled={startService.isPending}
              onClick={() => startService.mutate()}
            >
              启动服务
            </Button>
          }
        />
      </MessageBar>
    )
  }

  const messages = []

  if (snapshot?.Actual?.PcManagerOpen) {
    messages.push(
      <MessageBar key="pcmanager" intent="warning">
        <MessageBarBody>
          <MessageBarTitle>荣耀电脑管家正在运行</MessageBarTitle>
          服务此刻只读：等电脑管家关闭后才会继续执行待处理的配置。
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (snapshot?.Actual?.ChargeError) {
    messages.push(
      <MessageBar key="charge" intent="warning">
        <MessageBarBody>
          <MessageBarTitle>充电阈值</MessageBarTitle>
          {snapshot.Actual.ChargeError}
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (snapshot?.Actual?.PerformanceError) {
    messages.push(
      <MessageBar key="performance" intent="warning">
        <MessageBarBody>
          <MessageBarTitle>性能模式</MessageBarTitle>
          {snapshot.Actual.PerformanceError}
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (snapshot?.Actual?.ServiceError) {
    messages.push(
      <MessageBar key="service" intent="info">
        <MessageBarBody>
          <MessageBarTitle>服务状态</MessageBarTitle>
          {snapshot.Actual.ServiceError}
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (forceMock) {
    messages.push(
      <MessageBar key="mock" intent="info" icon={<Warning24Regular />}>
        <MessageBarBody>
          <MessageBarTitle>已打开全量示例数据</MessageBarTitle>
          界面上的所有读数都来自模拟模型，不会反映真实硬件状态。
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (startService.isError) {
    messages.push(
      <MessageBar key="starterror" intent="error">
        <MessageBarBody>
          <MessageBarTitle>启动服务失败</MessageBarTitle>
          {startService.error?.message}
        </MessageBarBody>
      </MessageBar>,
    )
  }

  if (messages.length === 0) return null
  return <div className={styles.stack}>{messages}</div>
}
