# 📋 Complete POS-Printer Integration Plan

## 🚀 TLDR - How Printing Works

**Simple Flow:**
1. **POS** creates PrinterTask with unique ID (`_id.id` field)
2. **POS** sends to Printer App at `http://127.0.0.1:9877/print`
3. **Printer App** uses the ID as Windows spooler document name (prevents duplicates)
4. **Printer App** returns success with `hasErrors` flag if OTHER jobs have problems
5. **POS** polls `/check-queue` every 20 seconds to see ALL jobs and their status
6. **Jobs disappear from queue = printed successfully**

**Key Points:**
- **No database needed** - Windows spooler is the source of truth
- **Duplicate prevention** - GUID in document name stops double prints
- **Error detection** - Success response includes `hasErrors` flag for queue problems
- **Stuck detection** - Jobs not printing for >30 seconds are flagged
- **Complete visibility** - `/check-queue` shows ALL jobs with age and status

**What POS Needs:**
- Store PrinterTask ID and Windows Spooler ID (optional)
- Check `/check-queue` when `hasErrors=true` in print response
- Poll status every 20 seconds for active jobs
- Launch printer app if not running (startup)

---

## Overview
This document outlines the complete integration between the POS system and the Windows Printer Tray App, including all retry logic, status tracking, and startup procedures.

---

## 🚀 POS Startup Requirements

### 1. Check and Launch Printer App

```javascript
// POS Startup Procedure
class POSPrinterManager {
    constructor() {
        this.printerAppPath = 'C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe';
        this.printerApiUrl = 'http://127.0.0.1:9877';
        this.checkInterval = null;
        this.isConnected = false;
    }
    
    async initialize() {
        // 1. Check if printing is enabled in POS settings
        if (!this.isPrintingEnabled()) {
            console.log('Printing disabled in settings');
            return;
        }
        
        // 2. Check if printer app is running
        const isRunning = await this.checkPrinterApp();
        
        if (!isRunning) {
            // 3. Try to launch printer app
            await this.launchPrinterApp();
            
            // 4. Wait for app to be ready
            await this.waitForReady();
        }
        
        // 5. Start monitoring
        this.startMonitoring();
    }
    
    async checkPrinterApp() {
        try {
            const response = await fetch(`${this.printerApiUrl}/health`, {
                timeout: 1000
            });
            return response.ok;
        } catch {
            return false;
        }
    }
    
    async launchPrinterApp() {
        try {
            // Windows-specific launch
            const { exec } = require('child_process');
            
            // Check if exe exists
            const fs = require('fs');
            if (!fs.existsSync(this.printerAppPath)) {
                console.error('Printer app not found at:', this.printerAppPath);
                this.showError('Printer application not installed');
                return false;
            }
            
            // Launch the app
            exec(`"${this.printerAppPath}"`, (error) => {
                if (error) {
                    console.error('Failed to launch printer app:', error);
                    this.showError('Could not start printer service');
                }
            });
            
            console.log('Launching printer app...');
            return true;
        } catch (error) {
            console.error('Error launching printer app:', error);
            return false;
        }
    }
    
    async waitForReady(maxAttempts = 30) {
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 1000));
            
            if (await this.checkPrinterApp()) {
                console.log('Printer app is ready');
                this.isConnected = true;
                return true;
            }
        }
        
        console.error('Printer app failed to start');
        this.showError('Printer service not responding');
        return false;
    }
    
    startMonitoring() {
        // Check connection every 30 seconds
        this.checkInterval = setInterval(async () => {
            const wasConnected = this.isConnected;
            this.isConnected = await this.checkPrinterApp();
            
            if (!wasConnected && this.isConnected) {
                console.log('Printer app reconnected');
                this.onReconnected();
            } else if (wasConnected && !this.isConnected) {
                console.log('Printer app disconnected');
                this.onDisconnected();
            }
        }, 30000);
    }
}
```

### 2. POS Configuration Requirements

