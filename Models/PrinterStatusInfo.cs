namespace PrinterTrayApp.Models;

public class PrinterStatusInfo
{
    public bool IsOnline { get; set; }
    public string DisplayText { get; set; } = "Unknown";
    public StatusSeverity Severity { get; set; }
}

public enum StatusSeverity
{
    Ready = 0,
    Active = 1,
    Warning = 2,
    Error = 3
}