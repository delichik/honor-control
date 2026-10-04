using System.Diagnostics;
using HonorControl.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service;

/// <summary>
/// 按托盘策略决定托盘的存活与拉起。
///
/// 三种策略（策略由面板写入、服务持久化，见 ConfigurationStore）：
/// - Off      ：不拉起。已在运行的托盘会自己读到策略并退出，服务不需要去杀它；
/// - OnDemand ：不主动拉起。用户打开面板时由面板拉起（默认策略）；
/// - Always   ：登录后常驻。服务发现托盘不在配置拥有者的会话里时立刻补一个。
///
/// 拉起手段是进程内调用 <see cref="TrayLauncher"/>（复制用户 shell 令牌 + CreateProcessAsUser），
/// 不依赖计划任务、也不 spawn 任何外部工具——安装流程已经全面去脚本化，运行期同样不留这类行为。
/// </summary>
internal sealed class TrayPolicyHost : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ConfigurationStore configuration;
    private readonly ILogger<TrayPolicyHost> logger;
    private TrayPolicyMode? applied;
    private int consecutiveFailures;

    public TrayPolicyHost(ConfigurationStore configuration, ILogger<TrayPolicyHost> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Synchronize();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "同步托盘策略失败");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Synchronize()
    {
        TrayPolicyMode policy = configuration.LoadTrayPolicy();

        if (applied != policy)
        {
            logger.LogInformation("托盘策略：{Policy}", policy);
            applied = policy;
            consecutiveFailures = 0;
        }

        if (policy != TrayPolicyMode.Always) return;
        if (IsTrayRunning()) return;

        // 失败时不要每 15 秒刷一条同样的日志：连续失败 4 次（约 1 分钟）才再报一次。
        if (consecutiveFailures > 0 && consecutiveFailures % 4 != 0)
        {
            consecutiveFailures++;
            return;
        }

        if (TrayLauncher.TryLaunch(configuration.LoadOwnerSid(), ResolveTrayPath(), out string? error))
        {
            logger.LogInformation("已按 Always 策略启动托盘进程。");
            consecutiveFailures = 0;
        }
        else
        {
            consecutiveFailures++;
            logger.LogWarning("启动托盘进程失败：{Error}", error);
        }
    }

    /// <summary>
    /// 托盘程序的位置：与服务工作在同一个安装根下（{app}\service\ 与 {app}\tray\）。
    /// 服务自己所在目录是 {app}\service，所以往上一级再进 tray。
    /// </summary>
    private static string ResolveTrayPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "tray", "HonorControl.Tray.exe")),
            Path.Combine(baseDirectory, "HonorControl.Tray.exe"),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return candidates[0];
    }

    private static bool IsTrayRunning()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName("HonorControl.Tray");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        catch
        {
            // 枚举失败按"没在跑"处理：多触发一次是无害的（托盘自己有单实例互斥量）。
            return false;
        }
    }
}
