namespace PrinterTrayApp.Models;

public class PrinterConfiguration
{
    public List<PrinterMapping> Mappings { get; set; } = new();
}

public class PrinterMapping
{
    public string LogicalName { get; set; } = string.Empty;
    public string WindowsPrinterName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}