```json
// POS config.json or database settings
{
    "printing": {
        "enabled": true,
        "printerAppPath": "C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe",
        "printerApiUrl": "http://127.0.0.1:9877",
        "autoLaunchPrinterApp": true,
        "defaultPrinter": "Kitchen",
        "retryEnabled": true,
        "statusPollingInterval": 20000,
        "connectionCheckInterval": 30000
    }
}
```

---

## 🔄 Complete Integration Architecture

### Core Concepts Implemented

1. **Serial/Synchronous Processing** - Simple, reliable confirmation
2. **GUID-Based Tracking** - Prevent duplicate prints
3. **Smart Retry Logic** - Handle errors intelligently  
4. **Queue Management** - Retry stuck jobs
5. **Status Polling** - Track success and failures
6. **Auto-Launch** - Start printer app if needed
7. **Connection Monitoring** - Detect disconnections

### Why Serial Processing Works Well

- **Immediate Confirmation** - POS knows task reached printer service
- **Simple Flow** - No complex async handling needed
- **Fast Enough** - Submission takes <100ms
- **Clear Success/Failure** - Instant feedback if service is down
- **Natural Queue** - Windows spooler handles queuing

---

## 📦 POS Implementation Requirements

### 1. Database Schema Changes

```sql
-- Add to printer_tasks table
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS job_number VARCHAR(50);
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS retry_count INT DEFAULT 0;
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS last_error TEXT;
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS printed_at TIMESTAMP;
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS completed_at TIMESTAMP;
ALTER TABLE printer_tasks ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'pending';
-- Status values: pending, printing, successful, failed, paused

-- Index for performance
CREATE INDEX idx_printer_tasks_status ON printer_tasks(status);
CREATE INDEX idx_printer_tasks_job_number ON printer_tasks(job_number);
```

### 2. Complete POS Print Service (POS Handles Retries)

