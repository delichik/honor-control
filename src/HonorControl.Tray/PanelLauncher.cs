using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HonorControl.Tray;

/// <summary>
/// 打开控制面板。
///
/// 托盘与面板是两个进程：面板是 Tauri 应用，托盘只负责"确保它被打开并且在前台"。
/// 这里没有依赖 Tauri 的单实例插件，而是用窗口查找来做激活——这样面板侧不需要任何配合代码，
/// 也避免了两边各自维护一套互斥量的不一致。
/// </summary>
internal static class PanelLauncher
{
    private const int SwRestore = 9;
    private const int SwShow = 5;

    public static void OpenOrActivate()
    {
        string? executable = ResolvePanelExecutable();
        if (executable == null)
        {
            MessageBoxW(IntPtr.Zero,
                "找不到控制面板程序。\n\n请重新安装 Honor Control。",
                "Honor Control", 0x00000010);
            return;
        }

        string processName = Path.GetFileNameWithoutExtension(executable);

        // 已经开着就把它提到前台，而不是再开一个窗口。
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.MainWindowHandle != IntPtr.Zero && Activate(process.MainWindowHandle)) return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBoxW(IntPtr.Zero,
                "打开控制面板失败：" + exception.Message,
                "Honor Control", 0x00000010);
        }
    }

    /// <summary>
    /// 面板 exe 的位置。安装布局是 {app}\tray\ 与 {app}\panel\，
    /// 但开发时也可能把两者放在同一目录，所以按候选列表依次找。
    /// </summary>
    private static string? ResolvePanelExecutable()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDirectory, "..", "panel", "honor-control-panel.exe"),
            Path.Combine(baseDirectory, "..", "panel", "HonorControl.Panel.exe"),
            Path.Combine(baseDirectory, "honor-control-panel.exe"),
            Path.Combine(baseDirectory, "HonorControl.Panel.exe"),
        ];

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static bool Activate(IntPtr window)
    {
        ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
