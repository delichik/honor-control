using System.Diagnostics;
using HonorControl.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service;

/// <summary>
/// 按托盘策略拉起/停放托盘进程。
///
/// 为什么服务不能直接创建托盘进程：服务在会话 0 里跑，而托盘必须活在用户会话里
/// （托盘图标只有同会话的进程能注册）。所以这里用**登录计划任务**作为"跳到用户会话"的手段：
/// 安装器注册一个交互式登录任务，服务只负责启用/禁用它、并在需要时立即触发一次。
///
/// 三种策略（策略由面板写入、服务持久化，见 ConfigurationStore）：
/// - Off      ：禁用任务；已在运行的托盘进程会自己读到策略并退出，服务不需要去杀它；
/// - OnDemand ：禁用任务（登录不自动出现），需要时由面板直接拉起；
/// - Always   ：启用任务（登录即出现）；并且发现托盘没在跑时立即触发一次，不需要等下一次登录。
///
/// 待真机验证：`schtasks /run` 由 SYSTEM 触发一个"仅在用户登录时运行"的任务，是否稳定地
/// 出现在该用户的会话里。这条路径在开发机上无法验证（服务未安装），失败时会记日志并等下一轮。
/// </summary>
internal sealed class TrayPolicyHost : BackgroundService
{
    /// <summary>安装器注册的任务名，两边必须一致。</summary>
    public const string TaskName = "HonorControlTrayHost";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ConfigurationStore configuration;
    private readonly ILogger<TrayPolicyHost> logger;
    private TrayPolicyMode? applied;

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
            // Always 才需要任务在登录时触发；其余两种情况禁用，避免"策略说不要托盘，登录却又冒出来"。
            bool enable = policy == TrayPolicyMode.Always;
            if (RunSchtasks("/change", $"/tn \"{TaskName}\" {(enable ? "/enable" : "/disable")}"))
            {
                logger.LogInformation("托盘策略切换为 {Policy}（登录任务已{Action}）", policy, enable ? "启用" : "禁用");
            }
            applied = policy;
        }

        if (policy != TrayPolicyMode.Always) return;

        // Always：托盘不在就立刻补一个，而不是等用户下次登录。
        if (IsTrayRunning()) return;
        if (RunSchtasks("/run", $"/tn \"{TaskName}\""))
        {
            logger.LogInformation("已触发托盘任务。");
        }
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
            // 枚举失败时按"没在跑"处理：多触发一次任务是无害的（托盘自己有单实例互斥量）。
            return false;
        }
    }

    private bool RunSchtasks(string verb, string arguments)
    {
        try
        {
            string executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

            using Process? process = Process.Start(new ProcessStartInfo(executable, $"{verb} {arguments}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process == null) return false;
            process.WaitForExit(10_000);
            if (process.ExitCode == 0) return true;

            logger.LogWarning("schtasks {Verb} {Arguments} 退出码 {Code}：{Error}",
                verb, arguments, process.ExitCode, process.StandardError.ReadToEnd().Trim());
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "调用 schtasks 失败");
            return false;
        }
    }
}
