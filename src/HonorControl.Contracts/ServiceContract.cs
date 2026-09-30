namespace HonorControl.Contracts;

public static class ServiceContract
{
    public const string PipeName = "HonorControl.Service.v1";
    public const int ProtocolVersion = 1;
}

public sealed record DesiredConfiguration(
    int? ChargeStart = null,
    int? ChargeEnd = null,
    int? PerformanceMode = null,
    bool AutoReconcile = false);

public sealed record ActualState(
    int? ChargeStart = null,
    int? ChargeEnd = null,
    int? PerformanceMode = null,
    string? PowerSchemeName = null,
    bool? IsOnAcPower = null,
    int? BatteryPercent = null,
    bool PcManagerOpen = false,
    bool BalancedPlanAvailable = false,
    bool HonorPerformancePlanAvailable = false,
    bool HighPerformanceSupported = false,
    string? ChargeError = null,
    string? PerformanceError = null,
    string? ServiceError = null,
    DateTimeOffset? CheckedAt = null);

public sealed record ServiceSnapshot(DesiredConfiguration Desired, ActualState Actual);

public sealed record ServiceRequest(int Version, string Command, DesiredConfiguration? Desired = null);

public sealed record ServiceResponse(int Version, ServiceSnapshot? Snapshot = null, string? Error = null);
