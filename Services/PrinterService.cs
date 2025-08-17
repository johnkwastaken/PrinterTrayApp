using System.Runtime.InteropServices;
using PrinterTrayApp.Interop;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Services;

public class PrinterService
{
    private List<PrinterInfo> _cachedPrinters = new();
    private Dictionary<string, string> _printerMappings = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    public PrinterService()
    {
        try
        {
            RefreshPrinters(true);  // Log on initial load
            LoadMappings();
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"PrinterService initialization failed: {ex.Message}");
        }
    }

    public List<PrinterInfo> GetAllPrinters()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds > Constants.RefreshIntervals.CacheExpirySeconds)
        {
            RefreshPrinters();
        }
        return _cachedPrinters;
    }

    public void RefreshPrinters(bool logToConsole = false)
    {
        _cachedPrinters.Clear();
        
        try
        {
            var flags = WinSpoolInterop.PrinterEnumFlags.PRINTER_ENUM_LOCAL | 
                       WinSpoolInterop.PrinterEnumFlags.PRINTER_ENUM_CONNECTIONS;
            
            uint cbNeeded = 0;
            uint cReturned = 0;
            
            WinSpoolInterop.EnumPrinters(flags, null, 2, IntPtr.Zero, 0, ref cbNeeded, ref cReturned);
            
            if (cbNeeded > 0)
            {
                using (var safeHandle = SafePrinterEnumHandle.Allocate((int)cbNeeded))
                {
                    if (WinSpoolInterop.EnumPrinters(flags, null, 2, safeHandle.DangerousGetHandle(), cbNeeded, ref cbNeeded, ref cReturned))
                    {
                        IntPtr currentPrinter = safeHandle.DangerousGetHandle();
                        int sizeOfStruct = Marshal.SizeOf(typeof(WinSpoolInterop.PRINTER_INFO_2));
                        
                        for (int i = 0; i < cReturned; i++)
                        {
                            var printerInfo = Marshal.PtrToStructure<WinSpoolInterop.PRINTER_INFO_2>(currentPrinter);
                            
                            // Skip printers that are pending deletion
                            if ((printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_PENDING_DELETION) != 0)
                            {
                                currentPrinter = IntPtr.Add(currentPrinter, sizeOfStruct);
                                continue;
                            }
                            
                            var statusInfo = GetPrinterStatus(printerInfo.Status, printerInfo.Attributes);
                            
                            // Skip virtual/document printers
                            var printerName = printerInfo.pPrinterName ?? "";
                            if (printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase) ||
                                printerName.Contains("XPS", StringComparison.OrdinalIgnoreCase) ||
                                printerName.Contains("OneNote", StringComparison.OrdinalIgnoreCase) ||
                                printerName.Contains("Fax", StringComparison.OrdinalIgnoreCase) ||
                                printerName.Contains("Microsoft Print", StringComparison.OrdinalIgnoreCase))
                            {
                                currentPrinter = IntPtr.Add(currentPrinter, sizeOfStruct);
                                continue;
                            }
                            
                            var printer = new PrinterInfo
                            {
                                WindowsPrinterName = printerInfo.pPrinterName,
                                LogicalName = GetLogicalName(printerInfo.pPrinterName),
                                PortName = printerInfo.pPortName,
                                DriverName = printerInfo.pDriverName,
                                IsOnline = statusInfo.IsOnline,
                                HasError = statusInfo.Severity == StatusSeverity.Error,
                                IsPaused = (printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_PAUSED) != 0,
                                Status = statusInfo.DisplayText,
                                JobCount = printerInfo.cJobs,
                                IsDefault = (printerInfo.Attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_DEFAULT) != 0
                            };
                            
                            _cachedPrinters.Add(printer);
                            currentPrinter = IntPtr.Add(currentPrinter, sizeOfStruct);
                        }
                    }
                }
            }
            
            _lastRefresh = DateTime.Now;
            
            // Only log if explicitly requested (initial load or manual refresh)
            if (logToConsole)
            {
                ConsoleWindow.WriteLine($"Discovered {_cachedPrinters.Count} printer(s)");
                
                foreach (var printer in _cachedPrinters)
                {
                    ConsoleWindow.WriteLine($"  - {printer.WindowsPrinterName} [{printer.Status}]");
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Failed to enumerate printers: {ex.Message}");
        }
    }

    private string GetLogicalName(string windowsPrinterName)
    {
        foreach (var mapping in _printerMappings)
        {
            if (mapping.Value.Equals(windowsPrinterName, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Key;
            }
        }
        return windowsPrinterName;
    }

    private static readonly Dictionary<uint, PrinterStatusInfo> StatusMap = new()
    {
        // Error conditions (highest priority)
        { WinSpoolInterop.PRINTER_STATUS_PAPER_OUT, new() { IsOnline = false, DisplayText = "Paper Out", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_PAPER_JAM, new() { IsOnline = false, DisplayText = "Paper Jam", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_NO_TONER, new() { IsOnline = false, DisplayText = "No Toner", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_DOOR_OPEN, new() { IsOnline = false, DisplayText = "Door Open", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_USER_INTERVENTION, new() { IsOnline = false, DisplayText = "Needs Attention", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_ERROR, new() { IsOnline = false, DisplayText = "Error", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_OFFLINE, new() { IsOnline = false, DisplayText = "Offline", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_NOT_AVAILABLE, new() { IsOnline = false, DisplayText = "Not Available", Severity = StatusSeverity.Error } },
        { WinSpoolInterop.PRINTER_STATUS_OUT_OF_MEMORY, new() { IsOnline = false, DisplayText = "Out of Memory", Severity = StatusSeverity.Error } },
        
        // Warning conditions
        { WinSpoolInterop.PRINTER_STATUS_PAUSED, new() { IsOnline = true, DisplayText = "Paused", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_TONER_LOW, new() { IsOnline = true, DisplayText = "Toner Low", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_OUTPUT_BIN_FULL, new() { IsOnline = true, DisplayText = "Output Bin Full", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_PAPER_PROBLEM, new() { IsOnline = true, DisplayText = "Paper Problem", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_WARMING_UP, new() { IsOnline = true, DisplayText = "Warming Up", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_INITIALIZING, new() { IsOnline = true, DisplayText = "Initializing", Severity = StatusSeverity.Warning } },
        { WinSpoolInterop.PRINTER_STATUS_POWER_SAVE, new() { IsOnline = true, DisplayText = "Power Save", Severity = StatusSeverity.Warning } },
        
        // Active conditions
        { WinSpoolInterop.PRINTER_STATUS_PRINTING, new() { IsOnline = true, DisplayText = "Printing", Severity = StatusSeverity.Active } },
        { WinSpoolInterop.PRINTER_STATUS_PROCESSING, new() { IsOnline = true, DisplayText = "Processing", Severity = StatusSeverity.Active } },
        { WinSpoolInterop.PRINTER_STATUS_BUSY, new() { IsOnline = true, DisplayText = "Busy", Severity = StatusSeverity.Active } },
        { WinSpoolInterop.PRINTER_STATUS_IO_ACTIVE, new() { IsOnline = true, DisplayText = "Active", Severity = StatusSeverity.Active } },
        
        // Ready conditions
        { WinSpoolInterop.PRINTER_STATUS_WAITING, new() { IsOnline = true, DisplayText = "Waiting", Severity = StatusSeverity.Ready } },
    };

    private static PrinterStatusInfo GetPrinterStatus(uint status, uint attributes)
    {
        // Check work offline attribute first
        if ((attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
            return new PrinterStatusInfo { IsOnline = false, DisplayText = "Work Offline", Severity = StatusSeverity.Error };
        
        // Status 0 means ready
        if (status == 0)
            return new PrinterStatusInfo { IsOnline = true, DisplayText = "Ready", Severity = StatusSeverity.Ready };
        
        // Check status flags in priority order (errors first, then warnings, then active)
        foreach (var severity in new[] { StatusSeverity.Error, StatusSeverity.Warning, StatusSeverity.Active, StatusSeverity.Ready })
        {
            foreach (var kvp in StatusMap.Where(x => x.Value.Severity == severity))
            {
                if ((status & kvp.Key) != 0)
                    return kvp.Value;
            }
        }
        
        // Default to ready if no specific status matched
        return new PrinterStatusInfo { IsOnline = true, DisplayText = "Ready", Severity = StatusSeverity.Ready };
    }

    private void LoadMappings()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printers.json");
        
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<PrinterConfiguration>(json);
                
                if (config?.Mappings != null)
                {
                    _printerMappings.Clear();
                    foreach (var mapping in config.Mappings)
                    {
                        if (!string.IsNullOrEmpty(mapping.LogicalName) && !string.IsNullOrEmpty(mapping.WindowsPrinterName))
                        {
                            _printerMappings[mapping.LogicalName] = mapping.WindowsPrinterName;
                        }
                    }
                    
                    ConsoleWindow.WriteLine($"Loaded {_printerMappings.Count} printer mapping(s) from config");
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Failed to load printer mappings: {ex.Message}");
            }
        }
    }

    public PrinterInfo? ResolvePrinter(string logicalName)
    {
        return _cachedPrinters.FirstOrDefault(p => 
            p.LogicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
    }

    public List<string> GetPrinterNames()
    {
        return _cachedPrinters.Select(p => p.WindowsPrinterName).ToList();
    }
    
    public bool PrinterExists(string printerName)
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds > Constants.RefreshIntervals.CacheExpirySeconds)
        {
            RefreshPrinters();
        }
        
        return _cachedPrinters.Any(p => 
            p.WindowsPrinterName.Equals(printerName, StringComparison.OrdinalIgnoreCase) ||
            p.LogicalName.Equals(printerName, StringComparison.OrdinalIgnoreCase));
    }
}