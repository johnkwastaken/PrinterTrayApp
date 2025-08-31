namespace PrinterTrayApp.Models;

public class PrintersResponse
{
    public int TotalPrinters { get; set; }
    public bool HasPrinterIssues { get; set; }
    public List<PrinterDetailDto> Printers { get; set; } = new();
    public PrintersSummary Summary { get; set; } = new();
}

public class PrinterDetailDto
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsOnline { get; set; }
    public string Status { get; set; } = "";
    public int StatusFlags { get; set; }
    public string Port { get; set; } = "";
    public string PortType { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Location { get; set; } = "";
    public string Comment { get; set; } = "";
    public uint JobCount { get; set; }
    public bool SupportsRaw { get; set; }
    public string[] SupportedPaperSizes { get; set; } = Array.Empty<string>();
    public bool IsShared { get; set; }
    public string? ShareName { get; set; }
}

public class PrintersSummary
{
    public int Total { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
    public int WithJobs { get; set; }
    public int RawCapable { get; set; }
}