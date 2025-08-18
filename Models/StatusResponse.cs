namespace PrinterTrayApp.Models;

public class StatusResponse
{
    public int TotalJobs { get; set; }
    public List<JobStatusInfo> Jobs { get; set; } = new();
}

public class JobStatusInfo
{
    public string? Guid { get; set; }
    public int SpoolerId { get; set; }
    public string PrinterName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsError { get; set; }
    public bool IsPrinting { get; set; }
    public bool IsComplete { get; set; }
}