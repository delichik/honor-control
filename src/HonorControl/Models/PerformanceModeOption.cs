namespace HonorControl.Models
{
    public sealed class PerformanceModeOption
    {
        public PerformanceModeOption(int mode, string label)
        {
            Mode = mode;
            Label = label;
        }

        public int Mode { get; private set; }
        public string Label { get; private set; }
    }
}