```javascript
class POSPrintService {
    constructor() {
        this.printerManager = new POSPrinterManager();
        this.apiUrl = 'http://127.0.0.1:9877';
        this.pollingInterval = null;
        this.maxRetries = 3;
        
        // Initialize on startup
        this.initialize();
    }
    
    async initialize() {
        // 1. Try to launch printer app if needed (may fail)
        try {
            await this.printerManager.initialize();
        } catch (error) {
            console.error('Printer app not available at startup:', error);
            // Continue anyway - will retry on print attempts
        }
        
        // 2. Start status polling (every 20 seconds)
        this.startStatusPolling();
        
        // 3. Check for any pending/failed prints from last session
        await this.retryPendingPrints();
    }
    
    // ========================================
    // MAIN PRINT FUNCTION (New or Retry)
    // Serial/Synchronous - Gets immediate response
    // ========================================
    async print(taskId, printerTask) {
        try {
            // Ensure printer app is running
            if (!this.printerManager.isConnected) {
                await this.printerManager.initialize();
                if (!this.printerManager.isConnected) {
                    throw new Error('Printer service not available');
                }
            }
            
            // Build request with GUID
            const request = {
                posGuid: taskId,  // Use task ID as GUID
                printerName: printerTask.printerName || 'Kitchen',
                printerTask: printerTask
            };
            
            // Send to printer service - SYNCHRONOUS CALL
            // This waits for the service to confirm receipt
            const response = await fetch(`${this.apiUrl}/print`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request),
                timeout: 5000  // 5 second timeout
            });
            
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }
            
            const result = await response.json();
            
            // IMMEDIATE RESPONSE - We know the service got it
            // The service has already:
            // 1. Checked for duplicates by GUID
            // 2. Submitted to Windows spooler if new
            // 3. Returned status
            
            if (result.success) {
                // SUCCESS - Task reached printer service and was submitted to spooler
                // Update database - mark as printing
                await this.updateTaskStatus(taskId, {
                    status: 'printing',
                    job_number: result.jobNumber,
                    retry_count: result.isRetry ? 
                        await this.incrementRetryCount(taskId) : 0,
                    printed_at: new Date()
                });
                
                // Show appropriate message
                if (result.action === 'retried_queue') {
                    this.showSuccess('Retried existing job in queue');
                } else if (result.isRetry) {
                    this.showSuccess('Print job retried');
                } else {
                    this.showSuccess('Print job sent to spooler');
                }
                
                return { 
                    success: true, 
                    jobNumber: result.jobNumber,
                    confirmed: true  // Service confirmed receipt
                };
                
            } else {
                // Check why it failed
                if (result.alreadyCompleted) {
                    // Already printed successfully!
                    await this.updateTaskStatus(taskId, {
                        status: 'successful',
                        completed_at: new Date()
                    });
                    
                    this.showInfo('Already printed successfully');
                    return { success: true, alreadyDone: true };
                    
                } else if (result.action === 'Wait') {
                    // Currently printing
                    this.showWarning('Job is currently printing - please wait');
                    return { success: false, reason: 'printing' };
                    
                } else {
                    // Other error
                    throw new Error(result.message);
                }
            }
            
        } catch (error) {
            // Failed to reach printer service or service error
            // This means the task DID NOT make it to the printer
            await this.updateTaskStatus(taskId, {
                status: 'failed',
                last_error: error.message
            });
            
            this.showError(`Print failed: ${error.message}`);
            return { 
                success: false, 
                error: error.message,
                confirmed: false  // Service never got it
            };
        }
    }
    
    // ========================================
    // STATUS POLLING (Every 20 seconds)
    // ========================================
    startStatusPolling() {
        this.pollingInterval = setInterval(async () => {
            await this.checkPrintStatuses();
        }, 20000);
    }
    
    async checkPrintStatuses() {
        try {
            // Don't poll if not connected
            if (!this.printerManager.isConnected) {
                return;
            }
            
            // Get status updates from printer service
            const response = await fetch(`${this.apiUrl}/status`, {
                timeout: 5000
            });
            
            if (!response.ok) return;
            
            const { failed, completed } = await response.json();
            
            // Update failed jobs
            if (failed && failed.length > 0) {
                for (const job of failed) {
                    await this.updateTaskByGuid(job.posTaskId, {
                        status: 'failed',
                        last_error: job.error
                    });
                    
                    // Show notification
                    this.showError(`Print failed: ${job.error}`);
                }
            }
            
            // Update successful jobs
            if (completed && completed.length > 0) {
                for (const job of completed) {
                    await this.updateTaskByGuid(job.posTaskId, {
                        status: 'successful',
                        completed_at: new Date()
                    });
                    
                    console.log(`Print ${job.jobNumber} completed`);
                }
            }
            
        } catch (error) {
            console.error('Status poll failed:', error);
        }
    }
    
    // ========================================
    // POS-CONTROLLED RETRY LOGIC
    // ========================================
    
    // POS decides when and how to retry
    async retryPrint(taskId) {
        // Get task data from POS database
        const task = await this.getTask(taskId);
        if (!task) {
            this.showError('Task not found');
            return;
        }
        
        // Check retry count
        if (task.retry_count >= this.maxRetries) {
            this.showError('Maximum retries exceeded');
            await this.updateTaskStatus(taskId, {
                status: 'failed_permanent',
                last_error: 'Max retries exceeded'
            });
            return;
        }
        
        // Check if printer app is running, try to start if not
        if (!this.printerManager.isConnected) {
            console.log('Printer app not running, attempting to start...');
            try {
                await this.printerManager.launchPrinterApp();
                await this.printerManager.waitForReady();
            } catch (error) {
                console.error('Could not start printer app:', error);
                // Continue anyway - let the print attempt fail
            }
        }
        
        // Attempt print (POS controls the retry)
        const result = await this.print(taskId, task.printer_data);
        
        if (!result.success && !result.alreadyDone) {
            // Failed again - POS decides what to do
            if (task.retry_count + 1 >= this.maxRetries) {
                await this.updateTaskStatus(taskId, {
                    status: 'failed_permanent',
                    last_error: result.error
                });
                this.showError('Print failed after maximum retries');
            } else {
                // Will retry again later
                this.showWarning(`Print failed, ${this.maxRetries - task.retry_count - 1} retries remaining`);
            }
        }
        
        return result;
    }
    
    // Automatic retry for failed tasks (POS controlled)
    async autoRetryFailedTasks() {
        // POS decides which tasks to retry
        const failedTasks = await db.query(
            `SELECT * FROM printer_tasks 
             WHERE status = 'failed' 
             AND retry_count < ?
             AND updated_at < NOW() - INTERVAL '1 minute'
             ORDER BY created_at`,
            [this.maxRetries]
        );
        
        for (const task of failedTasks) {
            console.log(`Auto-retrying task ${task.id} (attempt ${task.retry_count + 1})`);
            await this.retryPrint(task.id);
            
            // Wait between retries to avoid overwhelming
            await new Promise(resolve => setTimeout(resolve, 2000));
        }
    }
    
    // Retry all errored jobs on a printer (after paper added)
    async retryPrinterQueue(printerName) {
        try {
            const response = await fetch(`${this.apiUrl}/retry-queue`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ printerName }),
                timeout: 5000
            });
            
            const result = await response.json();
            
            if (result.success && result.retriedCount > 0) {
                this.showSuccess(`Retried ${result.retriedCount} jobs on ${printerName}`);
                
                // Update task statuses
                for (const jobNumber of result.retriedJobs) {
                    await this.updateTaskByJobNumber(jobNumber, {
                        status: 'printing',
                        retry_count: db.raw('retry_count + 1')
                    });
                }
            }
            
            return result;
            
        } catch (error) {
            this.showError(`Queue retry failed: ${error.message}`);
            return { success: false, error: error.message };
        }
    }
    
    // Retry pending prints from last session
    async retryPendingPrints() {
        const pendingTasks = await db.query(
            'SELECT * FROM printer_tasks WHERE status IN (?, ?) AND created_at > ?',
            ['printing', 'pending', new Date(Date.now() - 24*60*60*1000)]
        );
        
        console.log(`Found ${pendingTasks.length} pending print tasks`);
        
        for (const task of pendingTasks) {
            // Check if still needed
            if (task.retry_count < 3) {
                await this.print(task.id, task.printer_data);
            }
        }
    }
    
    // ========================================
    // DATABASE HELPERS
    // ========================================
    async updateTaskStatus(taskId, updates) {
        const fields = Object.keys(updates)
            .map(key => `${key} = ?`)
            .join(', ');
        
        const values = Object.values(updates);
        values.push(taskId);
        
        await db.query(
            `UPDATE printer_tasks SET ${fields}, updated_at = NOW() WHERE id = ?`,
            values
        );
    }
    
    async updateTaskByGuid(guid, updates) {
        // GUID is the task ID
        await this.updateTaskStatus(guid, updates);
    }
    
    async updateTaskByJobNumber(jobNumber, updates) {
        const fields = Object.keys(updates)
            .map(key => `${key} = ?`)
            .join(', ');
        
        const values = Object.values(updates);
        values.push(jobNumber);
        
        await db.query(
            `UPDATE printer_tasks SET ${fields}, updated_at = NOW() WHERE job_number = ?`,
            values
        );
    }
    
    async incrementRetryCount(taskId) {
        const result = await db.query(
            'UPDATE printer_tasks SET retry_count = retry_count + 1 WHERE id = ? RETURNING retry_count',
            [taskId]
        );
        return result[0]?.retry_count || 1;
    }
    
    // ========================================
    // UI HELPERS
    // ========================================
    showSuccess(message) {
        ui.toast({ type: 'success', message, duration: 3000 });
    }
    
    showError(message) {
        ui.toast({ type: 'error', message, duration: 5000 });
        ui.log('error', message);
    }
    
    showWarning(message) {
        ui.toast({ type: 'warning', message, duration: 4000 });
    }
    
    showInfo(message) {
        ui.toast({ type: 'info', message, duration: 3000 });
    }
}

// Initialize on POS startup
const printService = new POSPrintService();
```

