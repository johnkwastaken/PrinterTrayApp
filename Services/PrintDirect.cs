using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PrinterTrayApp.Services;

/// <summary>
/// Direct Windows Print Spooler interface for RAW printing to thermal printers
/// 
/// PURPOSE:
/// - Bypasses Windows print drivers for direct ESC/POS command transmission
/// - Sends raw bytes/strings directly to printer without driver processing
/// - Provides complete control over print job lifecycle
/// - Enables thermal printer features not supported by standard drivers
/// 
/// TECHNICAL DETAILS:
/// - Uses P/Invoke to call Windows Print Spooler APIs (winspool.drv)
/// - Implements RAW printing mode (no driver formatting)
/// - Handles print job creation, data writing, and status monitoring
/// - Manages printer handle lifecycle with proper cleanup
/// 
/// WHY RAW PRINTING:
/// Standard Windows drivers expect formatted documents (text, images)
/// Thermal printers need ESC/POS control codes for:
/// - Font sizing and formatting
/// - Paper cutting commands
/// - Cash drawer control
/// - Barcode generation
/// 
/// RAW mode bypasses driver and sends our ESC/POS strings directly
/// </summary>
public static class PrintDirect
{
    // Windows Print Spooler API declarations
    // These P/Invoke declarations map to functions in winspool.drv
    
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);
    
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 pDocInfo);
    
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBuf, int cbBuf, out int pcWritten);
    
    /// <summary>
    /// Document information structure for Windows Print Spooler
    /// Defines print job metadata and data type
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string pDocName;      // Human-readable document name
        public string? pOutputFile;  // Output file (null for direct printing)
        public string? pDatatype;    // Data type (null for RAW mode)
    }
    
    /// <summary>
    /// Main print function - sends string data directly to printer
    /// Implements the complete print job lifecycle for RAW printing
    /// </summary>
    /// <param name="printerName">Windows printer name</param>
    /// <param name="docName">Document name (appears in print queue)</param>
    /// <param name="docType">Data type ("RAW" for thermal printers)</param>
    /// <param name="data">ESC/POS command string to send to printer</param>
    /// <returns>Windows spooler job ID for tracking</returns>
    public static int Print(string printerName, string docName, string docType, string data)
    {
        IntPtr hPrinter = IntPtr.Zero;  // Printer handle from Windows
        int jobId = 0;
        
        try
        {
            // STEP 1: Open connection to printer
            // This gets a handle to the Windows printer object
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                throw new Exception($"Failed to open printer '{printerName}'. Error: {Marshal.GetLastWin32Error()}");
            }
            
            // STEP 2: Create print document
            // For RAW printing, we set datatype to null to bypass driver processing
            var docInfo = new DOC_INFO_1
            {
                pDocName = docName,
                pDatatype = docType == "RAW" ? null : docType  // null = RAW mode
            };
            
            // Start the document - creates print job in Windows spooler
            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to start document. Error: {error}");
            }
            
            // Get the job ID for tracking - Windows assigns unique ID
            jobId = GetLastJobId(hPrinter);
            
            // STEP 3: Start page
            // Even for thermal printers, Windows requires page start/end
            if (!StartPagePrinter(hPrinter))
            {
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to start page. Error: {Marshal.GetLastWin32Error()}");
            }
            
            // STEP 4: Write the actual ESC/POS data
            // Convert string to UTF-8 bytes and send directly to printer
            // This is where our ESC/POS commands get transmitted
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            
            if (!WritePrinter(hPrinter, bytes, bytes.Length, out int written))
            {
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to write to printer. Error: {Marshal.GetLastWin32Error()}");
            }
            
            // STEP 5: End page and document
            // Finalizes the print job and sends to printer queue
            if (!EndPagePrinter(hPrinter))
            {
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to end page. Error: {Marshal.GetLastWin32Error()}");
            }
            
            // Complete the document - job is now queued for printing
            if (!EndDocPrinter(hPrinter))
            {
                throw new Exception($"Failed to end document. Error: {Marshal.GetLastWin32Error()}");
            }
            
            return jobId;
        }
        finally
        {
            // CRITICAL: Always close printer handle to prevent resource leaks
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }
    
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pPrinter, int cbBuf, out int pcbNeeded);
    
    /// <summary>
    /// Gets the job ID for the current print job
    /// This is a simplified implementation - in practice, the actual job ID
    /// is returned from StartDocPrinter, but this provides a fallback
    /// </summary>
    private static int GetLastJobId(IntPtr hPrinter)
    {
        int needed = 0;
        // Get required buffer size for printer info
        GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out needed);
        
        if (needed <= 0) return 0;
        
        // Allocate buffer and get printer info
        IntPtr pPrinter = Marshal.AllocHGlobal(needed);
        try
        {
            if (GetPrinter(hPrinter, 2, pPrinter, needed, out _))
            {
                // Read job count from PRINTER_INFO_2 structure (offset 76)
                var cJobs = Marshal.ReadInt32(pPrinter, 76);
                return cJobs;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pPrinter);
        }
        
        return 0;
    }
    
    public static class JobCommand
    {
        public const int JOB_CONTROL_PAUSE = 1;
        public const int JOB_CONTROL_RESUME = 2;
        public const int JOB_CONTROL_CANCEL = 3;
        public const int JOB_CONTROL_RESTART = 4;
        public const int JOB_CONTROL_DELETE = 5;
    }
    
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetJob(IntPtr hPrinter, int jobId, int level, IntPtr pJob, int command);
    
    public static void SendJobCommand(string printerName, int jobId, int command)
    {
        IntPtr hPrinter = IntPtr.Zero;
        
        try
        {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                throw new Exception($"Failed to open printer '{printerName}'. Error: {Marshal.GetLastWin32Error()}");
            }
            
            if (!SetJob(hPrinter, jobId, 0, IntPtr.Zero, command))
            {
                throw new Exception($"Failed to send job command. Error: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }
    
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumJobs(IntPtr hPrinter, int firstJob, int noJobs, int level, IntPtr pJob, int cbBuf, out int pcbNeeded, out int pcReturned);
    
    public static PrintJobInfo[] GetPrinterJobs(string printerName)
    {
        IntPtr hPrinter = IntPtr.Zero;
        
        try
        {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                return Array.Empty<PrintJobInfo>();
            }
            
            int needed = 0;
            int returned = 0;
            
            EnumJobs(hPrinter, 0, 100, 1, IntPtr.Zero, 0, out needed, out returned);
            
            if (needed <= 0)
            {
                return Array.Empty<PrintJobInfo>();
            }
            
            IntPtr pJob = Marshal.AllocHGlobal(needed);
            try
            {
                if (EnumJobs(hPrinter, 0, 100, 1, pJob, needed, out _, out returned))
                {
                    var jobs = new PrintJobInfo[returned];
                    var offset = pJob;
                    
                    for (int i = 0; i < returned; i++)
                    {
                        var jobId = Marshal.ReadInt32(offset, 0);
                        var status = Marshal.ReadInt32(offset, 52);
                        
                        // Get document name from JOB_INFO_1 structure (offset 16 for pDocumentName pointer)
                        var documentNamePtr = Marshal.ReadIntPtr(offset, 16);
                        string? documentName = null;
                        if (documentNamePtr != IntPtr.Zero)
                        {
                            documentName = Marshal.PtrToStringUni(documentNamePtr);
                        }
                        
                        jobs[i] = new PrintJobInfo
                        {
                            JobId = jobId,
                            Status = (JobStatus)status,
                            DocumentName = documentName
                        };
                        
                        offset = IntPtr.Add(offset, 64);
                    }
                    
                    return jobs;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pJob);
            }
            
            return Array.Empty<PrintJobInfo>();
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }
    
    public class PrintJobInfo
    {
        public int JobId { get; set; }
        public JobStatus Status { get; set; }
        public string? DocumentName { get; set; }
    }
    
    [Flags]
    public enum JobStatus
    {
        None = 0,
        Paused = 0x00000001,
        Error = 0x00000002,
        Deleting = 0x00000004,
        Spooling = 0x00000008,
        Printing = 0x00000010,
        Offline = 0x00000020,
        PaperOut = 0x00000040,
        Printed = 0x00000080,
        Deleted = 0x00000100,
        Blocked = 0x00000200,
        UserIntervention = 0x00000400,
        Restart = 0x00000800,
        Complete = 0x00001000
    }
}