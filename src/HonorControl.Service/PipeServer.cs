using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using HonorControl.Contracts;
using HonorControl.Service.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service;

internal sealed class PipeServer : BackgroundService
{
    private readonly ConfigurationStore configuration;
    private readonly ReconciliationCoordinator coordinator;
    private readonly TelemetrySampler telemetry;
    private readonly HistoryStore history;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<PipeServer> logger;
    private readonly PipeSecurity security = CreateSecurity();

    public PipeServer(
        ConfigurationStore configuration,
        ReconciliationCoordinator coordinator,
        TelemetrySampler telemetry,
        HistoryStore history,
        IHostApplicationLifetime lifetime,
        ILogger<PipeServer> logger)
    {
        this.configuration = configuration;
        this.coordinator = coordinator;
        this.telemetry = telemetry;
        this.history = history;
        this.lifetime = lifetime;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using SemaphoreSlim slots = new(8, 8);
        List<Task> clients = new();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await slots.WaitAsync(stoppingToken);
                    NamedPipeServerStream pipe;
                    try
                    {
                        pipe = NamedPipeServerStreamAcl.Create(
                            ServiceContract.PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous, 0, 0, security);
                    }
                    catch
                    {
                        slots.Release();
                        throw;
                    }
                    try
                    {
                        await pipe.WaitForConnectionAsync(stoppingToken);
                    }
                    catch
                    {
                        pipe.Dispose();
                        slots.Release();
                        throw;
                    }
                    clients.RemoveAll(client => client.IsCompleted);
                    clients.Add(HandleClientAndReleaseAsync(pipe, slots, stoppingToken));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Named pipe listener failed");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
        finally { await Task.WhenAll(clients); }
    }

    private async Task HandleClientAndReleaseAsync(NamedPipeServerStream pipe, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try { await HandleClientAsync(pipe, stoppingToken); }
        finally { slots.Release(); }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        using (pipe)
        using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);
                using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

                // 先读请求再模拟身份：ImpersonateNamedPipeClient（RunAsClient 底层就是它）要求管道里
                // 已经有客户端写入的数据，否则会抛 IOException。真机事件日志里留下的就是这条：
                // "在使用命名管道读取数据之前，无法经由该管道模拟" —— 表现为请求随机失败。
                string? line = await ReadRequestLineAsync(reader, timeout.Token);

                string? sid = null;
                string? impersonationError = null;
                try
                {
                    pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent().User?.Value);
                }
                catch (Exception exception)
                {
                    impersonationError = exception.Message;
                    logger.LogWarning(exception, "无法获取命名管道客户端身份");
                }

                ServiceResponse response;
                int clientSession = -1;
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientProcessId))
                {
                    using Process client = Process.GetProcessById(checked((int)clientProcessId));
                    clientSession = client.SessionId;
                }
                uint consoleSession = WTSGetActiveConsoleSessionId();
                int activeConsoleSession = consoleSession == uint.MaxValue ? -1 : (int)consoleSession;
                if (impersonationError != null)
                    response = new(ServiceContract.ProtocolVersion, Error: "无法确认调用方身份：" + impersonationError);
                else if (!configuration.AuthorizeOrEnroll(sid, clientSession, activeConsoleSession))
                    response = new(ServiceContract.ProtocolVersion, Error: "当前 Windows 用户无权访问 Honor Control 服务。");
                else if (line == null)
                    response = new(ServiceContract.ProtocolVersion, Error: "服务请求无效。");
                else
                    response = Execute(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { logger.LogWarning(exception, "Client request failed"); }
        }
    }

    private ServiceResponse Execute(string line)
    {
        try
        {
            ServiceRequest request = JsonSerializer.Deserialize<ServiceRequest>(line)
                ?? throw new InvalidDataException("请求为空。");
            if (request.Version != ServiceContract.ProtocolVersion)
                throw new InvalidDataException("应用与服务的通信版本不一致，请重新安装应用。");

            switch (request.Command)
            {
                case "GetState":
                    return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());

                case "GetTelemetry":
                    return new(ServiceContract.ProtocolVersion, Telemetry: telemetry.Current);

                case "GetCapabilities":
                    return new(ServiceContract.ProtocolVersion, Capabilities: telemetry.Capabilities);

                case "GetHistory":
                    HistoryQuery query = request.History ?? new HistoryQuery("BatteryPower", "24h");
                    return new(ServiceContract.ProtocolVersion, History: history.Query(query));

                case "SetTrayPolicy":
                    // 托盘策略只由面板写；托盘进程本身是只读客户端。
                    coordinator.UpdateTrayPolicy(request.Desired?.TrayPolicy ?? TrayPolicyMode.OnDemand);
                    return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());

                case "ShutdownService":
                    // 由服务**自己**停止自己：托盘/面板都是中完整性进程，没有 SERVICE_STOP 权限，
                    // 也不需要申请提权（见可行性文档 4.1）。先把响应写回去，再延迟退出，
                    // 否则调用方只会看到连接被断开。
                    ScheduleShutdown();
                    return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());

                default:
                    if (request.Desired == null)
                        throw new InvalidDataException("缺少配置内容。");
                    coordinator.Update(request.Command, request.Desired);
                    return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Rejected service request");
            return new(ServiceContract.ProtocolVersion, Error: exception.Message);
        }
    }

    /// <summary>
    /// 延迟一小会儿再停：给调用方留出收到响应的时间。
    /// 只有配置拥有者能走到这里——归属校验在 <see cref="HandleClientAsync"/> 里已经完成
    /// （第一个从活动控制台会话连接的 Windows 用户成为拥有者）。
    /// </summary>
    private void ScheduleShutdown()
    {
        logger.LogInformation("收到停止服务请求，准备退出。");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            lifetime.StopApplication();
        });
    }

    private static async Task<string?> ReadRequestLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        char[] character = new char[1];
        while (builder.Length <= 4096)
        {
            int count = await reader.ReadAsync(character.AsMemory(), cancellationToken);
            if (count == 0) return builder.Length == 0 ? null : builder.ToString();
            if (character[0] == '\n') return builder.ToString().TrimEnd('\r');
            builder.Append(character[0]);
        }
        throw new InvalidDataException("服务请求过长。");
    }

    private static PipeSecurity CreateSecurity()
    {
        PipeSecurity result = new();
        result.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        result.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        result.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
