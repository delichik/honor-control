using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using HonorControl.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service;

internal sealed class PipeServer : BackgroundService
{
    private readonly ConfigurationStore configuration;
    private readonly ReconciliationCoordinator coordinator;
    private readonly ILogger<PipeServer> logger;
    private readonly PipeSecurity security = CreateSecurity();

    public PipeServer(ConfigurationStore configuration, ReconciliationCoordinator coordinator, ILogger<PipeServer> logger)
    {
        this.configuration = configuration;
        this.coordinator = coordinator;
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
                string? sid = null;
                pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent().User?.Value);
                using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);
                using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                string? line = await ReadRequestLineAsync(reader, timeout.Token);
                ServiceResponse response;
                int clientSession = -1;
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientProcessId))
                {
                    using Process client = Process.GetProcessById(checked((int)clientProcessId));
                    clientSession = client.SessionId;
                }
                uint consoleSession = WTSGetActiveConsoleSessionId();
                int activeConsoleSession = consoleSession == uint.MaxValue ? -1 : (int)consoleSession;
                if (!configuration.AuthorizeOrEnroll(sid, clientSession, activeConsoleSession))
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
            if (request.Command == "GetState")
                return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());
            if (request.Desired == null)
                throw new InvalidDataException("缺少配置内容。");
            coordinator.Update(request.Command, request.Desired);
            return new(ServiceContract.ProtocolVersion, coordinator.Snapshot());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Rejected service request");
            return new(ServiceContract.ProtocolVersion, Error: exception.Message);
        }
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
