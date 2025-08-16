using System.Windows.Forms;
using PrinterTrayApp.Services;

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
        Width = 800;
        Height = 500;
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
            Width = 100,
            Height = 30,
            Location = new Point(0, 5)
        };
        _refreshButton.Click += (s, e) => RefreshPrinters();

        _testPrintButton = new Button
        {
            Text = "🖨️ Test Print",
            Width = 100,
            Height = 30,
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

        mainPanel.Controls.Add(_printerGrid);
        
        Controls.Add(mainPanel);
        Controls.Add(topPanel);

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000  // Start with 1 second for responsiveness
        };
        _refreshTimer.Tick += (s, e) => AutoRefreshPrinters();
    }

    private void LoadPrinters()
    {
        var printers = _printerService.GetAllPrinters();
        
        // Store selected printer before refresh
        string? selectedPrinter = null;
        if (_printerGrid.SelectedRows.Count > 0)
        {
            selectedPrinter = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
        }
        
        _printerGrid.Rows.Clear();
        
        int totalJobs = 0;
        
        foreach (var printer in printers)
        {
            var row = _printerGrid.Rows[_printerGrid.Rows.Add()];
            row.Cells["LogicalName"].Value = printer.LogicalName;
            row.Cells["WindowsName"].Value = printer.WindowsPrinterName;
            row.Cells["Status"].Value = printer.Status;
            row.Cells["Port"].Value = printer.PortName;
            row.Cells["Jobs"].Value = printer.JobCount.ToString();
            
            totalJobs += (int)printer.JobCount;
            
            // Color code based on status
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
            
            // Restore selection
            if (selectedPrinter != null && printer.WindowsPrinterName == selectedPrinter)
            {
                row.Selected = true;
            }
        }
        
        _activeJobCount = totalJobs;
        
        // Update status with job count indicator
        var jobIndicator = totalJobs > 0 ? $" | {totalJobs} active job(s)" : "";
        _statusLabel.Text = $"Found {printers.Count} printer(s){jobIndicator} - Updated: {DateTime.Now:HH:mm:ss}";
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
            // Fast refresh (1 second) when jobs are active
            _refreshTimer.Interval = 1000;
        }
        else
        {
            // Slower refresh (3 seconds) when idle
            _refreshTimer.Interval = 3000;
        }
    }

    private void RefreshPrinters()
    {
        _isManualRefreshing = true;
        _statusLabel.Text = "Refreshing...";
        _printerService.RefreshPrinters(true);  // Log manual refresh
        LoadPrinters();
        _isManualRefreshing = false;
    }

    private void TestPrint()
    {
        if (_printerGrid.SelectedRows.Count > 0)
        {
            var printerName = _printerGrid.SelectedRows[0].Cells["WindowsName"].Value?.ToString();
            MessageBox.Show($"Test print to '{printerName}' will be available after Milestone 7", 
                "Test Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
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