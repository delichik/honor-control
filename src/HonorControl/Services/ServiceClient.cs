using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Principal;
using HonorControl.Contracts;

namespace HonorControl.Services;

public sealed class ServiceClient
{
    public Task<ServiceSnapshot> GetStateAsync() => SendAsync("GetState");

    public Task<ServiceSnapshot> SaveChargeAsync(int start, int end) =>
        SendAsync("SetCharge", new DesiredConfiguration(ChargeStart: start, ChargeEnd: end));

    public Task<ServiceSnapshot> SavePerformanceAsync(int mode) =>
        SendAsync("SetPerformance", new DesiredConfiguration(PerformanceMode: mode));

    public Task<ServiceSnapshot> SetAutoReconcileAsync(bool enabled) =>
        SendAsync("SetAutoReconcile", new DesiredConfiguration(AutoReconcile: enabled));

    private static async Task<ServiceSnapshot> SendAsync(string command, DesiredConfiguration? desired = null)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        using NamedPipeClientStream pipe = new(".", ServiceContract.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token);
        using StreamReader reader = new(pipe, Encoding.UTF8, false, 1024, true);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new ServiceRequest(ServiceContract.ProtocolVersion, command, desired)));
        string? line = await reader.ReadLineAsync(timeout.Token);
        if (line == null) throw new IOException("Honor Control 服务已断开连接。");
        ServiceResponse response = JsonSerializer.Deserialize<ServiceResponse>(line)
            ?? throw new InvalidDataException("服务返回了空响应。");
        if (response.Version != ServiceContract.ProtocolVersion)
            throw new InvalidDataException("应用与服务版本不一致，请重新安装应用。");
        if (response.Error != null) throw new InvalidOperationException(response.Error);
        return response.Snapshot ?? throw new InvalidDataException("服务未返回状态。");
    }
}
