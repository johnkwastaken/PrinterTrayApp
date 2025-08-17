# Test script to check printer configuration
$printer = Get-WmiObject Win32_Printer | Where-Object {$_.Name -eq 'passkitchen'}

if ($printer) {
    Write-Host "=== PRINTER FOUND ===" -ForegroundColor Green
    Write-Host "Name: $($printer.Name)"
    Write-Host "DriverName: $($printer.DriverName)"
    Write-Host "PortName: $($printer.PortName)"
    Write-Host "PrintProcessor: $($printer.PrintProcessor)"
    Write-Host "PrintJobDataType: $($printer.PrintJobDataType)"
    Write-Host "Direct: $($printer.Direct)"
    Write-Host "RawOnly: $($printer.RawOnly)"
    Write-Host "Attributes: $($printer.Attributes)"
    Write-Host "WorkOffline: $($printer.WorkOffline)"
    Write-Host "Status: $($printer.Status)"
    Write-Host "PrinterStatus: $($printer.PrinterStatus)"
    Write-Host ""
    
    # Check port type
    Write-Host "=== PORT DETAILS ===" -ForegroundColor Yellow
    $tcpPort = Get-WmiObject Win32_TCPIPPrinterPort | Where-Object {$_.Name -eq $printer.PortName}
    if ($tcpPort) {
        Write-Host "TCP/IP Port: $($tcpPort.Name)"
        Write-Host "Host Address: $($tcpPort.HostAddress)"
    } else {
        Write-Host "Port Type: Not TCP/IP (likely USB/COM/WSD)"
    }
    
    # Test different datatypes
    Write-Host ""
    Write-Host "=== TESTING PRINT WITH DIFFERENT DATATYPES ===" -ForegroundColor Cyan
    
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class PrinterTest {
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 pDocInfo);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);
    
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(IntPtr hPrinter, byte[] pBuf, int cbBuf, out int pcWritten);
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DOC_INFO_1 {
        public string pDocName;
        public string pOutputFile;
        public string pDatatype;
    }
    
    public static bool TestPrint(string printerName, string datatype) {
        IntPtr hPrinter = IntPtr.Zero;
        
        try {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero)) {
                Console.WriteLine("Failed to open printer. Error: " + Marshal.GetLastWin32Error());
                return false;
            }
            
            var docInfo = new DOC_INFO_1();
            docInfo.pDocName = "Test";
            docInfo.pDatatype = datatype;
            
            if (!StartDocPrinter(hPrinter, 1, ref docInfo)) {
                Console.WriteLine("Failed to start doc with datatype '" + datatype + "'. Error: " + Marshal.GetLastWin32Error());
                return false;
            }
            
            if (!StartPagePrinter(hPrinter)) {
                Console.WriteLine("Failed to start page. Error: " + Marshal.GetLastWin32Error());
                EndDocPrinter(hPrinter);
                return false;
            }
            
            // Simple ESC/POS test
            byte[] init = new byte[] { 0x1B, 0x40 };
            byte[] text = Encoding.ASCII.GetBytes("TEST PRINT\n\n\n");
            byte[] cut = new byte[] { 0x1D, 0x56, 0x42, 0x00 };
            
            int written = 0;
            WritePrinter(hPrinter, init, init.Length, out written);
            WritePrinter(hPrinter, text, text.Length, out written);
            WritePrinter(hPrinter, cut, cut.Length, out written);
            
            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
            
            Console.WriteLine("SUCCESS with datatype: " + datatype);
            return true;
        }
        catch (Exception ex) {
            Console.WriteLine("Exception: " + ex.Message);
            return false;
        }
        finally {
            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
    }
}
"@
    
    # Test different datatypes
    $datatypes = @("RAW", "TEXT", "XPS_PASS", "", $null)
    
    foreach ($dt in $datatypes) {
        $displayDt = if ($dt -eq $null) { "null" } elseif ($dt -eq "") { "empty string" } else { $dt }
        Write-Host "Testing with datatype: $displayDt" -ForegroundColor White
        
        if ($dt -eq $null) {
            # Special handling for null
            [PrinterTest]::TestPrint($printer.Name, [NullString]::Value)
        } else {
            [PrinterTest]::TestPrint($printer.Name, $dt)
        }
        Write-Host ""
    }
    
} else {
    Write-Host "Printer 'passkitchen' not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Available printers:" -ForegroundColor Yellow
    Get-WmiObject Win32_Printer | Select-Object Name, DriverName, PortName | Format-Table
}