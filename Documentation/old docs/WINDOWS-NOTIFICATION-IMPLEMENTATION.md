# 🔔 Windows Notification Implementation for Print Service

## Overview
Implement Windows notifications to alert staff about print errors while allowing the POS to assume success and continue operating smoothly.

## Core Philosophy
**"Optimistic Printing with Error Notifications"**
- POS assumes print success immediately (non-blocking)
- Windows notifications alert staff only when problems occur
- No need for complex status tracking or databases

## Implementation

### 1. Basic Notification Service

```csharp
using System.Windows.Forms;

public class PrintNotificationService
{
    private readonly NotifyIcon _trayIcon;
    private readonly Queue<string> _recentNotifications = new();
    private readonly object _lock = new();
    
    public PrintNotificationService(NotifyIcon trayIcon)
    {
        _trayIcon = trayIcon;
    }
    
    // Simple balloon notification (works on all Windows versions)
    public void ShowNotification(string title, string message, bool isError = false)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = isError ? ToolTipIcon.Error : ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(3000);
        
        // Log notification
        ConsoleWindow.WriteLine($"[NOTIFICATION] {title}: {message}");
        
        // Track recent notifications to avoid duplicates
        lock (_lock)
        {
            var key = $"{title}:{message}";
            _recentNotifications.Enqueue(key);
            
            // Keep only last 50 notifications
            while (_recentNotifications.Count > 50)
                _recentNotifications.Dequeue();
        }
    }
    
    // Check if we already notified about this
    public bool WasRecentlyNotified(string title, string message)
    {
        lock (_lock)
        {
            var key = $"{title}:{message}";
            return _recentNotifications.Contains(key);
        }
    }
}
```

### 2. Print Monitor with Notifications

```csharp
public class PrintMonitorService
{
    private readonly PrintNotificationService _notifications;
    private readonly PrinterService _printerService;
    private readonly HashSet<string> _notifiedJobs = new();
    private Task? _monitorTask;
    private CancellationTokenSource? _cancellation;
    
    public PrintMonitorService(PrintNotificationService notifications, PrinterService printerService)
    {
        _notifications = notifications;
        _printerService = printerService;
    }
    
    public void StartMonitoring()
    {
        _cancellation = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorPrintJobs(_cancellation.Token));
    }
    
    private async Task MonitorPrintJobs(CancellationToken cancellationToken)
    {
        ConsoleWindow.WriteLine("Print monitor started");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                CheckForPrintErrors();
                await Task.Delay(2000, cancellationToken); // Check every 2 seconds
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Monitor error: {ex.Message}");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }
    
    private void CheckForPrintErrors()
    {
        var printers = _printerService.GetPrinterNames();
        
        foreach (var printerName in printers)
        {
            try
            {
                // Check printer status
                var printerInfo = _printerService.GetPrinterInfo(printerName);
                
                // Notify about printer-level issues
                if (printerInfo.Status.Contains("Offline"))
                {
                    if (!_notifications.WasRecentlyNotified("Printer Offline", printerName))
                    {
                        _notifications.ShowNotification(
                            "🔌 Printer Offline",
                            $"{printerName} is offline. Please check the connection.",
                            isError: true
                        );
                    }
                    continue; // Skip job checking if printer is offline
                }
                
                if (printerInfo.Status.Contains("Paper"))
                {
                    if (!_notifications.WasRecentlyNotified("Paper Out", printerName))
                    {
                        _notifications.ShowNotification(
                            "📄 Paper Out",
                            $"{printerName} is out of paper!",
                            isError: true
                        );
                    }
                }
                
                // Check individual print jobs
                var jobs = PrintDirect.GetPrinterJobs(printerName);
                
                foreach (var job in jobs)
                {
                    CheckJobStatus(job, printerName);
                }
                
                // Check for queue jams (too many stuck jobs)
                if (jobs.Count > 10)
                {
                    if (!_notifications.WasRecentlyNotified("Queue Jammed", printerName))
                    {
                        _notifications.ShowNotification(
                            "🚨 Print Queue Jammed",
                            $"{printerName} has {jobs.Count} stuck jobs!",
                            isError: true
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error checking printer {printerName}: {ex.Message}");
            }
        }
    }
    
    private void CheckJobStatus(PrintJobInfo job, string printerName)
    {
        var jobKey = $"{printerName}:{job.JobId}";
        
        // Only notify once per job
        if (_notifiedJobs.Contains(jobKey))
            return;
        
        // Check for error conditions
        if (job.Status.HasFlag(JobStatus.Error))
        {
            _notifiedJobs.Add(jobKey);
            _notifications.ShowNotification(
                "❌ Print Failed",
                $"Job '{job.DocumentName}' failed on {printerName}",
                isError: true
            );
        }
        else if (job.Status.HasFlag(JobStatus.UserIntervention))
        {
            _notifiedJobs.Add(jobKey);
            _notifications.ShowNotification(
                "⚠️ Printer Needs Attention",
                $"{printerName} requires user intervention for '{job.DocumentName}'",
                isError: true
            );
        }
        else if (job.Status.HasFlag(JobStatus.Paused))
        {
            // Check if paused for more than 30 seconds
            if ((DateTime.Now - job.SubmitTime).TotalSeconds > 30)
            {
                if (!_notifiedJobs.Contains(jobKey))
                {
                    _notifiedJobs.Add(jobKey);
                    _notifications.ShowNotification(
                        "⏸️ Print Paused",
                        $"Job '{job.DocumentName}' is paused on {printerName}",
                        isError: false
                    );
                }
            }
        }
        
        // Clean up old notifications (jobs that are gone)
        if (_notifiedJobs.Count > 100)
        {
            _notifiedJobs.Clear();
        }
    }
}
```