### 3. UI Integration

```javascript
// POS UI Components

// 1. Print button handler
async function onPrintClick(orderId) {
    const task = await createPrintTask(orderId);
    const result = await printService.print(task.id, task.printer_data);
    
    if (result.success) {
        updateOrderUI(orderId, 'printed');
    }
}

// 2. Retry button handler
async function onRetryClick(taskId) {
    const result = await printService.retryPrint(taskId);
    
    if (result.success) {
        updateTaskUI(taskId, 'printing');
    }
}

// 3. Paper added handler
async function onPaperAdded(printerName) {
    ui.showLoader('Retrying print queue...');
    const result = await printService.retryPrinterQueue(printerName);
    ui.hideLoader();
    
    if (result.success) {
        refreshPrintQueue();
    }
}

// 4. Status indicators
function updatePrintStatusIndicator(status) {
    const indicator = document.getElementById('print-status');
    
    switch(status) {
        case 'connected':
            indicator.className = 'status-green';
            indicator.title = 'Printer service connected';
            break;
        case 'disconnected':
            indicator.className = 'status-red';
            indicator.title = 'Printer service not available';
            break;
        case 'error':
            indicator.className = 'status-yellow';
            indicator.title = 'Print errors detected';
            break;
    }
}
```

---

## 🖨️ Printer Service Implementation

