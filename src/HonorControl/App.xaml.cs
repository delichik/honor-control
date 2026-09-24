using System.Runtime.InteropServices;
using HonorControl.Services;
using Microsoft.UI.Xaml;

namespace HonorControl;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        AppDiagnostics.BeginSession();
        UnhandledException += App_UnhandledException;
        try
        {
            InitializeComponent();
            AppDiagnostics.Write("[app] XAML resources initialized.");
        }
        catch (Exception exception)
        {
            AppDiagnostics.WriteException("app-initialize", exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppDiagnostics.Write("[launch] Creating main window.");
            window = new MainWindow();
            window.Activate();
            AppDiagnostics.Write("[launch] Main window activated.");
        }
        catch (Exception exception)
        {
            AppDiagnostics.WriteException("launch", exception);
            ShowStartupError(exception);
            Exit();
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        AppDiagnostics.WriteException("unhandled", e.Exception);

    private static void ShowStartupError(Exception exception)
    {
        string message = "Honor Control 启动失败。\n\n"
            + exception.GetType().Name + "：" + exception.Message + "\n\n"
            + "诊断日志：" + AppDiagnostics.LogPath;
        MessageBoxW(IntPtr.Zero, message, "Honor Control", 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}
