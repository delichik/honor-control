namespace HonorControl.Models;

public sealed class ActivityRecord
{
    public ActivityRecord(string message, bool isError)
    {
        DisplayTime = DateTime.Now.ToString("HH:mm:ss");
        Message = message;
        IsError = isError;
    }

    public string DisplayTime { get; }
    public string Message { get; }
    public bool IsError { get; }
}
