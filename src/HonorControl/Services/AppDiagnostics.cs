using System.Text;

namespace HonorControl.Services;

public static class AppDiagnostics
{
    private static readonly object SyncRoot = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HonorControl");

    public static string LogPath { get; } = Path.Combine(DirectoryPath, "startup.log");

    public static void BeginSession()
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string header = $"{DateTimeOffset.Now:O} [startup] Honor Control {Environment.ProcessId}\r\n"
                    + $"Executable={Environment.ProcessPath}\r\n"
                    + $"OS={Environment.OSVersion}; Framework={Environment.Version}; Elevated={IsElevated()}\r\n";
                File.WriteAllText(LogPath, header, new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never prevent the application from starting.
            }
        }
    }

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}\r\n", new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never affect the application lifecycle.
            }
        }
    }

    public static void WriteException(string stage, Exception exception) =>
        Write($"[{stage}] {exception}");

    private static bool IsElevated()
    {
        try
        {
            using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