### 3. Updated HTTP Server with Notifications

```csharp
public class HttpServer
{
    private readonly PrintNotificationService _notifications;
    private readonly PrintMonitorService _monitor;
    private readonly PrinterService _printerService;
    
    public HttpServer(NotifyIcon trayIcon, PrinterService printerService)
    {
        _printerService = printerService;
        _notifications = new PrintNotificationService(trayIcon);
        _monitor = new PrintMonitorService(_notifications, printerService);
        
        // Start monitoring for errors
        _monitor.StartMonitoring();
    }
    
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // ... existing startup code ...
        
        ConfigureEndpoints();
    }
    
    private void ConfigureEndpoints()
    {
        // Simple fire-and-forget print endpoint
        _app.MapPost("/print", async (HttpContext context) =>
        {
            try
            {
                ConsoleWindow.WriteLine($"POST /print from {context.Connection.RemoteIpAddress}");
                
                // Parse and process request
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                var printerTask = JsonSerializer.Deserialize<PrinterTask>(body, _jsonOptions);
                
                if (printerTask == null)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Invalid request" 
                    }));
                    return;
                }
                
                // Generate job number and print data
                var jobNumber = GenerateJobNumber();
                var printData = await ProcessPrintTask(printerTask, jobNumber);
                
                // Get printer
                var printers = _printerService.GetPrinterNames();
                if (printers.Count == 0)
                {
                    // Notify about no printers
                    _notifications.ShowNotification(
                        "❌ No Printers",
                        "No printers available for printing!",
                        isError: true
                    );
                    
                    context.Response.StatusCode = 503;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "No printers available" 
                    }));
                    return;
                }
                
                var targetPrinter = SelectPrinter(printerTask, printers);
                
                // Submit to Windows spooler (fire and forget)
                int spoolerId = PrintDirect.Print(targetPrinter, jobNumber, "RAW", printData);
                
                // Special notifications for important operations
                if (printerTask.isOpenCashDrawer)
                {
                    _notifications.ShowNotification(
                        "💰 Cash Drawer",
                        $"Opening cash drawer on {targetPrinter}",
                        isError: false
                    );
                }
                
                ConsoleWindow.WriteLine($"Job {jobNumber} sent to {targetPrinter} (Spooler ID: {spoolerId})");
                
                // Return success immediately - POS assumes it printed
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    jobNumber = jobNumber,
                    spoolerId = spoolerId,
                    printer = targetPrinter,
                    message = "Print job submitted successfully"
                }, _jsonOptions));
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Print submission error: {ex.Message}");
                
                // Notify about submission failure
                _notifications.ShowNotification(
                    "❌ Print Submission Failed",
                    ex.Message,
                    isError: true
                );
                
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                    success = false, 
                    error = ex.Message 
                }));
            }
        });
        
        // Optional: Manual error check endpoint
        _app.MapGet("/check-errors", async (HttpContext context) =>
        {
            var errors = new List<object>();
            
            foreach (var printer in _printerService.GetPrinterNames())
            {
                var jobs = PrintDirect.GetPrinterJobs(printer);
                var errorJobs = jobs.Where(j => 
                    j.Status.HasFlag(JobStatus.Error) || 
                    j.Status.HasFlag(JobStatus.UserIntervention))
                    .Select(j => new
                    {
                        printer = printer,
                        jobId = j.JobId,
                        document = j.DocumentName,
                        status = j.Status.ToString(),
                        submitTime = j.SubmitTime
                    });
                
                errors.AddRange(errorJobs);
            }
            
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                hasErrors = errors.Any(),
                errors = errors
            }, _jsonOptions));
        });
    }
}
```

