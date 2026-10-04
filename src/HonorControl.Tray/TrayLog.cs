using System.Text;

namespace HonorControl.Tray;

/// <summary>
/// 极简诊断日志。
///
/// 托盘是个没有控制台、没有窗口的常驻进程：一旦"服务连不上就退出"这类逻辑出问题，
/// 用户看到的现象只是"图标没了"，没有任何线索。所以它必须留下痕迹。
///
/// 位置：%LocalAppData%\HonorControl\tray.log（每个用户自己的目录，卸载不会残留系统级数据）。
/// 写入永远是 best-effort：日志失败绝不能影响托盘本身。
/// </summary>
internal static class TrayLog
{
    private const long MaxBytes = 256 * 1024;

    private static readonly object Sync = new();
    private static readonly string LogPath = BuildPath();

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                Trim();
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败不影响任何功能。
        }
    }

    private static void Trim()
    {
        FileInfo info = new(LogPath);
        if (info.Exists && info.Length > MaxBytes)
        {
            // 超限就整体重开，不做滚动归档——这是诊断日志，不是审计日志。
            File.Delete(LogPath);
        }
    }

    private static string BuildPath()
    {
        // 允许用环境变量改路径：诊断时可以把日志指到可写目录（受限环境里 %LocalAppData% 可能写不进去）。
        string? overridden = Environment.GetEnvironmentVariable("HONORCONTROL_TRAY_LOG");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "HonorControl", "tray.log");
    }
}
