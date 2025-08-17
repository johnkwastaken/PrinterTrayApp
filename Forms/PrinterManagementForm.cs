using System.Windows.Forms;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Forms;

public class PrinterManagementForm : Form
{
    private PrinterService _printerService;
    private DataGridView _printerGrid;
    private Button _refreshButton;
    private Button _testPrintButton;
    private Label _statusLabel;
    private System.Windows.Forms.Timer _refreshTimer;
    private int _activeJobCount = 0;
    private bool _isManualRefreshing = false;

    public PrinterManagementForm(PrinterService printerService)
    {
        _printerService = printerService;
        InitializeComponents();
        LoadPrinters();
        StartAutoRefresh();
    }

    private void InitializeComponents()
    {
        Text = "Printer Management - Printer Tray App";
        Width = Constants.UI.FormWidth;
        Height = Constants.UI.FormHeight;
        StartPosition = FormStartPosition.CenterScreen;
        
        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var topPanel = new Panel
        {
            Height = 40,
            Dock = DockStyle.Top
        };

        _refreshButton = new Button
        {
            Text = "🔄 Refresh",
            Width = Constants.UI.ButtonWidth,
            Height = Constants.UI.ButtonHeight,
            Location = new Point(0, 5)
        };
        _refreshButton.Click += (s, e) => RefreshPrinters();

        _testPrintButton = new Button
        {
            Text = "🖨️ Test Print",
            Width = Constants.UI.ButtonWidth,
            Height = Constants.UI.ButtonHeight,
            Location = new Point(110, 5),
            Enabled = false
        };
        _testPrintButton.Click += (s, e) => TestPrint();

        _statusLabel = new Label
        {
            Text = "Loading printers...",
            AutoSize = true,
            Location = new Point(220, 12)
        };

        topPanel.Controls.AddRange(new Control[] { _refreshButton, _testPrintButton, _statusLabel });

        _printerGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = System.Drawing.Color.White,
            BorderStyle = BorderStyle.Fixed3D
        };

