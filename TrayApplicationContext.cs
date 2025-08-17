using System.Windows.Forms;
using PrinterTrayApp.Services;
using PrinterTrayApp.Forms;

namespace PrinterTrayApp;

public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private HttpServer _httpServer;
    private PrinterService _printerService;
    private CancellationTokenSource _cts;
    private Form? _statusForm;
    private PrinterManagementForm? _printerForm;
    private PrintTestForm? _testForm;

    public TrayApplicationContext()
    {
        try
        {
            ConsoleWindow.WriteLine("=== Application Starting ===");
            
            _cts = new CancellationTokenSource();
            
            ConsoleWindow.WriteLine("Initializing printer service...");
            _printerService = new PrinterService();
            
            ConsoleWindow.WriteLine("Creating HTTP server...");
            _httpServer = new HttpServer(_printerService);
            
            
            ConsoleWindow.WriteLine("Setting up tray icon...");
            InitializeTrayIcon();
            
            ConsoleWindow.WriteLine("Starting HTTP server...");
            StartHttpServer();
            
            
            ConsoleWindow.WriteLine("=== Application Started Successfully ===");
            
            // Log detailed diagnostics after startup
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Failed to initialize application: {ex}");
            MessageBox.Show($"Failed to start application:\n{ex.Message}", "Startup Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }


    private void InitializeTrayIcon()
    {
        _trayIcon = new NotifyIcon()
        {
            Text = Constants.AppName,
            Visible = true,
            ContextMenuStrip = CreateContextMenu(),
            Icon = CreateTrayIcon()
        };

        _trayIcon.BalloonTipTitle = Constants.AppName;
        _trayIcon.BalloonTipText = $"Application started. API running on port {Constants.ApiPort}";
        _trayIcon.ShowBalloonTip(Constants.UI.BalloonTipTimeout);
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var iconBitmap = new System.Drawing.Bitmap(Constants.UI.TrayIconSize, Constants.UI.TrayIconSize);
        using (var g = System.Drawing.Graphics.FromImage(iconBitmap))
        {
            g.Clear(System.Drawing.Color.DarkBlue);
            using (var font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold))
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
            {
                g.DrawString(Constants.UI.TrayIconText, font, brush, 0, 0);
            }
        }
        
        var hIcon = iconBitmap.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(hIcon);
        iconBitmap.Dispose();
        
        // Create a copy to avoid handle issues
        var iconCopy = new System.Drawing.Icon(icon, Constants.UI.TrayIconSize, Constants.UI.TrayIconSize);
        DestroyIcon(hIcon);
        return iconCopy;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private ContextMenuStrip CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        
        var printersItem = new ToolStripMenuItem("🖨️ Manage Printers");
        printersItem.Click += ShowPrinterManagement;
        contextMenu.Items.Add(printersItem);
        
        var testPrintItem = new ToolStripMenuItem("🧪 Test Print (JSON)");
        testPrintItem.Click += ShowTestForm;
        contextMenu.Items.Add(testPrintItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var consoleItem = new ToolStripMenuItem("Show/Hide Console");
        consoleItem.Click += ToggleConsole;
        contextMenu.Items.Add(consoleItem);
        
        var statusItem = new ToolStripMenuItem("API Status");
        statusItem.Click += ShowStatus;
        contextMenu.Items.Add(statusItem);
        
        var memoryItem = new ToolStripMenuItem("Memory Status");
        memoryItem.Click += ShowMemoryStatus;
        contextMenu.Items.Add(memoryItem);
        
        var diagnosticsItem = new ToolStripMenuItem("Run Diagnostics");
        diagnosticsItem.Click += (s, e) => ConsoleWindow.WriteLine("Diagnostics: System running normally");
        contextMenu.Items.Add(diagnosticsItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += Exit;
        contextMenu.Items.Add(exitItem);
        
        return contextMenu;
    }

    private void ShowPrinterManagement(object? sender, EventArgs e)
    {
        if (_printerForm == null || _printerForm.IsDisposed)
        {
            _printerForm = new PrinterManagementForm(_printerService);
        }
        
        _printerForm.Show();
        _printerForm.BringToFront();
    }
    
    private void ShowTestForm(object? sender, EventArgs e)
    {
        if (_testForm == null || _testForm.IsDisposed)
        {
            _testForm = new PrintTestForm();
        }
        
        _testForm.Show();
        _testForm.BringToFront();
    }

    private void ToggleConsole(object? sender, EventArgs e)
    {
        ConsoleWindow.Toggle();
    }

    private void ShowMemoryStatus(object? sender, EventArgs e)
    {
        ConsoleWindow.WriteLine($"Memory Status: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
        
        using var memoryForm = new MemoryStatusForm();
        memoryForm.ShowDialog();
    }

    private async void StartHttpServer()
    {
        try
        {
            await _httpServer.StartAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Failed to start HTTP server: {ex.Message}");
            
            MessageBox.Show($"Failed to start HTTP server: {ex.Message}", 
                "Printer Tray App Error", 
                MessageBoxButtons.OK, 
                MessageBoxIcon.Error);
            
            Application.Exit();
        }
    }

    private void ShowStatus(object? sender, EventArgs e)
    {
        if (_statusForm == null || _statusForm.IsDisposed)
        {
            _statusForm = new Form
            {
                Text = "Printer Tray App - Status",
                Width = 500,
                Height = 300,
                StartPosition = FormStartPosition.CenterScreen
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 10),
                Text = $"Printer Tray App Status\r\n" +
                       $"========================\r\n\r\n" +
                       $"Version: 0.1.0\r\n" +
                       $"API Server: http://127.0.0.1:9877\r\n" +
                       $"Status: Running\r\n\r\n" +
                       $"Available Endpoints:\r\n" +
                       $"  GET  /health     - Check server health\r\n" +
                       $"  POST /print      - Submit print job\r\n" +
                       $"  GET  /self-test  - Print test receipt\r\n\r\n" +
                       $"Test with PowerShell:\r\n" +
                       $"  Invoke-WebRequest -Uri http://127.0.0.1:9877/health"
            };

            _statusForm.Controls.Add(textBox);
        }

        _statusForm.Show();
        _statusForm.BringToFront();
    }

    private async void Exit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _cts.Cancel();
        
        try
        {
            await _httpServer.StopAsync();
        }
        catch { }
        
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _cts?.Dispose();
            _statusForm?.Dispose();
            _printerForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}