### 4. Enhanced Tray Application

```csharp
public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _contextMenu;
    private HttpServer? _httpServer;
    private PrinterService _printerService;
    
    public TrayApplicationContext()
    {
        InitializeTrayIcon();
        InitializeServices();
        StartServer();
    }
    
    private void InitializeTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Printer Tray App - Ready",
            Visible = true
        };
        
        // Create context menu
        _contextMenu = new ContextMenuStrip();
        
        _contextMenu.Items.Add("🖨️ View Print Queue", null, (s, e) => ViewPrintQueue());
        _contextMenu.Items.Add("🔄 Clear Error Notifications", null, (s, e) => ClearNotifications());
        _contextMenu.Items.Add("-");
        _contextMenu.Items.Add("🧪 Test Notification", null, (s, e) => TestNotification());
        _contextMenu.Items.Add("📊 Show Console", null, (s, e) => ConsoleWindow.Show());
        _contextMenu.Items.Add("-");
        _contextMenu.Items.Add("❌ Exit", null, (s, e) => ExitApplication());
        
        _trayIcon.ContextMenuStrip = _contextMenu;
        
        // Handle notification clicks
        _trayIcon.BalloonTipClicked += OnNotificationClicked;
        
        // Double-click shows console
        _trayIcon.DoubleClick += (s, e) => ConsoleWindow.Show();
    }
    
    private void OnNotificationClicked(object? sender, EventArgs e)
    {
        // Open print queue when notification is clicked
        ViewPrintQueue();
    }
    
    private void ViewPrintQueue()
    {
        try
        {
            // Open Windows print queue
            System.Diagnostics.Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /o /n \"" + 
                _printerService.GetPrinterNames().FirstOrDefault() + "\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open print queue: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private void TestNotification()
    {
        var notification = new PrintNotificationService(_trayIcon);
        notification.ShowNotification(
            "🧪 Test Notification",
            "This is a test notification from the Printer Tray App",
            isError: false
        );
    }
    
    private void ClearNotifications()
    {
        // Clear the notification history
        _trayIcon.BalloonTipTitle = "";
        _trayIcon.BalloonTipText = "";
        ConsoleWindow.WriteLine("Notification history cleared");
    }
}
```

### 5. Configuration Settings

```json
{
  "Notifications": {
    "Enabled": true,
    "ErrorCheckInterval": 2000,
    "NotificationTimeout": 3000,
    "PlaySoundOnError": true,
    "ShowSuccessNotifications": false,
    "MaxRecentNotifications": 50,
    "ErrorTypes": {
      "PrinterOffline": true,
      "PaperOut": true,
      "PrintError": true,
      "QueueJammed": true,
      "UserIntervention": true,
      "CashDrawer": true
    }
  }
}
```

## Complete Flow with POS Updates

### Setup - POS Registers Callback
```csharp
// POS registers its callback URL when starting
POST /register-callback
{
    "callbackUrl": "http://pos.local:8080/print-status",
    "apiKey": "pos-api-key-123"
}
```

### Normal Operation (99% of cases)
```
1. POS sends print job → /print endpoint (includes callback URL)
2. Job sent to spooler → Returns success immediately  
3. POS marks as printed → Continues operating
4. Print completes normally → No notification, no callback
```

### Error Case with POS Update (1% of cases)
```
1. POS sends print job → /print endpoint (includes callback URL)
2. Job sent to spooler → Returns success immediately
3. POS marks as printed → Continues operating
4. Printer has issue → Error detected by monitor
5. Windows notification → "🔌 Printer Offline: Kitchen printer"
6. Callback to POS → Updates task status to "failed"
7. Staff sees notification → Fixes printer
8. Print resumes → Callback to POS with "completed"
```

## Implementation with POS Callbacks

### 1. Enhanced Print Monitor with Callbacks

