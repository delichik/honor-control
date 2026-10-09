using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HonorControl.Tray;

/// <summary>
/// 托盘图标宿主：一个**隐藏的顶层窗口** + Shell_NotifyIcon。
///
/// 两个关键约束（都是踩过的坑）：
/// 1. 窗口必须是**顶层窗口**，不能用 HWND_MESSAGE 消息窗口——TaskbarCreated 是广播消息，
///    消息专用窗口收不到，那样 Explorer 重启后图标就再也回不来了；
/// 2. 用 NIM_SETVERSION 4 + 固定 GUID 注册，Explorer 重启后能按 GUID 恢复，而不是变成重复图标。
///
/// 本进程不画任何自绘界面：菜单用 Win32 原生菜单，确认框用 MessageBox，
/// 视觉与系统一致，也不需要引入 WinForms（那会把依赖扩到 WindowsDesktop 运行时）。
/// </summary>
internal sealed class TrayWindow : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x21; // WM_APP + 0x21
    private const uint IconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;
    private const uint NifInfo = 0x0010;
    private const uint NifGuid = 0x0020;
    private const uint NifShowTip = 0x0080;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint WmCommand = 0x0111;
    private const uint WmDestroy = 0x0002;
    private const uint WmTimer = 0x0113;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmUser = 0x0400;
    private const uint NinSelect = WmUser;
    private const uint NinKeySelect = WmUser + 1;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint PollTimerId = 1;
    private const uint NiifInfo = 0x0001;
    private const uint CommandOpen = 1001;
    private const uint CommandExit = 1002;

    private static readonly Guid TrayIconGuid = new("764AE595-3200-45B8-9A7B-4837B9EFACD8");

    private readonly WindowProcedure procedure;
    private readonly uint taskbarCreatedMessage;
    private IntPtr handle;
    private IntPtr iconHandle;
    private bool iconAdded;
    private bool disposed;

    public TrayWindow()
    {
        procedure = WindowProc;
        taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        CreateHostWindow();
        LoadIcon();
        AddIcon("Honor Control");
    }

    /// <summary>左键点击或菜单"打开控制面板"。</summary>
    public event Action? OpenRequested;

    /// <summary>退出托盘进程；后台服务保持运行。</summary>
    public event Action? ExitRequested;

    /// <summary>轮询节拍：由宿主决定节奏（托盘只负责转发消息）。</summary>
    public event Action? PollRequested;

    public IntPtr Handle => handle;

    /// <summary>把托盘消息循环跑起来，直到窗口销毁。</summary>
    public void RunMessageLoop()
    {
        while (GetMessageW(out Message message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    public void StartPolling(int intervalMs) => SetTimer(handle, PollTimerId, (uint)intervalMs, IntPtr.Zero);

    public void SetTooltip(string text)
    {
        if (!iconAdded) return;
        NotifyIconData data = CreateIconData(NifTip | NifGuid);
        data.Tip = Truncate(text, 127);
        ShellNotifyIconW(NimModify, ref data);
    }

    public void ShowNotification(string title, string message)
    {
        if (!iconAdded) return;
        NotifyIconData data = CreateIconData(NifInfo | NifGuid);
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(message, 255);
        data.InfoFlags = NiifInfo;
        data.TimeoutOrVersion = 5000;
        ShellNotifyIconW(NimModify, ref data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (iconAdded)
        {
            NotifyIconData data = CreateIconData(NifGuid);
            ShellNotifyIconW(NimDelete, ref data);
            iconAdded = false;
        }

        if (iconHandle != IntPtr.Zero)
        {
            DestroyIcon(iconHandle);
            iconHandle = IntPtr.Zero;
        }

        if (handle != IntPtr.Zero)
        {
            DestroyWindow(handle);
            handle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private void CreateHostWindow()
    {
        IntPtr instance = GetModuleHandleW(null);
        WindowClass windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 0,
            WindowProcedure = procedure,
            Instance = instance,
            ClassName = "HonorControlTrayHost",
        };

        ushort registered = RegisterClassExW(ref windowClass);
        if (registered == 0)
        {
            int error = Marshal.GetLastWin32Error();
            // 1410 = 类已注册（同一进程重复注册时可能出现），可以继续。
            if (error != 1410) throw new Win32Exception(error, "注册托盘宿主窗口类失败。");
        }

        // 顶层窗口但保持隐藏：既能收到 TaskbarCreated 广播，又不出现在任务栏与 Alt+Tab 里。
        handle = CreateWindowExW(
            0, "HonorControlTrayHost", "Honor Control", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建托盘宿主窗口失败。");
        }
    }

    private void LoadIcon()
    {
        foreach (string candidate in IconCandidates())
        {
            if (!File.Exists(candidate)) continue;
            iconHandle = LoadImageW(IntPtr.Zero, candidate, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (iconHandle != IntPtr.Zero) return;
        }

        // 退化到系统默认图标：没有图标也比进程起不来强。
        iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    private static IEnumerable<string> IconCandidates()
    {
        string baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "HonorControl.ico");
        yield return Path.Combine(baseDirectory, "Assets", "HonorControl.ico");
    }

    private bool AddIcon(string tooltip)
    {
        NotifyIconData data = CreateIconData(NifMessage | NifIcon | NifTip | NifGuid | NifShowTip);
        data.CallbackMessage = CallbackMessage;
        data.IconHandle = iconHandle;
        data.Tip = Truncate(tooltip, 127);

        if (!ShellNotifyIconW(NimAdd, ref data)) return false;

        data.TimeoutOrVersion = NotifyIconVersion4;
        if (ShellNotifyIconW(NimSetVersion, ref data))
        {
            iconAdded = true;
            return true;
        }

        ShellNotifyIconW(NimDelete, ref data);
        return false;
    }

    private NotifyIconData CreateIconData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = handle,
        Id = IconId,
        Flags = flags,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
        GuidItem = TrayIconGuid,
    };

    private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                HandleTrayMessage(unchecked((uint)lParam.ToInt64()) & 0xFFFF);
                return IntPtr.Zero;

            case WmCommand:
                HandleCommand(unchecked((uint)wParam.ToInt64()) & 0xFFFF);
                return IntPtr.Zero;

            case WmTimer:
                PollRequested?.Invoke();
                return IntPtr.Zero;

            case WmDestroy:
                KillTimer(hWnd, PollTimerId);
                PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
                {
                    // Explorer 重启：按 GUID 重新注册图标（不重新加载图标句柄）。
                    iconAdded = false;
                    AddIcon("Honor Control");
                    return IntPtr.Zero;
                }
                return DefWindowProcW(hWnd, message, wParam, lParam);
        }
    }

    private void HandleTrayMessage(uint notification)
    {
        switch (notification)
        {
            case WmLButtonUp:
            case WmLButtonDoubleClick:
            case NinSelect:
            case NinKeySelect:
                OpenRequested?.Invoke();
                break;
            case WmContextMenu:
            case WmRButtonUp:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(menu, MfString, new UIntPtr(CommandOpen), "打开控制面板");
            AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(menu, MfString | MfGrayed, UIntPtr.Zero, StatusLine);
            AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(menu, MfString, new UIntPtr(CommandExit), "退出托盘");

            GetCursorPos(out Point cursor);
            // 必须先置前台，否则菜单不会在点击别处时关闭（Win32 菜单的固定套路）。
            SetForegroundWindow(handle);
            uint command = TrackPopupMenuEx(menu, TpmReturnCommand | TpmRightButton, cursor.X, cursor.Y, handle, IntPtr.Zero);
            PostMessageW(handle, 0, IntPtr.Zero, IntPtr.Zero);

            HandleCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint command)
    {
        switch (command)
        {
            case CommandOpen:
                OpenRequested?.Invoke();
                break;
            case CommandExit:
                ExitRequested?.Invoke();
                break;
        }
    }

    /// <summary>菜单里那行灰色的状态文字，由宿主在每次轮询后更新。</summary>
    public string StatusLine { get; set; } = "正在连接服务…";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// WNDCLASSEXW。
    /// 注意最后那个 SmallIcon 字段不能省：cbSize 必须等于整个结构的大小（x64 上是 80 字节），
    /// 少一个 IntPtr 就会让 RegisterClassExW 直接返回 ERROR_INVALID_PARAMETER(87)。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Handle;
        public uint Type;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Position;
    }

    private delegate IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr itemId, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Message message, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hWnd, uint id, uint interval, IntPtr procedure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hWnd, uint id);

}
