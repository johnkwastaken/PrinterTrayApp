using PrinterTrayApp.Interop;
using PrinterTrayApp.Models;
using System.Runtime.InteropServices;

namespace PrinterTrayApp.Services;

/// <summary>
/// Service for discovering and managing Windows printers
/// 
/// PURPOSE:
/// - Discovers all available printers using Windows Print Spooler API
/// - Filters out virtual/document printers (PDF, XPS, OneNote, etc.)
/// - Monitors printer status (online, offline, errors, paper out, etc.)
/// - Provides logical name mapping for POS system compatibility
/// - Caches printer information to reduce API calls
/// 
/// ARCHITECTURE:
/// - Uses P/Invoke to call Windows Print Spooler API (winspool.drv)
/// - Wraps native printer enumeration in safe handles
/// - Provides status mapping from cryptic Windows codes to readable text
/// - Supports printer mapping from logical names to Windows names
/// 
/// PRINTER DISCOVERY:
/// - Enumerates LOCAL printers (directly attached)
/// - Enumerates NETWORK printers (shared/mapped)
/// - Excludes virtual printers automatically
/// - Identifies printer capabilities (RAW printing support)
/// 
/// STATUS MONITORING:
/// - Maps 20+ Windows printer status codes
/// - Categorizes by severity: Error, Warning, Active, Ready
/// - Provides human-readable status messages
/// - Tracks job counts and default printer
/// </summary>
public class PrinterService
{
    private List<PrinterInfo> _cachedPrinters = new();  // Cached list of discovered printers
    private Dictionary<string, string> _printerMappings = new();  // Logical name -> Windows name mappings
    private DateTime _lastRefresh = DateTime.MinValue;  // Last cache refresh time

