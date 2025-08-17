using PrinterTrayApp;
using System.Windows.Forms;

// ================================================================================
// PRINTER TRAY APP - Main Entry Point
// ================================================================================
// This Windows application runs in the system tray and provides a REST API
// for the POS system to send print jobs to thermal printers.
// 
// Key Features:
// - HTTP Server on port 9877 accepting PrinterTask JSON from POS
// - ESC/POS command generation for thermal receipt printers
// - XML template rendering with token replacement
// - Direct Windows printer spooler integration
// - System tray icon with management UI
// ================================================================================

// Check for test mode FIRST - allows running tests without starting the server
var cmdArgs = Environment.GetCommandLineArgs();
if (cmdArgs.Length > 1 && cmdArgs[1] == "--test")
{
    RunTests();
    return;
}

// Configure thread pool for minimal resource usage
// Since this is a local-only API with low traffic, we can minimize threads
ThreadPool.SetMinThreads(2, 2);  // Reduce minimum threads (1 for HTTP, 1 for UI)
ThreadPool.SetMaxThreads(10, 10); // Cap maximum threads to prevent resource exhaustion

// Set up global exception handlers to catch any unhandled errors
// This prevents the app from crashing silently and helps with debugging
Application.ThreadException += (sender, e) =>
{
    // Log to console window for debugging
    ConsoleWindow.WriteError($"[UNHANDLED UI EXCEPTION] {e.Exception}");
    // Show user-friendly error message
    MessageBox.Show($"An unexpected error occurred:\n{e.Exception.Message}", "Application Error", 
        MessageBoxButtons.OK, MessageBoxIcon.Error);
};

// Catch exceptions from non-UI threads (like the HTTP server)
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    var ex = e.ExceptionObject as Exception;
    ConsoleWindow.WriteError($"[UNHANDLED DOMAIN EXCEPTION] {ex?.ToString() ?? e.ExceptionObject.ToString()}");
    MessageBox.Show($"A critical error occurred:\n{ex?.Message ?? "Unknown error"}", "Critical Error", 
        MessageBoxButtons.OK, MessageBoxIcon.Error);
};

Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

// Prevent multiple instances using a named mutex
// This ensures only one instance can bind to port 9877 and prevents duplicate tray icons
using var mutex = new Mutex(true, Constants.MutexName, out bool createdNew);
if (!createdNew)
{
    // Another instance is already running - notify user and exit gracefully
    MessageBox.Show($"{Constants.AppName} is already running!", "Already Running", 
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// Set STA (Single Threaded Apartment) thread model required for Windows Forms
// This is needed for clipboard operations and COM interop with Windows printing
[STAThread]
static void SetSTAThread() { }
SetSTAThread();

// Enable visual styles for modern Windows appearance (Windows XP and later)
Application.EnableVisualStyles();
// Use GDI+ for text rendering (better quality than GDI)
Application.SetCompatibleTextRenderingDefault(false);

// Run as tray application
// TrayApplicationContext creates the tray icon and starts the HTTP server
// The app will run until the user selects "Exit" from the tray menu
Application.Run(new TrayApplicationContext());

static void RunTests()
{
    Console.WriteLine("RUNNING PRINTER OUTPUT TESTS");
    Console.WriteLine("=============================\n");
    Console.WriteLine("Template test mode is not available.");
    Console.WriteLine("Tests have been moved to the PrinterTrayApp.Tests project.");
}