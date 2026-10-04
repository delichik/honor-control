using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using HonorControl.Contracts;

namespace HonorControl.Tray;

/// <summary>
/// 与服务之间的连接。
///
/// 托盘是**只读客户端**：只发 GetState / GetTelemetry / ShutdownService 三类请求，
/// 不写任何配置（配置由面板写，托盘不参与，这样归属语义不会因为托盘自启而改变）。
///
/// 每次请求一条新连接、一问一答，与服务端 PipeServer 的实现一致（单行 JSON）。
/// </summary>
internal sealed class ServiceLink
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly JsonSerializerOptions json = new();

    /// <summary>最近一次成功读到快照的时间，用于判断服务是否还在。</summary>
    public DateTimeOffset LastSuccess { get; private set; } = DateTimeOffset.MinValue;

    public string? LastError { get; private set; }

    public bool TryGetSnapshot(out ServiceSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            ServiceResponse response = Send("GetState", null);
            snapshot = response.Snapshot;
            LastSuccess = DateTimeOffset.Now;
            LastError = null;
            return snapshot != null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return false;
        }
    }

    public bool TryGetTelemetry(out Contracts.Telemetry? telemetry)
    {
        telemetry = null;
        try
        {
            ServiceResponse response = Send("GetTelemetry", null);
            telemetry = response.Telemetry;
            return telemetry != null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 请求服务**自己**停止。
    /// 托盘是中完整性进程，没有也不需要有 SERVICE_STOP 权限——这是"退出托盘 = 停止服务"的实现方式。
    /// </summary>
    public bool TryShutdownService(out string? error)
    {
        error = null;
        try
        {
            Send("ShutdownService", null);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private ServiceResponse Send(string command, object? desired)
    {
        using CancellationTokenSource timeout = new(Timeout);
        using NamedPipeClientStream pipe = new(
            ".", ServiceContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Impersonation);
        pipe.Connect((int)Timeout.TotalMilliseconds);

        using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };

        ServiceRequest request = new(ServiceContract.ProtocolVersion, command, desired as DesiredConfiguration);
        writer.WriteLine(JsonSerializer.Serialize(request, json));

        Task<string?> read = reader.ReadLineAsync(timeout.Token).AsTask();
        if (!read.Wait(Timeout))
        {
            throw new TimeoutException("服务没有在 5 秒内响应。");
        }

        string? line = read.Result;
        if (line == null) throw new IOException("服务在返回结果前关闭了连接。");

        ServiceResponse response = JsonSerializer.Deserialize<ServiceResponse>(line, json)
            ?? throw new InvalidDataException("服务返回了空响应。");
        if (response.Error != null) throw new InvalidOperationException(response.Error);
        return response;
    }
}