```csharp
public class PrintMonitorService
{
    private readonly PrintNotificationService _notifications;
    private readonly PosCallbackService _posCallback;
    private readonly Dictionary<string, JobTrackingInfo> _trackedJobs = new();
    private readonly HashSet<string> _notifiedJobs = new();
    
    public PrintMonitorService(
        PrintNotificationService notifications,
        PosCallbackService posCallback,
        PrinterService printerService)
    {
        _notifications = notifications;
        _posCallback = posCallback;
        _printerService = printerService;
    }
    
    // Track a new job with callback info
    public void TrackJob(string jobNumber, int spoolerId, string printerName, string? callbackUrl)
    {
        lock (_trackedJobs)
        {
            _trackedJobs[jobNumber] = new JobTrackingInfo
            {
                JobNumber = jobNumber,
                SpoolerId = spoolerId,
                PrinterName = printerName,
                CallbackUrl = callbackUrl,
                Status = "printing",
                SubmittedAt = DateTime.UtcNow
            };
        }
    }
    
    private async Task MonitorPrintJobs(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllJobs();
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Monitor error: {ex.Message}");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }
    
    private async Task CheckAllJobs()
    {
        var currentSpoolerJobs = new HashSet<string>();
        
        // Get all jobs currently in spooler
        foreach (var printer in _printerService.GetPrinterNames())
        {
            var jobs = PrintDirect.GetPrinterJobs(printer);
            
            foreach (var job in jobs)
            {
                currentSpoolerJobs.Add(job.DocumentName);
                
                // Check for errors
                if (job.Status.HasFlag(JobStatus.Error) || 
                    job.Status.HasFlag(JobStatus.PaperOut) ||
                    job.Status.HasFlag(JobStatus.Offline))
                {
                    await HandleJobError(job.DocumentName, job.Status, printer);
                }
            }
        }
        
        // Check for completed jobs (no longer in spooler)
        var trackedJobNumbers = _trackedJobs.Keys.ToList();
        foreach (var jobNumber in trackedJobNumbers)
        {
            if (!currentSpoolerJobs.Contains(jobNumber))
            {
                await HandleJobCompleted(jobNumber);
            }
        }
        
        // Clean up old tracked jobs (older than 5 minutes)
        CleanupOldJobs();
    }
    
    private async Task HandleJobError(string jobNumber, JobStatus status, string printer)
    {
        if (!_trackedJobs.TryGetValue(jobNumber, out var jobInfo))
            return;
            
        // Only notify once per error
        var errorKey = $"{jobNumber}:error";
        if (_notifiedJobs.Contains(errorKey))
            return;
            
        _notifiedJobs.Add(errorKey);
        
        // Determine error type
        var errorMessage = GetErrorMessage(status);
        
        // Update tracked status
        jobInfo.Status = "error";
        jobInfo.Error = errorMessage;
        jobInfo.UpdatedAt = DateTime.UtcNow;
        
        // Show Windows notification
        _notifications.ShowNotification(
            "❌ Print Error",
            $"Job {jobNumber} failed: {errorMessage}",
            isError: true
        );
        
        // Callback to POS
        if (!string.IsNullOrEmpty(jobInfo.CallbackUrl))
        {
            await _posCallback.SendStatusUpdate(jobInfo.CallbackUrl, new PosStatusUpdate
            {
                JobNumber = jobNumber,
                Status = "failed",
                Error = errorMessage,
                PrinterName = printer,
                Timestamp = DateTime.UtcNow
            });
            
            ConsoleWindow.WriteLine($"POS callback sent for failed job {jobNumber}");
        }
    }
    
    private async Task HandleJobCompleted(string jobNumber)
    {
        if (!_trackedJobs.TryGetValue(jobNumber, out var jobInfo))
            return;
            
        // Update status
        jobInfo.Status = "completed";
        jobInfo.CompletedAt = DateTime.UtcNow;
        
        ConsoleWindow.WriteLine($"Job {jobNumber} completed successfully");
        
        // Callback to POS (optional - you might not want this for successful prints)
        if (!string.IsNullOrEmpty(jobInfo.CallbackUrl) && jobInfo.SendCompletionCallback)
        {
            await _posCallback.SendStatusUpdate(jobInfo.CallbackUrl, new PosStatusUpdate
            {
                JobNumber = jobNumber,
                Status = "completed",
                PrinterName = jobInfo.PrinterName,
                Timestamp = DateTime.UtcNow
            });
        }
        
        // Remove from tracking after a delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(30000); // Keep for 30 seconds for status queries
            lock (_trackedJobs)
            {
                _trackedJobs.Remove(jobNumber);
            }
        });
    }
    
    private string GetErrorMessage(JobStatus status)
    {
        if (status.HasFlag(JobStatus.PaperOut)) return "Paper out";
        if (status.HasFlag(JobStatus.Offline)) return "Printer offline";
        if (status.HasFlag(JobStatus.UserIntervention)) return "User intervention required";
        if (status.HasFlag(JobStatus.Error)) return "Printer error";
        return "Unknown error";
    }
}

public class JobTrackingInfo
{
    public string JobNumber { get; set; } = "";
    public int SpoolerId { get; set; }
    public string PrinterName { get; set; } = "";
    public string? CallbackUrl { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool SendCompletionCallback { get; set; } = false; // Usually false
}
```

