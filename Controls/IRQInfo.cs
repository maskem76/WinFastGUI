namespace WinFastGUI.Controls
{
    public class IRQInfo
    {
        public int IrqNumber { get; set; }
        public string DeviceName { get; set; } = "";
        public string CurrentAffinity { get; set; } = "";
        public string NewAffinity { get; set; } = "";
    }
}
