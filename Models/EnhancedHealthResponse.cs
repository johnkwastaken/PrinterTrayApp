namespace PrinterTrayApp.Models;

public class EnhancedHealthResponse
{
    public bool Ok { get; set; }
    public string Version { get; set; } = "0.1.0";
    public bool HasPrinterIssues { get; set; }
    public List<PrinterHealthStatus> Printers { get; set; } = new();
    public PrinterHealthSummary Summary { get; set; } = new();
    public long UptimeSeconds { get; set; }
}

public class PrinterHealthStatus
{
    public string Name { get; set; } = "";
    public bool IsOnline { get; set; }
    public string Status { get; set; } = "";
    public int JobCount { get; set; }
}

public class PrinterHealthSummary
{
    public int TotalPrinters { get; set; }
    public int OnlinePrinters { get; set; }
    public int OfflinePrinters { get; set; }
    public int PrintersWithJobs { get; set; }
}