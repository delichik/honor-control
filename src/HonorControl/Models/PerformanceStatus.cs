namespace HonorControl.Models
{
    public sealed class PerformanceStatus
    {
        public PerformanceStatus(string modeQuery, string supportQuery)
        {
            ModeQuery = modeQuery;
            SupportQuery = supportQuery;
        }

        public string ModeQuery { get; private set; }
        public string SupportQuery { get; private set; }
    }
}