        _printerGrid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "LogicalName", 
            HeaderText = "Logical Name",
            FillWeight = 20
        });
        
        _printerGrid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "WindowsName", 
            HeaderText = "Windows Printer Name",
            FillWeight = 35
        });
        
        _printerGrid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "Status", 
            HeaderText = "Status",
            FillWeight = 15
        });
        
        _printerGrid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "Port", 
            HeaderText = "Port",
            FillWeight = 15
        });
        
        _printerGrid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            Name = "Jobs", 
            HeaderText = "Jobs",
            FillWeight = 10
        });

        _printerGrid.SelectionChanged += (s, e) =>
        {
            _testPrintButton.Enabled = _printerGrid.SelectedRows.Count > 0;
        };
        
        // Add right-click context menu
        var contextMenu = new ContextMenuStrip();
        
        var testPrintItem = new ToolStripMenuItem("Print Windows Test Page");
        testPrintItem.Click += (s, e) => TestPrint();
        contextMenu.Items.Add(testPrintItem);
        
        var propertiesItem = new ToolStripMenuItem("Printer Properties");
        propertiesItem.Click += (s, e) => ShowPrinterProperties();
        contextMenu.Items.Add(propertiesItem);
        
        var queueItem = new ToolStripMenuItem("View Print Queue");
        queueItem.Click += (s, e) => ShowPrintQueue();
        contextMenu.Items.Add(queueItem);
        
        _printerGrid.ContextMenuStrip = contextMenu;

        mainPanel.Controls.Add(_printerGrid);
        
        Controls.Add(mainPanel);
        Controls.Add(topPanel);

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = Constants.RefreshIntervals.ActiveMilliseconds
        };
        _refreshTimer.Tick += (s, e) => AutoRefreshPrinters();
    }

    private void LoadPrinters()
    {
        try
        {
            var printers = _printerService.GetAllPrinters();
            UpdatePrinterGrid(printers);
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Failed to load printers: {ex.Message}");
            _statusLabel.Text = "Error loading printers";
        }
    }

    private void UpdatePrinterGrid(List<PrinterInfo> printers)
    {
        _printerGrid.SuspendLayout();
        
        // Store selected printer
        string? selectedPrinter = null;
        if (_printerGrid.SelectedRows.Count > 0)
        {
            selectedPrinter = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
        }
        
        // Build dictionary of existing rows
        var existingRows = new Dictionary<string, DataGridViewRow>();
        foreach (DataGridViewRow row in _printerGrid.Rows)
        {
            var name = row.Cells["WindowsName"].Value?.ToString();
            if (name != null)
                existingRows[name] = row;
        }
        
        int totalJobs = 0;
        var processedPrinters = new HashSet<string>();
        
        foreach (var printer in printers)
        {
            processedPrinters.Add(printer.WindowsPrinterName);
            totalJobs += (int)printer.JobCount;
            
            if (existingRows.TryGetValue(printer.WindowsPrinterName, out var row))
            {
                // Update existing row only if values changed
                bool needsUpdate = false;
                
                if (row.Cells["Status"].Value?.ToString() != printer.Status)
                {
                    row.Cells["Status"].Value = printer.Status;
                    needsUpdate = true;
                }
                
                var jobsText = printer.JobCount.ToString();
                if (row.Cells["Jobs"].Value?.ToString() != jobsText)
                {
                    row.Cells["Jobs"].Value = jobsText;
                    needsUpdate = true;
                }
                
                if (row.Cells["LogicalName"].Value?.ToString() != printer.LogicalName)
                {
                    row.Cells["LogicalName"].Value = printer.LogicalName;
                    needsUpdate = true;
                }
                
                if (needsUpdate)
                {
                    UpdateRowStyle(row, printer);
                }
            }
            else
            {
                // Add new printer
                row = _printerGrid.Rows[_printerGrid.Rows.Add()];
                row.Cells["LogicalName"].Value = printer.LogicalName;
                row.Cells["WindowsName"].Value = printer.WindowsPrinterName;
                row.Cells["Status"].Value = printer.Status;
                row.Cells["Port"].Value = printer.PortName;
                row.Cells["Jobs"].Value = printer.JobCount.ToString();
                UpdateRowStyle(row, printer);
            }
            
            // Restore selection
            if (selectedPrinter == printer.WindowsPrinterName)
            {
                row.Selected = true;
            }
        }
        
        // Remove printers that no longer exist
        foreach (var kvp in existingRows)
        {
            if (!processedPrinters.Contains(kvp.Key))
            {
                _printerGrid.Rows.Remove(kvp.Value);
            }
        }
        
        _printerGrid.ResumeLayout();
        
        _activeJobCount = totalJobs;
        var jobIndicator = totalJobs > 0 ? $" | {totalJobs} active job(s)" : "";
        _statusLabel.Text = $"Found {printers.Count} printer(s){jobIndicator} - Updated: {DateTime.Now:HH:mm:ss}";
    }

    private void UpdateRowStyle(DataGridViewRow row, PrinterInfo printer)
    {
        var statusText = printer.Status.ToLower();
        
        if (statusText == "ready" || statusText == "waiting" || statusText == "active")
        {
            row.DefaultCellStyle.ForeColor = System.Drawing.Color.Green;
            row.Cells["Status"].Style.Font = new System.Drawing.Font(row.InheritedStyle.Font, System.Drawing.FontStyle.Bold);
        }
        else if (statusText == "printing" || statusText == "processing" || statusText == "busy")
        {
            row.DefaultCellStyle.ForeColor = System.Drawing.Color.Blue;
        }
        else if (statusText == "paused" || statusText == "warming up" || statusText == "initializing" || statusText == "power save")
        {
            row.DefaultCellStyle.ForeColor = System.Drawing.Color.Orange;
        }
        else if (printer.HasError || !printer.IsOnline || statusText.Contains("error") || statusText.Contains("offline"))
        {
            row.DefaultCellStyle.ForeColor = System.Drawing.Color.Red;
            row.Cells["Status"].Style.Font = new System.Drawing.Font(row.InheritedStyle.Font, System.Drawing.FontStyle.Bold);
        }
        else
        {
            row.DefaultCellStyle.ForeColor = System.Drawing.Color.DarkGray;
        }
    }

    private void AutoRefreshPrinters()
    {
        if (_isManualRefreshing) return;
        
        // Always refresh the cached printer data
        _printerService.RefreshPrinters();
        
        // Update the UI
        LoadPrinters();
        
        // Adjust refresh rate based on activity
        if (_activeJobCount > 0)
        {
            // Fast refresh when jobs are active
            _refreshTimer.Interval = Constants.RefreshIntervals.ActiveMilliseconds;
        }
        else
        {
            // Slower refresh when idle
            _refreshTimer.Interval = Constants.RefreshIntervals.IdleMilliseconds;
        }
    }

    private void RefreshPrinters()
    {
        try
        {
            _isManualRefreshing = true;
            _statusLabel.Text = "Refreshing...";
            _printerService.RefreshPrinters(true);  // Log manual refresh
            LoadPrinters();
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Manual refresh failed: {ex.Message}");
            _statusLabel.Text = "Refresh failed";
        }
        finally
        {
            _isManualRefreshing = false;
        }
    }

    private void TestPrint()
    {
        if (_printerGrid.SelectedRows.Count > 0)
        {
            var printerName = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
            if (!string.IsNullOrEmpty(printerName))
            {
                try
                {
                    _statusLabel.Text = $"Sending test to {printerName}...";
                    ConsoleWindow.WriteLine($"Initiating test print to: {printerName}");
                    
                    // Send test print using PrintDirect
                    var testCommands = "ESC@\nTEST PRINT\n\n\nEscP";
                    var jobId = PrintDirect.Print(
                        printerName,
                        "TEST_PRINT",
                        "RAW",
                        testCommands);
                    
                    if (jobId > 0)
                    {
                        _statusLabel.Text = $"Test sent to {printerName}";
                        ConsoleWindow.WriteLine($"Test print sent successfully to: {printerName}");
                        MessageBox.Show($"Simple test has been sent to '{printerName}'.\n\nCheck the printer output.", 
                            "Test Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        _statusLabel.Text = $"Cannot test {printerName}";
                        
                        // Check if it's a virtual printer
                        var nameLower = printerName.ToLower();
                        if (nameLower.Contains("pdf") || nameLower.Contains("xps") || nameLower.Contains("onenote"))
                        {
                            ConsoleWindow.WriteLine($"Virtual printer detected, cannot print: {printerName}");
                            MessageBox.Show($"'{printerName}' is a virtual printer.\n\nVirtual printers don't support RAW printing.\nThis app is designed for physical printers and ESC/POS devices.", 
                                "Virtual Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            ConsoleWindow.WriteError($"Test print failed for: {printerName}");
                            MessageBox.Show($"Failed to send test to '{printerName}'.\n\nMake sure the printer is online and supports RAW printing.", 
                                "Test Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Test print exception for {printerName}: {ex.Message}");
                    _statusLabel.Text = "Test print error";
                    MessageBox.Show($"Error during test print:\n{ex.Message}", "Test Print Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void ShowPrinterProperties()
    {
        if (_printerGrid.SelectedRows.Count > 0)
        {
            var printerName = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
            if (!string.IsNullOrEmpty(printerName))
            {
                try
                {
                    ConsoleWindow.WriteLine($"Opening properties for printer: {printerName}");
                    // Open printer properties using Windows shell
                    System.Diagnostics.Process.Start("rundll32.exe", $"printui.dll,PrintUIEntry /p /n \"{printerName}\"");
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to open printer properties: {ex.Message}");
                    MessageBox.Show($"Could not open printer properties:\n{ex.Message}", "Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void ShowPrintQueue()
    {
        if (_printerGrid.SelectedRows.Count > 0)
        {
            var printerName = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
            if (!string.IsNullOrEmpty(printerName))
            {
                try
                {
                    ConsoleWindow.WriteLine($"Opening print queue for: {printerName}");
                    // Open print queue using Windows shell
                    System.Diagnostics.Process.Start("rundll32.exe", $"printui.dll,PrintUIEntry /o /n \"{printerName}\"");
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to open print queue: {ex.Message}");
                    MessageBox.Show($"Could not open print queue:\n{ex.Message}", "Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void StartAutoRefresh()
    {
        _refreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        base.OnFormClosed(e);
    }
}