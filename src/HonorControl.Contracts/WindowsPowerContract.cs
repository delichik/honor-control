namespace HonorControl.Contracts;

/// <summary>Only submitted fields are changed. Timeout 0 means never; otherwise use seconds.</summary>
public sealed record WindowsPowerSettingsUpdate(
    string? SchemeId = null,
    uint? AcDisplayTimeoutSeconds = null,
    uint? DcDisplayTimeoutSeconds = null,
    uint? AcSleepTimeoutSeconds = null,
    uint? DcSleepTimeoutSeconds = null,
    int? AcIntelGraphicsPowerPlan = null,
    int? DcIntelGraphicsPowerPlan = null,
    string? ExpectedActiveSchemeId = null,
    uint? AcDiskTimeoutSeconds = null,
    uint? DcDiskTimeoutSeconds = null);

public sealed record WindowsPowerScheme(string Id, string Name);

/// <summary>Null is unavailable, with a reason under the corresponding property name.</summary>
public sealed record WindowsPowerSettingsSnapshot(
    string? ActiveSchemeId,
    IReadOnlyList<WindowsPowerScheme> Schemes,
    uint? AcDisplayTimeoutSeconds = null,
    uint? DcDisplayTimeoutSeconds = null,
    uint? AcSleepTimeoutSeconds = null,
    uint? DcSleepTimeoutSeconds = null,
    int? AcIntelGraphicsPowerPlan = null,
    int? DcIntelGraphicsPowerPlan = null,
    bool CanRestore = false,
    IReadOnlyDictionary<string, string>? MissingReason = null,
    uint? AcDiskTimeoutSeconds = null,
    uint? DcDiskTimeoutSeconds = null);
