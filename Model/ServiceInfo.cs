namespace WinFastGUI.Model
{
    public class ServiceInfo
    {
        public string ServiceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string StartType { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Dependencies { get; set; } = "";
        public string Note { get; set; } = "";
        public double CpuUsage { get; set; } = 0.0;

        // Önerilen durdurulabilir servis mi? (ServiceManager.cs için)
        public bool IsRecommendedStoppable { get; set; } = false;
    }
}