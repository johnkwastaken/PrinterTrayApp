using System.Windows.Forms;

namespace PrinterTrayApp.Forms;

public class MemoryStatusForm : Form
{
    private TextBox _statusTextBox;
    private Button _copyButton;
    private Button _refreshButton;
    private Button _gcButton;
    
    public MemoryStatusForm()
    {
        InitializeComponents();
        RefreshStatus();
    }
    
    private void InitializeComponents()
    {
        Text = "Memory Status - Printer Tray App";
        Width = 500;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        
        // Status TextBox
        _statusTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Font = new System.Drawing.Font("Consolas", 10),
            Dock = DockStyle.Top,
            Height = 300,
            ScrollBars = ScrollBars.Vertical
        };
        
        // Button Panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50
        };
        
        _copyButton = new Button
        {
            Text = "📋 Copy to Clipboard",
            Width = 150,
            Height = 30,
            Location = new System.Drawing.Point(10, 10)
        };
        _copyButton.Click += CopyToClipboard;
        
        _refreshButton = new Button
        {
            Text = "🔄 Refresh",
            Width = 100,
            Height = 30,
            Location = new System.Drawing.Point(170, 10)
        };
        _refreshButton.Click += (s, e) => RefreshStatus();
        
        _gcButton = new Button
        {
            Text = "🗑️ Force GC",
            Width = 100,
            Height = 30,
            Location = new System.Drawing.Point(280, 10)
        };
        _gcButton.Click += (s, e) => 
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            RefreshStatus();
        };
        
        buttonPanel.Controls.AddRange(new Control[] { _copyButton, _refreshButton, _gcButton });
        
        Controls.Add(_statusTextBox);
        Controls.Add(buttonPanel);
    }
    
    private void RefreshStatus()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var managedMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
        var workingSet = process.WorkingSet64 / 1024.0 / 1024.0;
        var privateMemory = process.PrivateMemorySize64 / 1024.0 / 1024.0;
        var uptime = DateTime.Now - process.StartTime;
        var text = new System.Text.StringBuilder();
        
        text.AppendLine("=== MEMORY STATUS REPORT ===");
        text.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"App Version: {Constants.ApiVersion}");
        text.AppendLine($"Uptime: {uptime:hh\\:mm\\:ss}");
        text.AppendLine();
        
        text.AppendLine("=== MEMORY USAGE ===");
        text.AppendLine($"Working Set:     {workingSet,8:F1} MB (RAM used)");
        text.AppendLine($"Private Memory:  {privateMemory,8:F1} MB (Process total)");
        text.AppendLine($"Managed Memory:  {managedMemory,8:F1} MB (.NET heap)");
        text.AppendLine();
        
        text.AppendLine("=== RESOURCES ===");
        text.AppendLine($"Handle Count:    {process.HandleCount,8} (Target < 500)");
        text.AppendLine($"Thread Count:    {process.Threads.Count,8} (Target < 20)");
        text.AppendLine();
        
        text.AppendLine("=== GARBAGE COLLECTION ===");
        text.AppendLine($"Gen 0 Collections: {GC.CollectionCount(0)}");
        text.AppendLine($"Gen 1 Collections: {GC.CollectionCount(1)}");
        text.AppendLine($"Gen 2 Collections: {GC.CollectionCount(2)}");
        text.AppendLine();
        
        // Health check
        text.AppendLine("=== HEALTH CHECK ===");
        
        if (workingSet < 50)
            text.AppendLine("✅ Memory usage: GOOD");
        else if (workingSet < 100)
            text.AppendLine("⚠️ Memory usage: WARNING (>50MB)");
        else
            text.AppendLine("❌ Memory usage: HIGH (>100MB)");
            
        if (process.HandleCount < 500)
            text.AppendLine("✅ Handle count: GOOD");
        else if (process.HandleCount < 1000)
            text.AppendLine("⚠️ Handle count: WARNING (>500)");
        else
            text.AppendLine("❌ Handle count: HIGH (>1000)");
            
        if (process.Threads.Count < 20)
            text.AppendLine("✅ Thread count: GOOD");
        else if (process.Threads.Count < 50)
            text.AppendLine("⚠️ Thread count: WARNING (>20)");
        else
            text.AppendLine("❌ Thread count: HIGH (>50)");
        
        _statusTextBox.Text = text.ToString();
    }
    
    private void CopyToClipboard(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_statusTextBox.Text))
            {
                ConsoleWindow.WriteError("No text to copy");
                return;
            }
            
            // Use Invoke to ensure we're on the UI thread
            if (InvokeRequired)
            {
                Invoke(new Action(() => CopyToClipboard(sender, e)));
                return;
            }
            
            // Try simple clipboard operation first
            try
            {
                Clipboard.SetText(_statusTextBox.Text);
                ConsoleWindow.WriteLine("Memory status copied to clipboard");
                MessageBox.Show("Memory status copied to clipboard!", "Copied", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            catch (Exception clipEx)
            {
                ConsoleWindow.WriteError($"Simple clipboard copy failed: {clipEx.Message}");
                
                // Try with DataObject
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetData(DataFormats.Text, _statusTextBox.Text);
                    dataObject.SetData(DataFormats.UnicodeText, _statusTextBox.Text);
                    Clipboard.SetDataObject(dataObject, true);
                    
                    ConsoleWindow.WriteLine("Memory status copied to clipboard (DataObject method)");
                    MessageBox.Show("Memory status copied to clipboard!", "Copied", 
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                catch (Exception dataEx)
                {
                    ConsoleWindow.WriteError($"DataObject clipboard copy failed: {dataEx.Message}");
                }
            }
            
            // Fallback: Save to file
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var fileName = $"MemoryStatus_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = System.IO.Path.Combine(desktopPath, fileName);
            
            System.IO.File.WriteAllText(filePath, _statusTextBox.Text);
            
            ConsoleWindow.WriteLine($"Saved memory status to file: {filePath}");
            MessageBox.Show($"Clipboard access failed.\n\nMemory status saved to:\n{filePath}", 
                "Saved to File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Critical error in copy operation: {ex}");
            MessageBox.Show($"Failed to copy: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}