### Complete Printer Service with All Features

```csharp
public class CompletePrinterService
{
    private readonly Dictionary<string, TrackedJob> _jobsByGuid = new();
    private readonly Dictionary<string, JobStatus> _currentStatuses = new();
    private readonly object _lock = new();
    
    // ========================================
    // MAIN PRINT ENDPOINT
    // ========================================
    [HttpPost("/print")]
    public async Task<IActionResult> Print([FromBody] PrintRequest request)
    {
        lock (_lock)
        {
            // Check if job exists by GUID
            if (_jobsByGuid.TryGetValue(request.PosGuid, out var existing))
            {
                switch (existing.Status)
                {
                    case "completed":
                        return Json(new {
                            success = false,
                            action = "AlreadyDone",
                            alreadyCompleted = true,
                            message = "Already printed successfully"
                        });
                        
                    case "printing":
                        // Check if stuck
                        if ((DateTime.UtcNow - existing.StartedAt).TotalMinutes > 2)
                        {
                            // Stuck - allow retry
                            break;
                        }
                        return Json(new {
                            success = false,
                            action = "Wait",
                            message = "Currently printing"
                        });
                        
                    case "error":
                        // Try to retry in queue first
                        if (TryRetryInQueue(existing.JobNumber))
                        {
                            existing.Status = "printing";
                            existing.RetryCount++;
                            
                            return Json(new {
                                success = true,
                                action = "retried_queue",
                                jobNumber = existing.JobNumber,
                                message = "Retried existing job in queue"
                            });
                        }
                        
                        // Clear and print new
                        ClearFromSpooler(existing.JobNumber);
                        _jobsByGuid.Remove(request.PosGuid);
                        break;
                }
            }
            
            // Print new job
            var jobNumber = $"PRT-{DateTime.Now:yyyyMMddHHmmss}";
            if (existing?.RetryCount > 0)
                jobNumber += "-R";
            
            // Generate print commands
            var printData = GeneratePrintCommands(request.PrinterTask);
            
            // Send to spooler
            int spoolerId = PrintDirect.Print(
                request.PrinterName ?? "Kitchen",
                jobNumber,
                "RAW",
                printData
            );
            
            // Track job
            _jobsByGuid[request.PosGuid] = new TrackedJob
            {
                PosGuid = request.PosGuid,
                JobNumber = jobNumber,
                SpoolerId = spoolerId,
                Status = "printing",
                StartedAt = DateTime.UtcNow,
                RetryCount = existing?.RetryCount ?? 0
            };
            
            return Json(new {
                success = true,
                jobNumber = jobNumber,
                action = existing != null ? "Retry" : "Print",
                isRetry = existing != null
            });
        }
    }
    
    // ========================================
    // STATUS POLLING ENDPOINT
    // ========================================
    [HttpGet("/status")]
    public IActionResult GetStatus()
    {
        lock (_lock)
        {
            var failed = new List<object>();
            var completed = new List<object>();
            
            foreach (var job in _jobsByGuid.Values)
            {
                if (job.Status == "error" && !job.ErrorReported)
                {
                    failed.Add(new {
                        posTaskId = job.PosGuid,
                        jobNumber = job.JobNumber,
                        error = job.Error
                    });
                    job.ErrorReported = true;
                }
                else if (job.Status == "completed" && !job.CompletionReported)
                {
                    completed.Add(new {
                        posTaskId = job.PosGuid,
                        jobNumber = job.JobNumber
                    });
                    job.CompletionReported = true;
                }
            }
            
            return Json(new { failed, completed });
        }
    }
    
    // ========================================
    // RETRY QUEUE ENDPOINT
    // ========================================
    [HttpPost("/retry-queue")]
    public IActionResult RetryQueue([FromBody] RetryQueueRequest request)
    {
        var retriedJobs = new List<string>();
        var jobs = PrintDirect.GetPrinterJobs(request.PrinterName);
        
        foreach (var job in jobs)
        {
            if (job.Status.HasFlag(JobStatus.Error) || 
                job.Status.HasFlag(JobStatus.Paused))
            {
                if (PrintDirect.ResumeJob(request.PrinterName, job.JobId))
                {
                    retriedJobs.Add(job.DocumentName);
                    
                    // Update tracking
                    lock (_lock)
                    {
                        var tracked = _jobsByGuid.Values
                            .FirstOrDefault(j => j.JobNumber == job.DocumentName);
                        if (tracked != null)
                        {
                            tracked.Status = "printing";
                            tracked.RetryCount++;
                        }
                    }
                }
            }
        }
        
        return Json(new {
            success = retriedJobs.Count > 0,
            retriedCount = retriedJobs.Count,
            retriedJobs = retriedJobs
        });
    }
    
    // ========================================
    // BACKGROUND SCANNER
    // ========================================
    private void ScanJobs()
    {
        while (true)
        {
            Thread.Sleep(2000);
            
            var spoolerJobs = new Dictionary<string, JobStatus>();
            
            // Get all jobs from spooler
            foreach (var printer in GetPrinters())
            {
                var jobs = PrintDirect.GetPrinterJobs(printer);
                foreach (var job in jobs)
                {
                    spoolerJobs[job.DocumentName] = job.Status;
                }
            }
            
            // Update tracked jobs
            lock (_lock)
            {
                foreach (var tracked in _jobsByGuid.Values)
                {
                    if (spoolerJobs.TryGetValue(tracked.JobNumber, out var status))
                    {
                        // Still in spooler
                        if (status.HasFlag(JobStatus.Error))
                        {
                            tracked.Status = "error";
                            tracked.Error = GetErrorMessage(status);
                        }
                        else if (status.HasFlag(JobStatus.Printing))
                        {
                            tracked.Status = "printing";
                        }
                    }
                    else
                    {
                        // Not in spooler = completed
                        if (tracked.Status != "completed")
                        {
                            tracked.Status = "completed";
                            tracked.CompletedAt = DateTime.UtcNow;
                        }
                    }
                }
                
                // Clean old completed jobs
                var cutoff = DateTime.UtcNow.AddMinutes(-2);
                var toRemove = _jobsByGuid
                    .Where(j => j.Value.Status == "completed" && 
                               j.Value.CompletedAt < cutoff)
                    .Select(j => j.Key)
                    .ToList();
                    
                foreach (var guid in toRemove)
                {
                    _jobsByGuid.Remove(guid);
                }
            }
        }
    }
}
```

