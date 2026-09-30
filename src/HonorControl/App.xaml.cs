using System.Runtime.InteropServices;
using System.Security.Principal;
using HonorControl.Services;
using Microsoft.UI.Xaml;

namespace HonorControl;

public partial class App : Application
{
    private Window? window;
    private Mutex? instanceMutex;
    private EventWaitHandle? activationEvent;

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
            string userSid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
            string instanceName = @"Local\HonorControl.UI." + userSid;
            activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Open");
            instanceMutex = new Mutex(false, instanceName, out bool firstInstance);
            if (!firstInstance)
            {
                activationEvent.Set();
                Exit();
                return;
            }
            AppDiagnostics.Write("[launch] Creating main window.");
            window = new MainWindow();
            window.Activate();
            _ = Task.Run(() => ListenForActivation((MainWindow)window, activationEvent));
            AppDiagnostics.Write("[launch] Main window activated.");
        }
        catch (Exception exception)
        {
            AppDiagnostics.WriteException("launch", exception);
            ShowStartupError(exception);
            Exit();
        }
    }

    private static void ListenForActivation(MainWindow mainWindow, EventWaitHandle signal)
    {
        while (true)
        {
            signal.WaitOne();
            if (!mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ShowFromExternalRequest)) return;
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