### 2. POS Callback Service

```csharp
public class PosCallbackService
{
    private readonly HttpClient _httpClient;
    private readonly string? _defaultCallbackUrl;
    private readonly string? _apiKey;
    
    public PosCallbackService(IConfiguration config)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5); // Short timeout
        
        // Can be configured globally
        _defaultCallbackUrl = config["POS:CallbackUrl"];
        _apiKey = config["POS:ApiKey"];
    }
    
    public async Task SendStatusUpdate(string callbackUrl, PosStatusUpdate update)
    {
        try
        {
            var url = callbackUrl ?? _defaultCallbackUrl;
            if (string.IsNullOrEmpty(url))
                return;
                
            var json = JsonSerializer.Serialize(update);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Add authentication if configured
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }
            
            var response = await _httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                ConsoleWindow.WriteLine($"POS callback successful: {update.Status} for {update.JobNumber}");
            }
            else
            {
                ConsoleWindow.WriteError($"POS callback failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            // Don't let callback failures affect printing
            ConsoleWindow.WriteError($"POS callback error: {ex.Message}");
        }
    }
}

public class PosStatusUpdate
{
    public string JobNumber { get; set; } = "";
    public string Status { get; set; } = ""; // failed, completed, error
    public string? Error { get; set; }
    public string PrinterName { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
```

### 3. Updated Print Endpoint

```csharp
_app.MapPost("/print", async (HttpContext context) =>
{
    try
    {
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<PrintRequest>(body);
        
        // Generate job number
        var jobNumber = GenerateJobNumber();
        
        // Process print data
        var printData = await ProcessPrintTask(request.PrinterTask, jobNumber);
        
        // Submit to spooler
        var targetPrinter = SelectPrinter(request.PrinterTask);
        int spoolerId = PrintDirect.Print(targetPrinter, jobNumber, "RAW", printData);
        
        // Track the job with callback URL
        _monitor.TrackJob(
            jobNumber, 
            spoolerId, 
            targetPrinter,
            request.CallbackUrl ?? _defaultCallbackUrl
        );
        
        // Return success immediately
        return new
        {
            success = true,
            jobNumber = jobNumber,
            spoolerId = spoolerId,
            printer = targetPrinter
        };
    }
    catch (Exception ex)
    {
        // Immediate failure - notify POS right away
        if (!string.IsNullOrEmpty(request?.CallbackUrl))
        {
            await _posCallback.SendStatusUpdate(request.CallbackUrl, new PosStatusUpdate
            {
                JobNumber = "unknown",
                Status = "failed",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        
        return new { success = false, error = ex.Message };
    }
});

public class PrintRequest
{
    public PrinterTask PrinterTask { get; set; }
    public string? CallbackUrl { get; set; }  // Optional POS callback URL
}
```

### 4. POS Implementation

