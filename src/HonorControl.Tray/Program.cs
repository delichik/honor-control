using System.Security.Principal;
using HonorControl.Contracts;

namespace HonorControl.Tray;

/// <summary>
/// 托盘进程入口。
///
/// 生命周期（与可行性文档 4.1 / 4.10 一致）：
/// - 每个交互式会话一份，只在配置拥有者的会话里运行（非拥有者登录不会出现图标）；
/// - 由服务（策略 Always）或面板（策略 OnDemand，用户到场）拉起，重复拉起由单实例吸收；
/// - 启动后向服务读取策略：读到 Off 就退出；
/// - 菜单"退出"= 请求**服务自己停止**，然后托盘退出——托盘与服务是成对的；
/// - 连不上服务时退避重试，超过约两分钟后退出，不留一个没有服务的空转托盘。
///
/// 它只读服务：不写配置、不做快捷开关，因此归属语义不会因为托盘自启而改变。
/// </summary>
internal static class Program
{
    private const int PollIntervalMs = 5000;
    private const int MaxConsecutiveFailures = 24; // 5 秒 × 24 ≈ 2 分钟

    [STAThread]
    private static int Main()
    {
        // 常驻进程崩了没人看得见（没有控制台、没有窗口），所以任何未处理异常都要落进日志。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            TrayLog.Write("未处理异常：" + args.ExceptionObject);

        try
        {
            return Run();
        }
        catch (Exception exception)
        {
            TrayLog.Write("托盘异常退出：" + exception);
            return 2;
        }
    }

    private static int Run()
    {
        TrayLog.Write("托盘进程启动。");

        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        string instanceName = $@"Local\HonorControl.Tray.{sid}";

        using EventWaitHandle activation = new(false, EventResetMode.AutoReset, instanceName + ".Open");
        using Mutex mutex = new(false, instanceName, out bool firstInstance);
        if (!firstInstance)
        {
            // 第二个实例：只负责把已有托盘提醒一下，然后立刻退出（不会出现两个图标）。
            TrayLog.Write("已有托盘实例在运行，本次启动直接退出。");
            activation.Set();
            return 0;
        }

        using TrayWindow window = new();
        ServiceLink link = new();
        int failures = 0;
        bool exiting = false;

        window.OpenRequested += PanelLauncher.OpenOrActivate;

        window.ExitRequested += () =>
        {
            if (exiting || !window.ConfirmExit()) return;
            exiting = true;
            if (!link.TryShutdownService(out string? error))
            {
                // 服务已经不在（或拒绝）时不必纠缠：托盘退出即可，后台本来就没在跑。
                window.ShowNotification("未能停止后台服务", error ?? "服务未响应。");
            }
            window.Dispose();
        };

        window.PollRequested += () =>
        {
            if (exiting) return;
            Poll(window, link, ref failures);
            if (failures >= MaxConsecutiveFailures)
            {
                // 服务长时间不可用：退出，而不是留一个什么都做不了的图标。
                TrayLog.Write($"服务连续 {failures} 次不可用，托盘退出。最后错误：{link.LastError}");
                exiting = true;
                window.Dispose();
            }
        };

        Poll(window, link, ref failures);
        if (failures >= MaxConsecutiveFailures)
        {
            TrayLog.Write("服务不可用，托盘退出。");
            return 1;
        }

        window.ShowNotification("Honor Control 已在后台运行",
            "左键打开控制面板；右键菜单可以退出（会同时停止后台服务）。");
        window.StartPolling(PollIntervalMs);
        window.RunMessageLoop();
        return 0;
    }

    private static void Poll(TrayWindow window, ServiceLink link, ref int failures)
    {
        if (!link.TryGetSnapshot(out ServiceSnapshot? snapshot) || snapshot == null)
        {
            failures++;
            string message = link.LastError ?? "服务未响应。";
            if (failures == 1) TrayLog.Write($"连接服务失败：{message}");
            window.StatusLine = failures == 1 ? "服务未连接" : $"服务未连接（{failures}/{MaxConsecutiveFailures}）";
            window.SetTooltip($"Honor Control — {message}");
            return;
        }

        if (failures != 0) TrayLog.Write("已连接到服务。");
        failures = 0;

        // 策略由服务持有，托盘只读。Off 表示"不要托盘"，那就自己退出。
        if (snapshot.TrayPolicy == TrayPolicyMode.Off)
        {
            TrayLog.Write("策略为 Off，托盘退出。");
            window.Dispose();
            return;
        }

        string state = DescribeSnapshot(snapshot);
        if (link.TryGetTelemetry(out Contracts.Telemetry? telemetry) && telemetry != null)
        {
            state = DescribeTelemetry(telemetry, snapshot);
        }

        window.StatusLine = state;
        window.SetTooltip($"Honor Control — {state}");
    }

    private static string DescribeSnapshot(ServiceSnapshot snapshot)
    {
        ActualState actual = snapshot.Actual;
        string mode = actual.PerformanceMode switch
        {
            2 => "高能模式",
            1 => "智能模式",
            _ => "模式未知",
        };
        string charge = actual.ChargeStart.HasValue && actual.ChargeEnd.HasValue
            ? $"阈值 {actual.ChargeStart}%–{actual.ChargeEnd}%"
            : "阈值未知";
        return $"{charge} · {mode}";
    }

    private static string DescribeTelemetry(Contracts.Telemetry telemetry, ServiceSnapshot snapshot)
    {
        string percent = $"{Math.Round(telemetry.BatteryPercent)}%";
        string power;
        if (telemetry.BatteryPowerW is double watts && Math.Abs(watts) > 0.6)
        {
            power = watts > 0 ? $"充电 {watts:F1} W" : $"放电 {-watts:F1} W";
        }
        else
        {
            power = telemetry.PluggedIn ? "已接电源" : "电池供电";
        }

        if (snapshot.Actual.PcManagerOpen) return $"{percent} · {power} · 电脑管家占用（只读）";
        if (snapshot.Actual.ChargeError != null) return $"{percent} · {power} · 校正失败";
        return $"{percent} · {power}";
    }
}
