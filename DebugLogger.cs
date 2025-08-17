using System;
using System.IO;

namespace PrinterTrayApp;

public static class DebugLogger
{
    private static readonly string LogFile = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
        "debug.log"
    );
    
    static DebugLogger()
    {
        try
        {
            File.WriteAllText(LogFile, $"=== Debug Log Started: {DateTime.Now} ===\n");
        }
        catch { }
    }
    
    public static void Log(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}\n";
            File.AppendAllText(LogFile, logMessage);
            
            // Also try console
            Console.WriteLine(logMessage);
        }
        catch { }
    }
    
    public static void LogError(string message, Exception? ex = null)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] ERROR: {message}\n";
            if (ex != null)
            {
                logMessage += $"  Exception: {ex.GetType().Name}: {ex.Message}\n";
                logMessage += $"  Stack: {ex.StackTrace}\n";
            }
            File.AppendAllText(LogFile, logMessage);
            
            // Also try console
            Console.WriteLine(logMessage);
        }
        catch { }
    }
}