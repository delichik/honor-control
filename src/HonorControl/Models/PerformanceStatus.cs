namespace HonorControl.Models
{
    public sealed class PerformanceStatus
    {
        public PerformanceStatus(string modeQuery, string supportQuery, string adapterQuery, int supportMask, int adapterOutput)
        {
            ModeQuery = modeQuery;
            SupportQuery = supportQuery;
            AdapterQuery = adapterQuery;
            SupportMask = supportMask;
            AdapterOutput = adapterOutput;
        }

        public string ModeQuery { get; private set; }
        public string SupportQuery { get; private set; }
        public string AdapterQuery { get; private set; }
        public int SupportMask { get; private set; }
        public int AdapterOutput { get; private set; }
    }
}
