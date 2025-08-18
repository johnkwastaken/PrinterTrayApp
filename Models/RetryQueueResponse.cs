namespace PrinterTrayApp.Models;

public class RetryQueueResponse
{
    public bool Success { get; set; }
    public int RetriedCount { get; set; }
    public List<RetriedJobInfo> RetriedJobs { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class RetriedJobInfo
{
    public string? Guid { get; set; }
    public int SpoolerId { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

public class RetryQueueRequest
{
    public string PrinterName { get; set; } = "";
}