using System.Runtime.InteropServices;
using System.Text;

namespace PrinterTrayApp.Interop;

public static class WinSpoolInterop
{
    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool EnumPrinters(
        PrinterEnumFlags Flags,
        string? Name,
        uint Level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        ref uint pcbNeeded,
        ref uint pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool OpenPrinter(
        string pPrinterName,
        out IntPtr phPrinter,
        IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GetPrinter(
        IntPtr hPrinter,
        uint Level,
        IntPtr pPrinter,
        uint cbBuf,
        ref uint pcbNeeded);

    [Flags]
    public enum PrinterEnumFlags : uint
    {
        PRINTER_ENUM_LOCAL = 0x00000002,
        PRINTER_ENUM_CONNECTIONS = 0x00000004,
        PRINTER_ENUM_NAME = 0x00000008,
        PRINTER_ENUM_NETWORK = 0x00000040,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PRINTER_INFO_2
    {
        public string pServerName;
        public string pPrinterName;
        public string pShareName;
        public string pPortName;
        public string pDriverName;
        public string pComment;
        public string pLocation;
        public IntPtr pDevMode;
        public string pSepFile;
        public string pPrintProcessor;
        public string pDatatype;
        public string pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    public const uint PRINTER_STATUS_PAUSED = 0x00000001;
    public const uint PRINTER_STATUS_ERROR = 0x00000002;
    public const uint PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    public const uint PRINTER_STATUS_PAPER_JAM = 0x00000008;
    public const uint PRINTER_STATUS_PAPER_OUT = 0x00000010;
    public const uint PRINTER_STATUS_MANUAL_FEED = 0x00000020;
    public const uint PRINTER_STATUS_PAPER_PROBLEM = 0x00000040;
    public const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    public const uint PRINTER_STATUS_IO_ACTIVE = 0x00000100;
    public const uint PRINTER_STATUS_BUSY = 0x00000200;
    public const uint PRINTER_STATUS_PRINTING = 0x00000400;
    public const uint PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    public const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    public const uint PRINTER_STATUS_WAITING = 0x00002000;
    public const uint PRINTER_STATUS_PROCESSING = 0x00004000;
    public const uint PRINTER_STATUS_INITIALIZING = 0x00008000;
    public const uint PRINTER_STATUS_WARMING_UP = 0x00010000;
    public const uint PRINTER_STATUS_TONER_LOW = 0x00020000;
    public const uint PRINTER_STATUS_NO_TONER = 0x00040000;
    public const uint PRINTER_STATUS_PAGE_PUNT = 0x00080000;
    public const uint PRINTER_STATUS_USER_INTERVENTION = 0x00100000;
    public const uint PRINTER_STATUS_OUT_OF_MEMORY = 0x00200000;
    public const uint PRINTER_STATUS_DOOR_OPEN = 0x00400000;
    public const uint PRINTER_STATUS_SERVER_UNKNOWN = 0x00800000;
    public const uint PRINTER_STATUS_POWER_SAVE = 0x01000000;
    
    public const uint PRINTER_ATTRIBUTE_QUEUED = 0x00000001;
    public const uint PRINTER_ATTRIBUTE_DIRECT = 0x00000002;
    public const uint PRINTER_ATTRIBUTE_DEFAULT = 0x00000004;
    public const uint PRINTER_ATTRIBUTE_SHARED = 0x00000008;
    public const uint PRINTER_ATTRIBUTE_NETWORK = 0x00000010;
    public const uint PRINTER_ATTRIBUTE_HIDDEN = 0x00000020;
    public const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;
    public const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;
}