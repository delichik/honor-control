namespace HonorControl.Models;

public sealed class PerformanceStatus
{
    public PerformanceStatus(int currentMode, string modeStateQuery)
    {
        CurrentMode = currentMode;
        ModeStateQuery = modeStateQuery;
    }

    public int CurrentMode { get; }
    public string ModeStateQuery { get; }
}
