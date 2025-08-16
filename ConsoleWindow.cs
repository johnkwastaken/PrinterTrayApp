using System.Runtime.InteropServices;
using System.Text;

namespace PrinterTrayApp;

public static class ConsoleWindow
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleTitle(string lpConsoleTitle);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static bool _consoleAllocated = false;
    private static IntPtr _consoleHandle = IntPtr.Zero;
    private static StringWriter? _consoleOutput;
    private static TextWriter? _originalOutput;

    public static void Show()
    {
        if (!_consoleAllocated)
        {
            AllocConsole();
            _consoleAllocated = true;
            SetConsoleTitle("Printer Tray App - Console");
            
            // Redirect console output
            _originalOutput = Console.Out;
            _consoleOutput = new StringWriter();
            
            var writer = new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true
            };
            Console.SetOut(writer);
            Console.SetError(writer);
            
            Console.WriteLine("=================================");
            Console.WriteLine("Printer Tray App - Console Output");
            Console.WriteLine("=================================");
            Console.WriteLine($"Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"API Server: http://127.0.0.1:9877");
            Console.WriteLine();
            Console.WriteLine("This console shows live server logs.");
            Console.WriteLine("You can close this window anytime - the app keeps running in tray.");
            Console.WriteLine("=================================");
            Console.WriteLine();
        }

        _consoleHandle = GetConsoleWindow();
        if (_consoleHandle != IntPtr.Zero)
        {
            ShowWindow(_consoleHandle, SW_SHOW);
        }
    }

    public static void Hide()
    {
        if (_consoleHandle != IntPtr.Zero)
        {
            ShowWindow(_consoleHandle, SW_HIDE);
        }
    }

    public static void Toggle()
    {
        if (!_consoleAllocated)
        {
            Show();
        }
        else
        {
            _consoleHandle = GetConsoleWindow();
            if (_consoleHandle != IntPtr.Zero)
            {
                // Check if window is visible
                var isVisible = IsWindowVisible(_consoleHandle);
                ShowWindow(_consoleHandle, isVisible ? SW_HIDE : SW_SHOW);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static void WriteLine(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";
        
        if (_consoleAllocated)
        {
            Console.WriteLine(formattedMessage);
        }
    }

    public static void WriteError(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] ERROR: {message}";
        
        if (_consoleAllocated)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(formattedMessage);
            Console.ForegroundColor = oldColor;
        }
    }
}