namespace PrinterTrayApp.Models;

public class HealthResponse
{
    public bool Ok { get; set; }
    public string Version { get; set; } = "0.1.0";
    public List<string> Printers { get; set; } = new();
    public long UptimeSeconds { get; set; }
}