namespace PrinterTrayApp;

public static class Constants
{
    public const int ApiPort = 9877;
    public const string ApiVersion = "0.1.0";
    public const string AppName = "Printer Tray App";
    public const string MutexName = "PrinterTrayApp_SingleInstance";
    
    public static class RefreshIntervals
    {
        public const int ActiveMilliseconds = 1000;  // 1 second when jobs active
        public const int IdleMilliseconds = 3000;    // 3 seconds when idle
        public const int CacheExpirySeconds = 30;    // 30 seconds cache expiry
    }
    
    public static class UI
    {
        public const int TrayIconSize = 16;
        public const string TrayIconText = "P";
        public const int FormWidth = 800;
        public const int FormHeight = 500;
        public const int ButtonWidth = 100;
        public const int ButtonHeight = 30;
        public const int BalloonTipTimeout = 3000;
    }
    
    public static class Logging
    {
        public const string TimeFormat = "HH:mm:ss";
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    }
    
    public static class PrinterDefaults
    {
        public const int MaxPayloadSizeMB = 2;
        public const string DefaultDataType = "RAW";
        public const string DefaultPrintProcessor = "WinPrint";
    }
}