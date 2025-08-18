namespace PrinterTrayApp.Models;

public class SpoolerStatusResponse
{
    public int SpoolerId { get; set; }
    public string? Guid { get; set; }
    public bool Found { get; set; }
    public string? PrinterName { get; set; }
    public string? Status { get; set; }
    public bool IsError { get; set; }
    public bool IsPrinting { get; set; }
    public bool IsPaused { get; set; }
    public bool IsComplete { get; set; }
}