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

    public TrayApplicationContext()
    {
        _cts = new CancellationTokenSource();
        _printerService = new PrinterService();
        _httpServer = new HttpServer(_printerService);
        
        InitializeTrayIcon();
        StartHttpServer();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new NotifyIcon()
        {
            Text = "Printer Tray App",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        // Create a simple icon programmatically (16x16 black square with "P" letter)
        using (var bmp = new System.Drawing.Bitmap(16, 16))
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.DarkBlue);
            g.DrawString("P", new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold), 
                System.Drawing.Brushes.White, 0, 0);
            
            _trayIcon.Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        _trayIcon.BalloonTipTitle = "Printer Tray App";
        _trayIcon.BalloonTipText = "Application started. API running on port 9877";
        _trayIcon.ShowBalloonTip(3000);
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        
        var printersItem = new ToolStripMenuItem("🖨️ Manage Printers");
        printersItem.Click += ShowPrinterManagement;
        contextMenu.Items.Add(printersItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var consoleItem = new ToolStripMenuItem("Show/Hide Console");
        consoleItem.Click += ToggleConsole;
        contextMenu.Items.Add(consoleItem);
        
        var statusItem = new ToolStripMenuItem("API Status");
        statusItem.Click += ShowStatus;
        contextMenu.Items.Add(statusItem);
        
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

    private void ToggleConsole(object? sender, EventArgs e)
    {
        ConsoleWindow.Toggle();
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