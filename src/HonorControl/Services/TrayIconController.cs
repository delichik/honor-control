using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace HonorControl.Services;

public sealed class TrayIconController : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x21;
    private const uint IconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NifGuid = 0x00000020;
    private const uint NifShowTip = 0x00000080;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrDefaultSize = 0x00000040;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmUser = 0x0400;
    private const uint NinSelect = WmUser;
    private const uint NinKeySelect = WmUser + 1;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNull = 0x0000;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint OpenCommand = 1001;
    private const uint ExitCommand = 1002;
    private const uint NiifInfo = 0x00000001;
    private const uint MsgfltAllow = 1;
    private static readonly Guid TrayIconGuid = new("764AE595-3200-45B8-9A7B-4837B9EFACD8");

    private readonly IntPtr windowHandle;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly SubclassProcedure subclassProcedure;
    private readonly uint taskbarCreatedMessage;
    private IntPtr iconHandle;
    private bool ownsIconHandle;
    private bool subclassAttached;
    private bool disposed;

    public TrayIconController(IntPtr windowHandle, DispatcherQueue dispatcherQueue, string iconPath)
    {
        this.windowHandle = windowHandle;
        this.dispatcherQueue = dispatcherQueue;
        subclassProcedure = WindowSubclass;
        taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        AppDiagnostics.Write($"[tray] Begin. Hwnd=0x{windowHandle.ToInt64():X}; TaskbarCreated=0x{taskbarCreatedMessage:X}.");

        if (!AllowElevatedWindowMessage(CallbackMessage))
        {
            int error = Marshal.GetLastWin32Error();
            LastError = "无法接收系统托盘消息：" + new Win32Exception(error).Message;
            AppDiagnostics.Write($"[tray] ChangeWindowMessageFilterEx callback failed. Error={error}; {LastError}");
            return;
        }
        AppDiagnostics.Write("[tray] Callback message filter allowed.");
        if (taskbarCreatedMessage != 0 && !AllowElevatedWindowMessage(taskbarCreatedMessage))
        {
            int error = Marshal.GetLastWin32Error();
            LastError = "任务栏重启后可能无法自动恢复托盘图标：" + new Win32Exception(error).Message;
            AppDiagnostics.Write($"[tray] ChangeWindowMessageFilterEx TaskbarCreated failed. Error={error}; {LastError}");
        }
        else
        {
            AppDiagnostics.Write("[tray] TaskbarCreated message filter allowed.");
        }

        subclassAttached = SetWindowSubclass(windowHandle, subclassProcedure, new UIntPtr(1), IntPtr.Zero);
        if (!subclassAttached)
        {
            int error = Marshal.GetLastWin32Error();
            LastError = new Win32Exception(error).Message;
            AppDiagnostics.Write($"[tray] SetWindowSubclass failed. Error={error}; {LastError}");
            return;
        }
        AppDiagnostics.Write("[tray] Window subclass attached.");

        iconHandle = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        ownsIconHandle = iconHandle != IntPtr.Zero;
        AppDiagnostics.Write($"[tray] LoadImage result=0x{iconHandle.ToInt64():X}; Path={iconPath}");
        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(32512));
            AppDiagnostics.Write($"[tray] Default icon result=0x{iconHandle.ToInt64():X}.");
        }

        if (iconHandle == IntPtr.Zero || !AddIcon())
        {
            int error = Marshal.GetLastWin32Error();
            LastError = new Win32Exception(error).Message;
            AppDiagnostics.Write($"[tray] Initialization failed. Error={error}; {LastError}");
            Dispose();
            return;
        }

        IsAvailable = true;
        AppDiagnostics.Write("[tray] Initialization completed.");
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public bool IsAvailable { get; private set; }
    public bool SystemEnding { get; private set; }
    public string? LastError { get; private set; }

    public void ShowNotification(string title, string message)
    {
        if (!IsAvailable || disposed) return;
        NotifyIconData data = CreateIconData(NifInfo | NifGuid);
        data.InfoTitle = title;
        data.Info = message;
        data.InfoFlags = NiifInfo;
        data.TimeoutOrVersion = 5000;
        ShellNotifyIconW(NimModify, ref data);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AppDiagnostics.Write($"[tray] Disposing. Available={IsAvailable}; SubclassAttached={subclassAttached}.");

        if (IsAvailable)
        {
            NotifyIconData data = CreateIconData(NifGuid);
            ShellNotifyIconW(NimDelete, ref data);
        }
        IsAvailable = false;

        if (subclassAttached)
        {
            RemoveWindowSubclass(windowHandle, subclassProcedure, new UIntPtr(1));
            subclassAttached = false;
        }

        if (ownsIconHandle && iconHandle != IntPtr.Zero)
        {
            DestroyIcon(iconHandle);
        }
        iconHandle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private bool AddIcon()
    {
        NotifyIconData data = CreateIconData(NifMessage | NifIcon | NifTip | NifGuid | NifShowTip);
        data.CallbackMessage = CallbackMessage;
        data.IconHandle = iconHandle;
        data.Tip = "Honor Control";
        if (!ShellNotifyIconW(NimAdd, ref data))
        {
            int error = Marshal.GetLastWin32Error();
            AppDiagnostics.Write($"[tray] NIM_ADD failed. Error={error}; {new Win32Exception(error).Message}");
            return false;
        }
        AppDiagnostics.Write("[tray] NIM_ADD succeeded.");

        data.TimeoutOrVersion = NotifyIconVersion4;
        if (ShellNotifyIconW(NimSetVersion, ref data))
        {
            AppDiagnostics.Write("[tray] NIM_SETVERSION succeeded.");
            return true;
        }

        int versionError = Marshal.GetLastWin32Error();
        AppDiagnostics.Write($"[tray] NIM_SETVERSION failed. Error={versionError}; {new Win32Exception(versionError).Message}");
        ShellNotifyIconW(NimDelete, ref data);
        return false;
    }

    private NotifyIconData CreateIconData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = windowHandle,
        Id = IconId,
        Flags = flags,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
        GuidItem = TrayIconGuid
    };

    private IntPtr WindowSubclass(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData)
    {
        if (message == CallbackMessage)
        {
            HandleTrayMessage(unchecked((uint)lParam.ToInt64()) & 0xFFFF);
            return IntPtr.Zero;
        }
        if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
        {
            IsAvailable = AddIcon();
            if (!IsAvailable) LastError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return IntPtr.Zero;
        }
        if (message == WmQueryEndSession || message == WmEndSession && wParam != IntPtr.Zero)
        {
            SystemEnding = true;
        }
        if (message == WmNcDestroy)
        {
            AppDiagnostics.Write("[tray] WM_NCDESTROY received.");
            IsAvailable = false;
            subclassAttached = false;
        }
        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void HandleTrayMessage(uint notification)
    {
        if (notification is WmLButtonUp or WmLButtonDoubleClick or NinSelect or NinKeySelect)
        {
            Queue(OpenRequested);
            return;
        }
        if (notification == WmContextMenu)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MfString, new UIntPtr(OpenCommand), "打开 Honor Control");
            AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
            AppendMenuW(menu, MfString, new UIntPtr(ExitCommand), "退出");
            GetCursorPos(out Point cursor);
            SetForegroundWindow(windowHandle);
            uint command = TrackPopupMenuEx(menu, TpmReturnCommand | TpmRightButton, cursor.X, cursor.Y, windowHandle, IntPtr.Zero);
            PostMessageW(windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (command == OpenCommand) Queue(OpenRequested);
            if (command == ExitCommand) Queue(ExitRequested);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void Queue(Action? action)
    {
        if (action == null || disposed) return;
        dispatcherQueue.TryEnqueue(() => action());
    }

    private bool AllowElevatedWindowMessage(uint message)
    {
        ChangeFilterStruct filter = new() { Size = (uint)Marshal.SizeOf<ChangeFilterStruct>() };
        return ChangeWindowMessageFilterEx(windowHandle, message, MsgfltAllow, ref filter);
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct ChangeFilterStruct
    {
        public uint Size;
        public uint ExtendedStatus;
    }

    private delegate IntPtr SubclassProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr referenceData);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcedure callback, UIntPtr subclassId, IntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProcedure callback, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint loadFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, ref ChangeFilterStruct filter);

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
}
