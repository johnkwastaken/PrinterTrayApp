namespace PrinterTrayApp.Models;

public class PrintResult
{
    public bool Success { get; set; }
    public string? Guid { get; set; }
    public int? SpoolerJobId { get; set; }
    public string? PrinterName { get; set; }
    public string? DocumentName { get; set; }
    public string? Status { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public bool HasErrors { get; set; }
    public bool HasPrinterIssues { get; set; }
}