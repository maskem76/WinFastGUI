namespace WinFastGUI.Services
{
    public enum ReportType
    {
        Log,
        Progress,
        Error,
        Result
    }

    public class BackupProgressReport
    {
        public ReportType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ResultPath { get; set; }
        public int Percent { get; set; }
    }
}