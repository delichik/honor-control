namespace HonorControl.Models
{
    public sealed class ChargeThreshold
    {
        public ChargeThreshold(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; private set; }
        public int End { get; private set; }
    }
}
