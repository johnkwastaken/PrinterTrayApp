using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PrinterTrayApp.Services;

public static class PrintDirect
{
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
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string pDocName;
        public string? pOutputFile;
        public string? pDatatype;
    }
    
    public static int Print(string printerName, string docName, string docType, string data)
    {
        IntPtr hPrinter = IntPtr.Zero;
        int jobId = 0;
        
        try
        {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                throw new Exception($"Failed to open printer '{printerName}'. Error: {Marshal.GetLastWin32Error()}");
            }
            
            var docInfo = new DOC_INFO_1
            {
                pDocName = docName,
                pDatatype = docType == "RAW" ? null : docType
            };
            
            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to start document. Error: {error}");
            }
            
            jobId = GetLastJobId(hPrinter);
            
            if (!StartPagePrinter(hPrinter))
            {
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to start page. Error: {Marshal.GetLastWin32Error()}");
            }
            
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            
            if (!WritePrinter(hPrinter, bytes, bytes.Length, out int written))
            {
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to write to printer. Error: {Marshal.GetLastWin32Error()}");
            }
            
            if (!EndPagePrinter(hPrinter))
            {
                EndDocPrinter(hPrinter);
                throw new Exception($"Failed to end page. Error: {Marshal.GetLastWin32Error()}");
            }
            
            if (!EndDocPrinter(hPrinter))
            {
                throw new Exception($"Failed to end document. Error: {Marshal.GetLastWin32Error()}");
            }
            
            return jobId;
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
    private static extern bool GetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pPrinter, int cbBuf, out int pcbNeeded);
    
    private static int GetLastJobId(IntPtr hPrinter)
    {
        int needed = 0;
        GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out needed);
        
        if (needed <= 0) return 0;
        
        IntPtr pPrinter = Marshal.AllocHGlobal(needed);
        try
        {
            if (GetPrinter(hPrinter, 2, pPrinter, needed, out _))
            {
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
                        
                        jobs[i] = new PrintJobInfo
                        {
                            JobId = jobId,
                            Status = (JobStatus)status
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