---

## 📊 Status Flow Diagram

```
POS Startup
    ↓
Check if printer app running
    ↓ No
Launch printer app
    ↓
Wait for ready
    ↓
Start monitoring
    ↓
┌─────────────────────────────────────┐
│       Main Operation Loop          │
├─────────────────────────────────────┤
│                                     │
│  Print Request (SYNCHRONOUS)       │
│      ↓                              │
│  Send to Printer Service           │
│      ↓                              │
│  Wait for Response (<100ms)        │
│      ↓                              │
│  Response Received:                 │
│      ├─ Success → "Sent to spooler" │
│      ├─ Already Done → Update DB    │
│      ├─ Currently Printing → Wait   │
│      └─ Error → Show message        │
│                                     │
│  POS knows immediately if:          │
│  ✓ Task reached service            │
│  ✓ Task sent to spooler            │
│  ✓ Task was duplicate              │
│                                     │
│  Every 20 seconds (BACKGROUND)     │
│      ↓                              │
│  Poll for status updates           │
│      ├─ Update failed jobs         │
│      └─ Update successful jobs     │
│                                     │
└─────────────────────────────────────┘
```

### Serial Processing Benefits

1. **Immediate Confirmation**
   - POS knows within 100ms if print task was received
   - No uncertainty about whether service got the request

