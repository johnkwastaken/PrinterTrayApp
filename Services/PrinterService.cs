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
        RefreshPrinters(true);  // Log on initial load
        LoadMappings();
    }

    public List<PrinterInfo> GetAllPrinters()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds > 30)
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
                IntPtr pPrinterEnum = Marshal.AllocHGlobal((int)cbNeeded);
                try
                {
                    if (WinSpoolInterop.EnumPrinters(flags, null, 2, pPrinterEnum, cbNeeded, ref cbNeeded, ref cReturned))
                    {
                        IntPtr currentPrinter = pPrinterEnum;
                        int sizeOfStruct = Marshal.SizeOf(typeof(WinSpoolInterop.PRINTER_INFO_2));
                        
                        for (int i = 0; i < cReturned; i++)
                        {
                            var printerInfo = Marshal.PtrToStructure<WinSpoolInterop.PRINTER_INFO_2>(currentPrinter);
                            
                            var printer = new PrinterInfo
                            {
                                WindowsPrinterName = printerInfo.pPrinterName,
                                LogicalName = GetLogicalName(printerInfo.pPrinterName),
                                PortName = printerInfo.pPortName,
                                DriverName = printerInfo.pDriverName,
                                IsOnline = DeterminePrinterOnlineStatus(printerInfo.Status, printerInfo.Attributes),
                                HasError = (printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_ERROR) != 0,
                                IsPaused = (printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_PAUSED) != 0,
                                Status = GetDetailedStatusString(printerInfo.Status, printerInfo.Attributes),
                                JobCount = printerInfo.cJobs,
                                IsDefault = (printerInfo.Attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_DEFAULT) != 0
                            };
                            
                            _cachedPrinters.Add(printer);
                            currentPrinter = IntPtr.Add(currentPrinter, sizeOfStruct);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pPrinterEnum);
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

    private bool DeterminePrinterOnlineStatus(uint status, uint attributes)
    {
        // Check if printer is marked as work offline
        if ((attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
            return false;
            
        // If status is 0 or only has "waiting" or "processing" flags, it's likely ready
        if (status == 0) return true;
        
        // Check for problematic statuses
        if ((status & WinSpoolInterop.PRINTER_STATUS_OFFLINE) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_ERROR) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_NOT_AVAILABLE) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_DOOR_OPEN) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_USER_INTERVENTION) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_PAPER_OUT) != 0) return false;
        if ((status & WinSpoolInterop.PRINTER_STATUS_NO_TONER) != 0) return false;
        
        // These statuses are OK - printer is still "online"
        if ((status & WinSpoolInterop.PRINTER_STATUS_PRINTING) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_PROCESSING) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_WAITING) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_WARMING_UP) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_INITIALIZING) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_BUSY) != 0) return true;
        if ((status & WinSpoolInterop.PRINTER_STATUS_IO_ACTIVE) != 0) return true;
        
        return true;
    }

    private string GetDetailedStatusString(uint status, uint attributes)
    {
        // Check attributes first
        if ((attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
            return "Work Offline";
            
        // If status is 0, printer is ready
        if (status == 0)
            return "Ready";
        
        // Check for specific problems first
        if ((status & WinSpoolInterop.PRINTER_STATUS_PAPER_OUT) != 0) return "Paper Out";
        if ((status & WinSpoolInterop.PRINTER_STATUS_PAPER_JAM) != 0) return "Paper Jam";
        if ((status & WinSpoolInterop.PRINTER_STATUS_NO_TONER) != 0) return "No Toner";
        if ((status & WinSpoolInterop.PRINTER_STATUS_DOOR_OPEN) != 0) return "Door Open";
        if ((status & WinSpoolInterop.PRINTER_STATUS_USER_INTERVENTION) != 0) return "Needs Attention";
        if ((status & WinSpoolInterop.PRINTER_STATUS_ERROR) != 0) return "Error";
        if ((status & WinSpoolInterop.PRINTER_STATUS_OFFLINE) != 0) return "Offline";
        if ((status & WinSpoolInterop.PRINTER_STATUS_PAUSED) != 0) return "Paused";
        if ((status & WinSpoolInterop.PRINTER_STATUS_NOT_AVAILABLE) != 0) return "Not Available";
        if ((status & WinSpoolInterop.PRINTER_STATUS_OUT_OF_MEMORY) != 0) return "Out of Memory";
        
        // Check for activity statuses
        if ((status & WinSpoolInterop.PRINTER_STATUS_PRINTING) != 0) return "Printing";
        if ((status & WinSpoolInterop.PRINTER_STATUS_PROCESSING) != 0) return "Processing";
        if ((status & WinSpoolInterop.PRINTER_STATUS_BUSY) != 0) return "Busy";
        if ((status & WinSpoolInterop.PRINTER_STATUS_WAITING) != 0) return "Waiting";
        if ((status & WinSpoolInterop.PRINTER_STATUS_WARMING_UP) != 0) return "Warming Up";
        if ((status & WinSpoolInterop.PRINTER_STATUS_INITIALIZING) != 0) return "Initializing";
        if ((status & WinSpoolInterop.PRINTER_STATUS_IO_ACTIVE) != 0) return "Active";
        if ((status & WinSpoolInterop.PRINTER_STATUS_POWER_SAVE) != 0) return "Power Save";
        if ((status & WinSpoolInterop.PRINTER_STATUS_PENDING_DELETION) != 0) return "Deleting";
        
        // Check for low supplies
        if ((status & WinSpoolInterop.PRINTER_STATUS_TONER_LOW) != 0) return "Toner Low";
        if ((status & WinSpoolInterop.PRINTER_STATUS_OUTPUT_BIN_FULL) != 0) return "Output Bin Full";
        
        // Default to Ready if no specific status
        return "Ready";
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
}