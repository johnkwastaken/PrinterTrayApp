namespace PrinterTrayApp.Models;

public class EnhancedPrintResponse
{
    public bool Success { get; set; }
    public string? Guid { get; set; }
    public int? SpoolerId { get; set; }
    public string? PrinterName { get; set; }
    public string? DocumentName { get; set; }
    public bool HasErrors { get; set; }
    public bool HasPrinterIssues { get; set; }
    public string? Message { get; set; }
    public int? RetryCount { get; set; }
}