```javascript
// POS-side implementation
class PrintService {
    constructor() {
        this.callbackUrl = 'http://localhost:8080/api/print-status';
        this.pendingJobs = new Map();
    }
    
    async printReceipt(printerTask) {
        const request = {
            printerTask: printerTask,
            callbackUrl: this.callbackUrl
        };
        
        try {
            // Send print job
            const response = await fetch('http://127.0.0.1:9877/print', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });
            
            const result = await response.json();
            
            if (result.success) {
                // Mark as printed optimistically
                this.updateTaskStatus(printerTask.id, 'printed', result.jobNumber);
                
                // Track for potential error callbacks
                this.pendingJobs.set(result.jobNumber, printerTask.id);
                
                // Auto-cleanup after 5 minutes
                setTimeout(() => this.pendingJobs.delete(result.jobNumber), 300000);
                
                return { success: true, jobNumber: result.jobNumber };
            } else {
                // Immediate failure
                this.updateTaskStatus(printerTask.id, 'failed', null, result.error);
                return { success: false, error: result.error };
            }
        } catch (error) {
            this.updateTaskStatus(printerTask.id, 'failed', null, error.message);
            return { success: false, error: error.message };
        }
    }
    
    // Endpoint to receive callbacks from printer service
    async handlePrintStatusCallback(req, res) {
        const { jobNumber, status, error, timestamp } = req.body;
        
        console.log(`Print status update: Job ${jobNumber} is ${status}`);
        
        // Find the original task
        const taskId = this.pendingJobs.get(jobNumber);
        if (taskId) {
            if (status === 'failed' || status === 'error') {
                // Update POS database - mark task as failed
                this.updateTaskStatus(taskId, 'failed', jobNumber, error);
                
                // Alert staff
                this.alertStaff(`Print failed: ${error}`, taskId);
                
                // Remove from pending
                this.pendingJobs.delete(jobNumber);
            } else if (status === 'completed') {
                // Optional: Update completion time
                this.updateTaskCompletionTime(taskId, timestamp);
                this.pendingJobs.delete(jobNumber);
            }
        }
        
        res.json({ received: true });
    }
    
    updateTaskStatus(taskId, status, jobNumber, error) {
        // Update your POS database
        db.query(
            'UPDATE printer_tasks SET status = ?, job_number = ?, error = ?, updated_at = NOW() WHERE id = ?',
            [status, jobNumber, error, taskId]
        );
        
        // Update UI if needed
        this.emit('taskStatusChanged', { taskId, status, error });
    }
    
    alertStaff(message, taskId) {
        // Show alert in POS UI
        ui.showAlert({
            type: 'error',
            title: 'Print Failed',
            message: message,
            actions: [
                { label: 'Retry', action: () => this.retryTask(taskId) },
                { label: 'Cancel', action: () => this.cancelTask(taskId) }
            ]
        });
    }
}

// Express endpoint for callbacks
app.post('/api/print-status', (req, res) => {
    printService.handlePrintStatusCallback(req, res);
});
```

## Testing

### Test Script
```powershell
# Test successful print
$json = @'
{
    "_id": {"id": "test-001"},
    "template": {
        "body": "<root><text>Test Print</text></root>",
        "name": "Test"
    },
    "templateData": "{\"printerDeviceName\":\"Microsoft Print to PDF\"}",
    "isOpenCashDrawer": false
}
'@

$response = Invoke-RestMethod -Uri "http://127.0.0.1:9877/print" -Method Post -Body $json -ContentType "application/json"
Write-Host "Print submitted: $($response.success)"

# Check for errors
Start-Sleep -Seconds 5
$errors = Invoke-RestMethod -Uri "http://127.0.0.1:9877/check-errors" -Method Get
if ($errors.hasErrors) {
    Write-Host "Errors detected: $($errors.errors | ConvertTo-Json)"
} else {
    Write-Host "No errors - print successful"
}
```

## Benefits

1. **Simple Architecture** - No complex status tracking needed
2. **Non-blocking POS** - POS never waits for print completion  
3. **Immediate Error Alerts** - Staff notified instantly of issues
4. **Zero Database** - No persistence layer required
5. **Windows Native** - Uses built-in notification system
6. **User-Friendly** - Click notification to open print queue

## Common Notifications

| Notification | When | Action Required |
|-------------|------|-----------------|
| 🔌 Printer Offline | Printer disconnected | Check cable/network |
| 📄 Paper Out | No paper detected | Load paper |
| ❌ Print Failed | Job error | Check printer display |
| 🚨 Queue Jammed | >10 stuck jobs | Clear print queue |
| ⚠️ User Intervention | Cover open, jam | Check printer |
| 💰 Cash Drawer | Drawer opened | None (info only) |

## Summary

This implementation provides:
- **Optimistic printing** - POS assumes success
- **Error notifications** - Windows alerts for problems only
- **Simple monitoring** - 2-second check interval
- **No database** - Everything in memory
- **Staff-friendly** - Clear notifications with actionable messages

The POS can operate at full speed while staff get instant alerts if anything goes wrong!