namespace HonorControl.Models;

public sealed class PerformanceStatus
{
    public PerformanceStatus(
        int currentMode,
        string modeStateQuery,
        string telemetryQuery,
        string supportQuery,
        string adapterQuery,
        int supportMask,
        int adapterVoltageMillivolts)
    {
        CurrentMode = currentMode;
        ModeStateQuery = modeStateQuery;
        TelemetryQuery = telemetryQuery;
        SupportQuery = supportQuery;
        AdapterQuery = adapterQuery;
        SupportMask = supportMask;
        AdapterVoltageMillivolts = adapterVoltageMillivolts;
    }

    public int CurrentMode { get; }
    public string ModeStateQuery { get; }
    public string TelemetryQuery { get; }
    public string SupportQuery { get; }
    public string AdapterQuery { get; }
    public int SupportMask { get; }
    public int AdapterVoltageMillivolts { get; }
    public bool SupportsHunterMode => SupportMask != 0;
}
