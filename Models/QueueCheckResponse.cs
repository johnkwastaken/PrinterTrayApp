namespace PrinterTrayApp.Models;

public class QueueCheckResponse
{
    public int TotalJobs { get; set; }
    public bool HasErrors { get; set; }
    public bool HasStuckJobs { get; set; }
    public List<QueueJobInfo> Jobs { get; set; } = new();
    public QueueSummary Summary { get; set; } = new();
}

public class QueueJobInfo
{
    public string? Guid { get; set; }
    public int SpoolerId { get; set; }
    public string PrinterName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? DocumentName { get; set; }
    public int Position { get; set; }
    public int PagesPrinted { get; set; }
    public int TotalPages { get; set; }
    public DateTime SubmittedAt { get; set; }
    public long AgeSeconds { get; set; }
    public string AgeFormatted { get; set; } = "";
    public bool IsStuck { get; set; }
    public bool IsError { get; set; }
}

public class QueueSummary
{
    public int Total { get; set; }
    public int Printing { get; set; }
    public int Queued { get; set; }
    public int Spooling { get; set; }
    public int Paused { get; set; }
    public int Error { get; set; }
    public int StuckCount { get; set; }
    public long OldestJobAge { get; set; }
    public string OldestJobAgeFormatted { get; set; } = "";
}