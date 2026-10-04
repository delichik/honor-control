using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace HonorControl.Service;

/// <summary>
/// 在配置拥有者的会话里拉起托盘进程。
///
/// 为什么服务不能直接 CreateProcess：服务跑在会话 0，而托盘必须在用户会话里（托盘图标只有
/// 同会话进程能注册）。这里用**复制用户 shell 的令牌**的办法跨过会话边界：
/// 找到该会话里的 explorer.exe，复制它的主令牌，用 CreateProcessAsUser 以 "winsta0\default"
/// 桌面启动托盘。
///
/// 为什么不用计划任务（schtasks）：那条路要在安装时注册任务、运行期 spawn schtasks.exe。
/// 安装流程已经因为杀软敏感而全面去脚本化，运行期再留一个"服务派生 schtasks"同样不划算——
/// 改成进程内 API 调用后，服务不依赖任何外部工具，也不需要任务计划程序的额外权限面。
///
/// 复制 shell 令牌还有一个附带好处：托盘拿到的是**中完整性**令牌（与 explorer 相同），
/// 用 WTSQueryUserToken 则会得到用户的完整令牌，管理员账户下会变成静默高完整性。
///
/// 未在真机验证：本机没有安装服务，这条路径需要 SYSTEM 身份 + 真实交互式会话才能跑。
/// 失败时只记日志并等下一轮，不影响服务其它功能。
/// </summary>
internal static class TrayLauncher
{
    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;
    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNewProcessGroup = 0x00000200;

    /// <summary>
    /// 在拥有者所在的交互式会话里启动托盘。返回 false 时 <paramref name="error"/> 给出原因。
    /// </summary>
    public static bool TryLaunch(string? ownerSid, string trayPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(ownerSid))
        {
            error = "尚未确定配置拥有者，无法决定在哪个会话里启动托盘。";
            return false;
        }

        if (!File.Exists(trayPath))
        {
            error = $"托盘程序不存在：{trayPath}";
            return false;
        }

        IntPtr shellToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        try
        {
            shellToken = FindShellToken(ownerSid, out int sessionId, out error);
            if (shellToken == IntPtr.Zero) return false;

            if (!DuplicateTokenEx(shellToken, MaximumAllowed, IntPtr.Zero,
                    SecurityImpersonationLevel.SecurityImpersonation, TokenType.TokenPrimary, out primaryToken))
            {
                error = "复制用户令牌失败：" + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                environment = IntPtr.Zero; // 没有环境块也能启动，只是子进程继承服务的环境
            }

            string commandLine = "\"" + trayPath + "\"";
            StartupInfo startup = new()
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                // 关键：指定交互式桌面，否则进程会落在服务的非交互桌面上，托盘图标没人看得见。
                Desktop = @"winsta0\default",
            };

            if (!CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CreateUnicodeEnvironment | CreateNewProcessGroup, environment,
                    Path.GetDirectoryName(trayPath), ref startup, out ProcessInformation process))
            {
                error = "在会话 " + sessionId + " 中启动托盘失败：" +
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            CloseHandle(process.Thread);
            CloseHandle(process.Process);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
        }
    }

    /// <summary>
    /// 找到拥有者会话里 explorer.exe 的令牌（调用方负责关闭）。
    /// 同时通过 <paramref name="sessionId"/> 回传会话号，便于日志与错误信息。
    /// </summary>
    private static IntPtr FindShellToken(string ownerSid, out int sessionId, out string? error)
    {
        sessionId = -1;
        error = null;
        bool seenExplorer = false;

        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                if (!OpenProcessToken(process.Handle, TokenQuery, out IntPtr queryToken)) continue;

                string? sid;
                try
                {
                    seenExplorer = true;
                    using WindowsIdentity identity = new(queryToken);
                    sid = identity.User?.Value;
                }
                finally
                {
                    CloseHandle(queryToken);
                }

                if (!string.Equals(sid, ownerSid, StringComparison.OrdinalIgnoreCase)) continue;

                sessionId = process.SessionId;

                // 换成可复制的令牌句柄（上面那个只有查询权限）。
                if (!OpenProcessToken(process.Handle, TokenQuery | TokenDuplicate, out IntPtr duplicate))
                {
                    error = "打开用户 shell 令牌失败：" +
                        new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return IntPtr.Zero;
                }

                return duplicate;
            }
        }

        error = seenExplorer
            ? "拥有者的会话里没有找到资源管理器进程（可能尚未登录或使用了非标准 shell）。"
            : "系统里没有资源管理器进程，说明当前没有交互式会话。";
        return IntPtr.Zero;
    }

    private enum TokenType
    {
        TokenPrimary = 1,
        TokenImpersonation = 2,
    }

    private enum SecurityImpersonationLevel
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Count;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken, uint desiredAccess, IntPtr attributes,
        SecurityImpersonationLevel impersonationLevel, TokenType tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string? applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