2. **Simple Error Handling**
   ```javascript
   try {
       const result = await print(task);
       // Got here = service received it
   } catch (error) {
       // Service down or network error
       // Task definitely NOT printed
   }
   ```

3. **Natural Flow**
   - User clicks Print → Waits 100ms → Gets confirmation
   - No complex async state management
   - Clear success/failure path

---

## ✅ POS Implementation Checklist

### Required Changes:

- [ ] **Database Schema**
  - [ ] Add status column to printer_tasks
  - [ ] Add job_number column
  - [ ] Add retry_count column
  - [ ] Add printed_at and completed_at timestamps
  - [ ] Add indexes for performance

- [ ] **Startup Procedure**
  - [ ] Check if printing enabled
  - [ ] Check if printer app running
  - [ ] Launch printer app if needed
  - [ ] Wait for app ready
  - [ ] Start connection monitoring

- [ ] **Print Service**
  - [ ] Implement GUID-based printing
  - [ ] Add retry logic
  - [ ] Add status polling (20 seconds)
  - [ ] Handle all response types
  - [ ] Update database status

- [ ] **UI Components**
  - [ ] Print button
  - [ ] Retry button
  - [ ] Status indicators
  - [ ] Error notifications
  - [ ] Paper added handler

- [ ] **Configuration**
  - [ ] Add printer app path setting
  - [ ] Add auto-launch setting
  - [ ] Add polling interval setting
  - [ ] Add default printer setting

### API Endpoints to Use:

1. **POST /print** - Main print/retry endpoint
   - Send: `{ posGuid, printerName, printerTask }`
   - Receive: `{ success, jobNumber, action, message }`

2. **GET /status** - Poll for updates
   - Receive: `{ failed: [], completed: [] }`

3. **POST /retry-queue** - Retry all errors on printer
   - Send: `{ printerName }`
   - Receive: `{ success, retriedCount, retriedJobs }`

4. **GET /health** - Check if service running
   - Receive: `{ ok, version, printers }`

---

## 🔒 Error Handling

### Connection Lost
```javascript
// Auto-reconnect logic
if (!connected) {
    // Queue prints locally
    await queueLocally(printTask);
    
    // Try to reconnect
    await tryReconnect();
    
    // Process queued prints when reconnected
    if (connected) {
        await processQueuedPrints();
    }
}
```

### Printer App Crashes
```javascript
// Auto-restart on next print
try {
    await print(task);
} catch (error) {
    if (error.message.includes('ECONNREFUSED')) {
        // Printer app not running
        await launchPrinterApp();
        await waitForReady();
        await print(task); // Retry
    }
}
```

---

## 📈 Performance Targets

- **Print submission**: < 100ms
- **Status check**: < 10ms  
- **GUID lookup**: < 1ms
- **Queue retry**: < 500ms
- **App launch**: < 5 seconds

---

## 🎯 Summary

The POS needs to:

1. **On Startup**: Launch printer app if not running
2. **On Print**: Send with GUID to prevent duplicates
3. **On Retry**: Use same print endpoint (auto-handles)
4. **Every 20s**: Poll for status updates
5. **On Paper Added**: Call retry-queue endpoint

The Printer Service handles:
- Duplicate prevention
- Queue management
- Error recovery
- Status tracking
- Spooler interaction

This provides a complete, robust printing solution with automatic recovery and comprehensive error handling.

---
**Document Version**: 2.0  
**Last Updated**: 2025-01-17  
**Status**: Complete Implementation Plan