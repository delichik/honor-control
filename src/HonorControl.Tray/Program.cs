using System.Security.Principal;
using HonorControl.Contracts;

namespace HonorControl.Tray;

/// <summary>
/// 托盘进程入口。
///
/// 生命周期：面板单独拉起托盘；托盘退出不会停止服务，服务离线时托盘仍可打开面板。
///
/// 它只读服务：不写配置、不做快捷开关，因此归属语义不会因为托盘自启而改变。
/// </summary>
internal static class Program
{
    private const int PollIntervalMs = 5000;
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
            if (exiting) return;
            exiting = true;
            window.Dispose();
        };

        window.PollRequested += () =>
        {
            if (exiting) return;
            Poll(window, link, ref failures);
        };

        Poll(window, link, ref failures);
        window.ShowNotification("Honor Control", "左键打开面板；右键退出托盘。");
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
            window.StatusLine = "服务未连接";
            window.SetTooltip("Honor Control — 服务未连接");
            return;
        }

        if (failures != 0) TrayLog.Write("已连接到服务。");
        failures = 0;

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
        string percent = telemetry.BatteryPercent is double batteryPercent
            ? $"{Math.Round(batteryPercent)}%"
            : "电量未知";
        string power;
        if (telemetry.BatteryPowerW is double watts && Math.Abs(watts) > 0.6)
        {
            power = watts > 0 ? $"充电 {watts:F1} W" : $"放电 {-watts:F1} W";
        }
        else
        {
            power = telemetry.PluggedIn is bool pluggedIn
                ? pluggedIn ? "已接电源" : "电池供电"
                : "供电状态未知";
        }

        if (snapshot.Actual.PcManagerOpen) return $"{percent} · {power} · 电脑管家占用（只读）";
        if (snapshot.Actual.ChargeError != null) return $"{percent} · {power} · 校正失败";
        return $"{percent} · {power}";
    }
}
