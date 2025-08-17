using System.Runtime.InteropServices;

namespace PrinterTrayApp;

public static class ConsoleWindow
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleTitle(string lpConsoleTitle);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static IntPtr _consoleHandle = IntPtr.Zero;
    private static bool _consoleAllocated = false;

    public static void Toggle()
    {
        if (!_consoleAllocated)
        {
            AllocConsole();
            _consoleAllocated = true;
            _consoleHandle = GetConsoleWindow();
            
            SetConsoleTitle($"{Constants.AppName} - Console");
            InitializeConsole();
        }
        else
        {
            _consoleHandle = GetConsoleWindow();
        }

        if (_consoleHandle != IntPtr.Zero)
        {
            var isVisible = IsWindowVisible(_consoleHandle);
            ShowWindow(_consoleHandle, isVisible ? SW_HIDE : SW_SHOW);
        }
    }

    private static void InitializeConsole()
    {
        Console.WriteLine("=================================");
        Console.WriteLine($"{Constants.AppName} - Console Output");
        Console.WriteLine("=================================");
        Console.WriteLine($"Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"API Server: http://127.0.0.1:{Constants.ApiPort}");
        Console.WriteLine();
        Console.WriteLine("This console shows live server logs.");
        Console.WriteLine("You can close this window anytime - the app keeps running in tray.");
        Console.WriteLine("=================================");
        Console.WriteLine();
    }

    public static void WriteLine(string message)
    {
        if (!_consoleAllocated) return;
        
        var timestamp = DateTime.Now.ToString(Constants.Logging.TimeFormat);
        Console.WriteLine($"[{timestamp}] {message}");
    }

    public static void WriteError(string message)
    {
        if (!_consoleAllocated) return;
        
        var timestamp = DateTime.Now.ToString(Constants.Logging.TimeFormat);
        var oldColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{timestamp}] ERROR: {message}");
        Console.ForegroundColor = oldColor;
    }

    public static void Hide()
    {
        if (_consoleHandle != IntPtr.Zero)
        {
            ShowWindow(_consoleHandle, SW_HIDE);
        }
    }

    public static void Show()
    {
        if (!_consoleAllocated)
        {
            Toggle();
        }
        else if (_consoleHandle != IntPtr.Zero)
        {
            ShowWindow(_consoleHandle, SW_SHOW);
        }
    }
}