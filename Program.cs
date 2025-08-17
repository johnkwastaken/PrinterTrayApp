using PrinterTrayApp;
using System.Windows.Forms;

// Check for test mode FIRST
var cmdArgs = Environment.GetCommandLineArgs();
if (cmdArgs.Length > 1 && cmdArgs[1] == "--test")
{
    RunTests();
    return;
}

// Configure thread pool for minimal resource usage
ThreadPool.SetMinThreads(2, 2);  // Reduce minimum threads
ThreadPool.SetMaxThreads(10, 10); // Cap maximum threads

// Set up global exception handlers
Application.ThreadException += (sender, e) =>
{
    ConsoleWindow.WriteError($"[UNHANDLED UI EXCEPTION] {e.Exception}");
    MessageBox.Show($"An unexpected error occurred:\n{e.Exception.Message}", "Application Error", 
        MessageBoxButtons.OK, MessageBoxIcon.Error);
};

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    var ex = e.ExceptionObject as Exception;
    ConsoleWindow.WriteError($"[UNHANDLED DOMAIN EXCEPTION] {ex?.ToString() ?? e.ExceptionObject.ToString()}");
    MessageBox.Show($"A critical error occurred:\n{ex?.Message ?? "Unknown error"}", "Critical Error", 
        MessageBoxButtons.OK, MessageBoxIcon.Error);
};

Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

// Prevent multiple instances
using var mutex = new Mutex(true, Constants.MutexName, out bool createdNew);
if (!createdNew)
{
    MessageBox.Show($"{Constants.AppName} is already running!", "Already Running", 
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    return;
}

// Set STA thread for clipboard operations
[STAThread]
static void SetSTAThread() { }
SetSTAThread();

// Enable visual styles for Windows Forms
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// Run as tray application
Application.Run(new TrayApplicationContext());

static void RunTests()
{
    Console.WriteLine("RUNNING PRINTER OUTPUT TESTS");
    Console.WriteLine("=============================\n");
    Console.WriteLine("Template test mode is not available.");
    Console.WriteLine("Tests have been moved to the PrinterTrayApp.Tests project.");
}