    /// <summary>
    /// Initializes the printer service and discovers available printers
    /// Loads any configured printer mappings from printers.json
    /// </summary>
    public PrinterService()
    {
        try
        {
            RefreshPrinters(true);  // Log on initial load
            LoadMappings();  // Load logical name mappings from config
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"PrinterService initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all discovered printers, refreshing cache if expired
    /// Cache expires after 30 seconds to detect newly added printers
    /// </summary>
    public List<PrinterInfo> GetAllPrinters()
    {
        // Auto-refresh if cache is stale
        if ((DateTime.Now - _lastRefresh).TotalSeconds > Constants.RefreshIntervals.CacheExpirySeconds)
        {
            RefreshPrinters();
        }
        return _cachedPrinters;
    }

    /// <summary>
    /// Refreshes the printer cache by querying Windows Print Spooler
    /// Uses EnumPrinters API to get detailed printer information
    /// </summary>
    /// <param name="logToConsole">Whether to log discovered printers to console</param>
    public void RefreshPrinters(bool logToConsole = false)
    {
        _cachedPrinters.Clear();

        try
        {
            // Enumerate both local (USB/parallel) and network printers
            var flags = WinSpoolInterop.PrinterEnumFlags.PRINTER_ENUM_LOCAL |
                       WinSpoolInterop.PrinterEnumFlags.PRINTER_ENUM_CONNECTIONS;

            uint cbNeeded = 0;   // Bytes needed for printer data
            uint cReturned = 0;  // Number of printers found

            // First call: Get required buffer size
            // Pass null buffer to determine how much memory we need
            WinSpoolInterop.EnumPrinters(flags, null, 2, IntPtr.Zero, 0, ref cbNeeded, ref cReturned);

            if (cbNeeded > 0)
            {
                // Allocate unmanaged memory for printer data
                using (var safeHandle = SafePrinterEnumHandle.Allocate((int)cbNeeded))
                {
                    // Second call: Get actual printer data
                    if (WinSpoolInterop.EnumPrinters(flags, null, 2, safeHandle.DangerousGetHandle(), cbNeeded, ref cbNeeded, ref cReturned))
                    {
                        // Parse the returned data - it's an array of PRINTER_INFO_2 structures
                        IntPtr currentPrinter = safeHandle.DangerousGetHandle();
                        int sizeOfStruct = Marshal.SizeOf(typeof(WinSpoolInterop.PRINTER_INFO_2));

                        // Process each printer in the array
                        for (int i = 0; i < cReturned; i++)
                        {
                            // Marshal the native structure to managed code
                            var printerInfo = Marshal.PtrToStructure<WinSpoolInterop.PRINTER_INFO_2>(currentPrinter);

                            // Skip printers that are being removed from the system
                            if ((printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_PENDING_DELETION) != 0)
                            {
                                currentPrinter = IntPtr.Add(currentPrinter, sizeOfStruct);
                                continue;
                            }

                            // Decode printer status from Windows status flags
                            var statusInfo = GetPrinterStatus(printerInfo.Status, printerInfo.Attributes);

                            // Filter out virtual/document printers
                            // These don't support RAW printing needed for ESC/POS
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

                            // Create PrinterInfo object with all relevant details
                            var printer = new PrinterInfo
                            {
                                WindowsPrinterName = printerInfo.pPrinterName,  // Actual Windows name
                                LogicalName = GetLogicalName(printerInfo.pPrinterName),  // Mapped name if configured
                                PortName = printerInfo.pPortName,  // Port (e.g., USB001, IP address)
                                DriverName = printerInfo.pDriverName,  // Driver name
                                IsOnline = statusInfo.IsOnline,  // Whether printer is available
                                HasError = statusInfo.Severity == StatusSeverity.Error,  // Error state
                                IsPaused = (printerInfo.Status & WinSpoolInterop.PRINTER_STATUS_PAUSED) != 0,
                                Status = statusInfo.DisplayText,  // Human-readable status
                                JobCount = printerInfo.cJobs,  // Pending print jobs
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

    /// <summary>
    /// Maps Windows printer name to logical name from configuration
    /// Allows POS to use friendly names like "kitchen" instead of "EPSON_TM_T88V_192_168_1_100"
    /// </summary>
    private string GetLogicalName(string windowsPrinterName)
    {
        // Check if this Windows name has a logical mapping
        foreach (var mapping in _printerMappings)
        {
            if (mapping.Value.Equals(windowsPrinterName, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Key;  // Return the logical name
            }
        }
        return windowsPrinterName;  // No mapping, use Windows name
    }

    /// <summary>
    /// Maps Windows printer status codes to human-readable messages
    /// Organized by severity: Error > Warning > Active > Ready
    /// </summary>
    private static readonly Dictionary<uint, PrinterStatusInfo> StatusMap = new()
    {
        // Error conditions (highest priority) - printer cannot print
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

    /// <summary>
    /// Decodes Windows printer status flags into human-readable status
    /// Checks multiple flags in priority order to determine most important status
    /// </summary>
    private static PrinterStatusInfo GetPrinterStatus(uint status, uint attributes)
    {
        // Check work offline attribute first - user manually set printer offline
        if ((attributes & WinSpoolInterop.PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
            return new PrinterStatusInfo { IsOnline = false, DisplayText = "Work Offline", Severity = StatusSeverity.Error };

        // Status 0 means ready - no issues
        if (status == 0)
            return new PrinterStatusInfo { IsOnline = true, DisplayText = "Ready", Severity = StatusSeverity.Ready };

        // Check status flags in priority order
        // Report most severe issue first (e.g., "Paper Out" over "Busy")
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

    /// <summary>
    /// Loads printer name mappings from printers.json configuration file
    /// Allows mapping logical names (e.g., "kitchen") to Windows printer names
    /// </summary>
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

    /// <summary>
    /// Resolves a logical printer name to PrinterInfo object
    /// Used to find printer details from POS-provided name
    /// </summary>
    public PrinterInfo? ResolvePrinter(string logicalName)
    {
        return _cachedPrinters.FirstOrDefault(p =>
            p.LogicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets list of Windows printer names for all discovered printers
    /// Used by API endpoints to return available printers
    /// </summary>
    public List<string> GetPrinterNames()
    {
        return _cachedPrinters.Select(p => p.WindowsPrinterName).ToList();
    }

    /// <summary>
    /// Checks if a printer exists by Windows name or logical name
    /// Auto-refreshes cache if expired to detect newly added printers
    /// </summary>
    public bool PrinterExists(string printerName)
    {
        // Refresh cache if stale
        if ((DateTime.Now - _lastRefresh).TotalSeconds > Constants.RefreshIntervals.CacheExpirySeconds)
        {
            RefreshPrinters();
        }

        // Check both Windows name and logical name
        return _cachedPrinters.Any(p =>
            p.WindowsPrinterName.Equals(printerName, StringComparison.OrdinalIgnoreCase) ||
            p.LogicalName.Equals(printerName, StringComparison.OrdinalIgnoreCase));
    }
}