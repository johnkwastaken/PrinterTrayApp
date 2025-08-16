namespace PrinterTrayApp.Models;

public class PrinterInfo
{
    public string LogicalName { get; set; } = string.Empty;
    public string WindowsPrinterName { get; set; } = string.Empty;
    public string PortName { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool HasError { get; set; }
    public bool IsPaused { get; set; }
    public string Status { get; set; } = "Unknown";
    public uint JobCount { get; set; }
    public bool IsDefault { get; set; }
}