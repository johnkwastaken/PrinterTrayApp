# 🎯 Final POS-Printer Integration Flow
**Complete Implementation with GUID & Spooler ID Tracking**

---

## 📌 API Endpoints Summary

| Endpoint | Method | Purpose | Returns |
|----------|---------|---------|---------|
| `/health` | GET | Quick health check with printer status | Server status, printer list with online/offline state, `hasPrinterIssues` flag |
| `/printers` | GET | Detailed printer discovery | All printers with full status, capabilities, port info, `hasPrinterIssues` flag |
| `/print` | POST | Submit print job | GUID, Spooler ID, `hasErrors` flag, `hasPrinterIssues` flag |
| `/status` | GET | Get all active print jobs | List of all jobs in spooler with status |
| `/status/guid/{guid}` | GET | Check specific job by GUID | Job status or "not found" if completed |
| `/status/spooler/{id}` | GET | Check specific job by Spooler ID | Job status or "not found" if completed |
| `/check-queue` | GET | Complete queue analysis | All jobs with age, stuck detection, error details |
| `/retry-queue` | POST | Retry all errored jobs on a printer | Count of retried jobs |

## 🚀 TLDR - How Printing Works

**Simple Flow:**
1. **POS** creates PrinterTask with unique ID (`_id.id` field - already exists!)
2. **POS** sends to Printer App at `http://127.0.0.1:9877/print`
3. **Printer App** uses the ID as Windows spooler document name (prevents duplicates)
4. **Printer App** returns success with `hasErrors` flag if OTHER jobs have problems
5. **POS** polls `/check-queue` every 20 seconds to see ALL jobs and their status
6. **Jobs disappear from queue = printed successfully**

**Key Points:**
- **No database needed in Printer App** - Windows spooler is the source of truth
- **Duplicate prevention** - GUID in document name stops double prints
- **Error detection** - Success response includes `hasErrors` flag for queue problems
- **Stuck detection** - Jobs not printing for >30 seconds are flagged
- **Complete visibility** - `/check-queue` shows ALL jobs with age and status

---

## 🎯 POS Integration - Quick Summary

### What Changes in POS (5 Functions)

| Old Function | New API | Used In |
|-----|-----|-------|
| `getPrinters()` | `GET /printers` | Settings page discovery |
| `getPrinterStatus()` | `GET /printers` or `/health` | refreshLocalStatus |
| `printDirect()` | `POST /print` | Print submission |
| `getPrinterJob()` | `GET /status/spooler/{id}` | Job monitoring |
| `sendJobCommand()` | `POST /retry-queue` | Retry failed jobs |

### Code Changes Required

1. **Add TrayAppService.ts** - One service file to handle all API calls
2. **Replace print submission**: `printDirect()` → `trayApp.print()`
3. **Replace discovery**: `getPrinters()` → `trayApp.getPrinters()`
4. **Replace status check**: Loop of `getPrinterStatus()` → One `trayApp.getPrinters()` call

### What Stays the Same
- ✅ PrinterTask structure
- ✅ Ditto sync
- ✅ Retry logic (3 retries)
- ✅ Secondary printer fallback
- ✅ Error handling
- ✅ Database updates

### Benefits
- **No duplicates** - GUID prevents double prints
- **Better performance** - One API call for all printer statuses
- **More info** - Online/offline status, job counts, error messages
- **Fallback support** - Keep native module as backup

---

## 📋 Executive Summary

This document describes the complete integration flow between the POS system and Windows Printer Tray App, utilizing:
- **GUID tracking** using `PrinterTask._id.id` as the document name in Windows spooler
- **Spooler ID tracking** for direct Windows print queue queries
- **Duplicate prevention** by checking GUID existence in spooler
- **Real-time status tracking** through spooler enumeration
- **Automatic completion detection** when jobs disappear from spooler

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                         POS System                           │
│  • Creates PrinterTask with _id.id (GUID)                   │
│  • Sends to Printer App via HTTP                            │
│  • Stores both GUID and Spooler ID                          │
│  • Polls for status updates                                 │
└────────────────────┬────────────────────────────────────────┘
                     │ POST /print
                     │ {PrinterTask with _id.id}
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                    Printer Tray App                          │
│  • Extracts GUID from PrinterTask._id.id                    │
│  • Checks for duplicate GUID in spooler                     │
│  • Submits to Windows using GUID as document name           │
│  • Returns both GUID and Spooler ID to POS                  │
│  • Monitors spooler for status changes                      │
└────────────────────┬────────────────────────────────────────┘
                     │ PrintDirect.Print()
                     │ Document Name = GUID
                     ↓
┌─────────────────────────────────────────────────────────────┐
│                    Windows Spooler                           │
│  • Job ID: 142                                              │
│  • Document: "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"        │
│  • Status: Printing/Error/Complete                          │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ↓
               Physical Printer
```

---

## 🔄 Complete Integration Flow

### Step 1: POS Creates Print Task
```javascript
// POS generates PrinterTask with unique GUID
const printerTask = {
    _id: {
        id: "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",  // UNIQUE GUID
        siteId: "site-001",
        orgId: "org-001"
    },
    template: {
        body: "<root><text>Receipt Content</text></root>",
        name: "Receipt",
        templateType: "Docket"
    },
    templateData: "{\"printerDeviceName\":\"passkitchen\",\"order\":{...}}",
    isOpenCashDrawer: false,
    retryCount: 0
};
```

### Step 2: POS Sends to Printer App
```javascript
const response = await fetch('http://127.0.0.1:9877/print', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(printerTask)
});

const result = await response.json();
// Result contains:
// {
//   success: true,
//   guid: "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
//   spoolerId: 142,
//   printerName: "passkitchen",
//   documentName: "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"
// }
```

### Step 3: Printer App Processing
```csharp
[HttpPost("/print")]
public IActionResult Print([FromBody] PrinterTask printerTask)
{
    // 1. Extract GUID from PrinterTask
    string posGuid = printerTask?._id?.id;
    
    // 2. Check for duplicate in Windows spooler
    var existingJob = CheckGuidInSpooler(posGuid);
    if (existingJob != null)
    {
        // Handle duplicate based on status
        if (existingJob.IsComplete)
            return Json(new { success = false, status = "completed" });
        if (existingJob.IsPrinting)
            return Json(new { success = false, status = "printing" });
        if (existingJob.IsError)
            ClearJobFromSpooler(existingJob);  // Retry
    }
    
    // 3. Process template and generate ESC/POS commands
    var printData = ProcessPrintTask(printerTask);
    
    // 4. Submit to Windows spooler with GUID as document name
    int spoolerId = PrintDirect.Print(
        printerName,
        posGuid,  // Document name = GUID for tracking!
        "RAW",
        printData
    );
    
    // 5. Check if there are ANY errored jobs in the system
    bool hasErroredJobs = CheckForAnyErroredJobs();
    
    // 6. Return both identifiers to POS with error flag
    return Json(new {
        success = true,
        guid = posGuid,
        spoolerId = spoolerId,
        printerName = printerName,
        documentName = posGuid,
        hasErrors = hasErroredJobs  // IMPORTANT: Tells POS to check for errors!
    });
}
```

### Step 4: POS Handles Response and Checks for Errors
```javascript
// POS handles the response
if (result.success) {
    // Store both IDs in database
    await db.query(
        `INSERT INTO printer_tasks 
         (id, spooler_id, printer_name, status, template_data, created_at) 
         VALUES (?, ?, ?, ?, ?, NOW())`,
        [
            result.guid,           // PrinterTask._id.id
            result.spoolerId,      // Windows spooler job ID
            result.printerName,    // Target printer
            'printing',            // Initial status
            JSON.stringify(printerTask)
        ]
    );
    
    // IMPORTANT: Check if there are errored jobs in the system
    if (result.hasErrors) {
        // There are OTHER errored jobs - fetch details
        const erroredJobs = await fetch('http://127.0.0.1:9877/check-errors');
        const errors = await erroredJobs.json();
        
        // Update errored tasks in POS database
        for (const error of errors.jobs) {
            await updateTaskByGuid(error.guid, {
                status: 'error',
                error: error.errorMessage,
                spoolerId: error.spoolerId
            });
        }
    }
}
```

### Step 5: Status Monitoring (Multiple Methods)

#### Method A: Check by GUID
```javascript
// POS checks status using GUID
const status = await fetch(`http://127.0.0.1:9877/status/guid/${guid}`);
// Returns: { found: true/false, status: "printing/completed/error", spoolerId: 142 }
```

#### Method B: Check by Spooler ID
```javascript
// POS checks status using Spooler ID (faster)
const status = await fetch(`http://127.0.0.1:9877/status/spooler/${spoolerId}`);
// Returns: { found: true/false, status: "printing/completed/error", guid: "..." }
```

#### Method C: Bulk Status Check
```javascript
// POS checks all active jobs
const response = await fetch('http://127.0.0.1:9877/status');
const allJobs = await response.json();
// Returns: {
//   totalJobs: 5,
//   jobs: [
//     { guid: "...", spoolerId: 142, status: "printing", printerName: "..." },
//     { guid: "...", spoolerId: 143, status: "error", printerName: "..." }
//   ]
// }
```

### Step 6: Completion Detection
```javascript
// Jobs disappear from spooler when completed successfully
if (!status.found) {
    // Job not in spooler = completed successfully
    await updateTaskStatus(guid, 'completed');
} else if (status.isError) {
    // Job has error
    await updateTaskStatus(guid, 'error', status.errorMessage);
}
```

---

## 🔌 Complete API Reference

### 1. Print Submission
```http
POST /print
Content-Type: application/json

Request Body: {PrinterTask object with _id.id}

Success Response:
{
    "success": true,
    "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    "spoolerId": 142,
    "printerName": "passkitchen",
    "documentName": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    "hasErrors": false,  // IMPORTANT: Indicates if OTHER jobs have errors
    "hasPrinterIssues": false,  // IMPORTANT: Indicates if ANY printer has issues (offline, error, paper out, etc.)
    "message": "Print job submitted successfully"
}

Duplicate Response:
{
    "success": false,
    "status": "completed",  // or "printing"
    "message": "Already printed successfully",
    "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"
}
```

### 2. Status Check by GUID
```http
GET /status/guid/{guid}

Response (Found):
{
    "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    "spoolerId": 142,
    "found": true,
    "printerName": "passkitchen",
    "status": "printing",
    "isError": false,
    "isPrinting": true,
    "isPaused": false
}

Response (Not Found - Completed):
{
    "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    "found": false,
    "status": "completed_or_not_found",
    "message": "Job not in spooler (likely completed successfully)"
}
```

### 3. Status Check by Spooler ID
```http
GET /status/spooler/{spoolerId}

Response:
{
    "spoolerId": 142,
    "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    "found": true,
    "printerName": "passkitchen",
    "status": "printing",
    "isError": false,
    "isPrinting": true,
    "isPaused": false
}
```

### 4. Get All Jobs Status
```http
GET /status

Response:
{
    "totalJobs": 3,
    "jobs": [
        {
            "guid": "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
            "spoolerId": 142,
            "printerName": "passkitchen",
            "status": "printing",
            "isError": false,
            "isPrinting": true,
            "isComplete": false
        },
        {
            "guid": "89a1b2c3-4d5e-6f7a-8b9c-0d1e2f3a4b5c",
            "spoolerId": 143,
            "printerName": "passkitchen",
            "status": "error",
            "isError": true,
            "isPrinting": false,
            "isComplete": false
        }
    ]
}
```

### 5. Retry Queue
```http
POST /retry-queue
Content-Type: application/json

Request:
{
    "printerName": "passkitchen"
}

Response:
{
    "success": true,
    "retriedCount": 2,
    "retriedJobs": [
        { "guid": "...", "spoolerId": 142 },
        { "guid": "...", "spoolerId": 143 }
    ]
}
```

### 6. Check Queue Status (All Jobs)
```http
GET /check-queue

Response:
{
    "totalJobs": 5,
    "hasErrors": true,
    "hasStuckJobs": true,
    "jobs": [
        {
            "guid": "abc-123",
            "spoolerId": 140,
            "printerName": "passkitchen",
            "status": "error",
            "errorMessage": "Paper out",
            "documentName": "abc-123",
            "position": 1,
            "pagesPrinted": 0,
            "totalPages": 2,
            "submittedAt": "2025-01-17T14:30:00Z",
            "ageSeconds": 180,
            "ageFormatted": "3 minutes",
            "isStuck": true,
            "isError": true
        },
        {
            "guid": "def-456",
            "spoolerId": 141,
            "printerName": "passkitchen",
            "status": "printing",
            "errorMessage": null,
            "documentName": "def-456",
            "position": 2,
            "pagesPrinted": 1,
            "totalPages": 1,
            "submittedAt": "2025-01-17T14:31:00Z",
            "ageSeconds": 120,
            "ageFormatted": "2 minutes",
            "isStuck": false,
            "isError": false
        },
        {
            "guid": "ghi-789",
            "spoolerId": 142,
            "printerName": "passkitchen",
            "status": "spooling",
            "errorMessage": null,
            "documentName": "ghi-789",
            "position": 3,
            "pagesPrinted": 0,
            "totalPages": 1,
            "submittedAt": "2025-01-17T14:32:00Z",
            "ageSeconds": 60,
            "ageFormatted": "1 minute",
            "isStuck": false,
            "isError": false
        },
        {
            "guid": "jkl-012",
            "spoolerId": 143,
            "printerName": "passkitchen",
            "status": "paused",
            "errorMessage": "User paused",
            "documentName": "jkl-012",
            "position": 4,
            "pagesPrinted": 0,
            "totalPages": 1,
            "submittedAt": "2025-01-17T14:28:00Z",
            "ageSeconds": 300,
            "ageFormatted": "5 minutes",
            "isStuck": true,
            "isError": false
        },
        {
            "guid": "mno-345",
            "spoolerId": 144,
            "printerName": "Microsoft Print to PDF",
            "status": "queued",
            "errorMessage": null,
            "documentName": "mno-345",
            "position": 1,
            "pagesPrinted": 0,
            "totalPages": 3,
            "submittedAt": "2025-01-17T14:33:00Z",
            "ageSeconds": 0,
            "ageFormatted": "just now",
            "isStuck": false,
            "isError": false
        }
    ],
    "summary": {
        "total": 5,
        "printing": 1,
        "queued": 1,
        "spooling": 1,
        "paused": 1,
        "error": 1,
        "stuckCount": 2,
        "oldestJobAge": 300,
        "oldestJobAgeFormatted": "5 minutes"
    }
}
```

### 7. Get Available Printers with Status
```http
GET /printers

Response:
{
    "totalPrinters": 3,
    "hasPrinterIssues": true,  // IMPORTANT: Flag to alert POS about any printer problems
    "printers": [
        {
            "name": "passkitchen",
            "displayName": "Pass Kitchen Printer",
            "isDefault": false,
            "isOnline": true,
            "status": "Ready",
            "statusFlags": 0,
            "port": "192.168.86.80",
            "portType": "NetworkIP",
            "driver": "Generic / Text Only",
            "location": "Kitchen",
            "comment": "Thermal printer for kitchen orders",
            "jobCount": 0,
            "supportsRaw": true,
            "supportedPaperSizes": ["80mm", "58mm"],
            "isShared": false,
            "shareName": null
        },
        {
            "name": "Bar_Printer",
            "displayName": "Bar Printer",
            "isDefault": false,
            "isOnline": false,  // OFFLINE!
            "status": "Offline",
            "statusFlags": 4096,  // Windows PRINTER_STATUS_OFFLINE
            "port": "192.168.86.81",
            "portType": "NetworkIP", 
            "driver": "Generic / Text Only",
            "location": "Bar",
            "comment": "Bar receipt printer",
            "jobCount": 2,  // Has stuck jobs!
            "supportsRaw": true,
            "supportedPaperSizes": ["80mm"],
            "isShared": false,
            "shareName": null,
            "error": "Cannot reach network printer"
        },
        {
            "name": "Microsoft Print to PDF",
            "displayName": "Microsoft Print to PDF",
            "isDefault": true,
            "isOnline": true,
            "status": "Ready",
            "statusFlags": 0,
            "port": "PORTPROMPT:",
            "portType": "Virtual",
            "driver": "Microsoft Print To PDF",
            "location": "",
            "comment": "",
            "jobCount": 0,
            "supportsRaw": false,  // Doesn't support RAW printing
            "supportedPaperSizes": ["A4", "Letter", "Legal"],
            "isShared": false,
            "shareName": null
        }
    ],
    "summary": {
        "total": 3,
        "online": 2,
        "offline": 1,
        "withJobs": 1,
        "rawCapable": 2
    }
}
```

### 8. Health Check (With Printer Status)
```http
GET /health

Response:
{
    "ok": true,
    "version": "0.1.0",
    "hasPrinterIssues": true,  // Quick flag for any printer problems (offline, errors, paper out, etc.)
    "printers": [
        {
            "name": "passkitchen",
            "isOnline": true,
            "status": "Ready",
            "jobCount": 0
        },
        {
            "name": "Bar_Printer", 
            "isOnline": false,
            "status": "Offline",
            "jobCount": 2
        },
        {
            "name": "Microsoft Print to PDF",
            "isOnline": true,
            "status": "Ready",
            "jobCount": 0
        }
    ],
    "summary": {
        "totalPrinters": 3,
        "onlinePrinters": 2,
        "offlinePrinters": 1,
        "printersWithJobs": 1
    },
    "uptimeSeconds": 3600
}
```

---

## 🔧 Implementation Details

### Printer Discovery: How It Works

The Printer App discovers printers using Windows APIs and provides real-time status:

```csharp
// Windows API for printer enumeration
[DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
public static extern bool EnumPrinters(PrinterEnumFlags Flags, string Name, 
    uint Level, IntPtr pPrinterEnum, uint cbBuf, 
    ref uint pcbNeeded, ref uint pcReturned);

// Get all printers with status
public List<PrinterInfo> GetAllPrintersWithStatus()
{
    var printers = new List<PrinterInfo>();
    
    // Enumerate all local and network printers
    foreach (string printerName in PrinterSettings.InstalledPrinters)
    {
        var printer = new PrinterInfo();
        printer.Name = printerName;
        
        // Get printer status using PRINTER_INFO_2
        IntPtr hPrinter;
        if (OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            try
            {
                // Get PRINTER_INFO_2 for detailed status
                int cbNeeded = 0;
                GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out cbNeeded);
                
                if (cbNeeded > 0)
                {
                    IntPtr pInfo = Marshal.AllocHGlobal(cbNeeded);
                    try
                    {
                        if (GetPrinter(hPrinter, 2, pInfo, cbNeeded, out cbNeeded))
                        {
                            var info = (PRINTER_INFO_2)Marshal.PtrToStructure(pInfo, typeof(PRINTER_INFO_2));
                            
                            // Extract printer details
                            printer.Status = GetStatusString(info.Status);
                            printer.IsOnline = (info.Status & PRINTER_STATUS_OFFLINE) == 0;
                            printer.StatusFlags = info.Status;
                            printer.JobCount = info.cJobs;
                            printer.Location = Marshal.PtrToStringAuto(info.pLocation);
                            printer.Comment = Marshal.PtrToStringAuto(info.pComment);
                            printer.Driver = Marshal.PtrToStringAuto(info.pDriverName);
                            printer.Port = Marshal.PtrToStringAuto(info.pPortName);
                            
                            // Determine port type
                            if (printer.Port.StartsWith("\\\\"))
                                printer.PortType = "Network";
                            else if (printer.Port.Contains("."))
                                printer.PortType = "NetworkIP";
                            else if (printer.Port.StartsWith("USB"))
                                printer.PortType = "USB";
                            else if (printer.Port.Contains("LPT"))
                                printer.PortType = "Parallel";
                            else if (printer.Port.Contains("COM"))
                                printer.PortType = "Serial";
                            else
                                printer.PortType = "Virtual";
                            
                            // Check if supports RAW printing (thermal printers)
                            printer.SupportsRaw = CheckRawSupport(printerName);
                            
                            // Get default printer
                            printer.IsDefault = (info.Attributes & PRINTER_ATTRIBUTE_DEFAULT) != 0;
                            
                            // Check for errors
                            if (info.Status & PRINTER_STATUS_ERROR)
                                printer.Error = GetErrorMessage(info.Status);
                        }
                    }
                    finally { Marshal.FreeHGlobal(pInfo); }
                }
            }
            finally { ClosePrinter(hPrinter); }
        }
        
        printers.Add(printer);
    }
    
    return printers;
}

// Status flag constants
const uint PRINTER_STATUS_OFFLINE = 0x00000080;
const uint PRINTER_STATUS_PAPER_OUT = 0x00000010;
const uint PRINTER_STATUS_ERROR = 0x00000002;
const uint PRINTER_STATUS_DOOR_OPEN = 0x00400000;
const uint PRINTER_STATUS_NO_TONER = 0x00040000;
const uint PRINTER_STATUS_USER_INTERVENTION = 0x00100000;

// Check if printer has any issues
public bool HasPrinterIssues(uint status)
{
    return (status & (PRINTER_STATUS_OFFLINE | 
                     PRINTER_STATUS_PAPER_OUT | 
                     PRINTER_STATUS_ERROR | 
                     PRINTER_STATUS_DOOR_OPEN | 
                     PRINTER_STATUS_NO_TONER | 
                     PRINTER_STATUS_USER_INTERVENTION)) != 0;
}

// Convert status flags to readable string
private string GetStatusString(uint status)
{
    if (status == 0) return "Ready";
    if (status & PRINTER_STATUS_OFFLINE) return "Offline";
    if (status & PRINTER_STATUS_PAPER_OUT) return "Paper Out";
    if (status & PRINTER_STATUS_ERROR) return "Error";
    if (status & PRINTER_STATUS_DOOR_OPEN) return "Door Open";
    if (status & PRINTER_STATUS_NO_TONER) return "No Toner";
    if (status & PRINTER_STATUS_USER_INTERVENTION) return "Needs Attention";
    return "Unknown";
}
```

### Why Printer Discovery Matters

1. **Pre-flight Checks**: POS can check if target printer is online before sending
2. **Failover**: If primary printer offline, POS can route to backup
3. **User Alerts**: Show which printers are available in UI
4. **Troubleshooting**: Quickly identify offline/error printers

### When POS Should Check Printers

```javascript
// 1. On startup
async function initPrinting() {
    const printers = await fetch('/printers');
    if (printers.hasPrinterIssues) {
        alertStaff('Some printers have issues');
    }
}

// 2. Before printing (optional)
async function printOrder(order, printerName) {
    // Quick check if target printer is online
    const health = await fetch('/health');
    const printer = health.printers.find(p => p.name === printerName);
    
    if (!printer.isOnline) {
        // Try backup printer or alert user
        const backup = findBackupPrinter(printerName);
        if (backup) {
            printerName = backup;
        } else {
            throw new Error(`Printer ${printerName} is offline`);
        }
    }
    
    // Send print job
    await sendPrintJob(order, printerName);
}

// 3. After print response with hasPrinterIssues flag
if (printResponse.hasPrinterIssues) {
    // Some printer has issues - check which ones
    const printers = await fetch('/printers');
    updatePrinterStatusUI(printers);
}

// 4. Periodic health checks (every minute)
setInterval(async () => {
    const health = await fetch('/health');
    if (health.hasPrinterIssues) {
        updatePrinterIssueWarning(health.printers);
    }
}, 60000);
```

### Printer App: Queue Status Implementation
```csharp
// Check for ANY errored or stuck jobs in the system
public bool CheckForProblematicJobs()
{
    foreach (string printerName in PrinterSettings.InstalledPrinters)
    {
        var jobs = GetPrinterJobs(printerName);
        foreach (var job in jobs)
        {
            // Check for errors
            if (job.Status.HasFlag(JobStatus.Error) || 
                job.Status.HasFlag(JobStatus.Paused) ||
                job.Status.HasFlag(JobStatus.PaperOut) ||
                job.Status.HasFlag(JobStatus.Offline))
            {
                return true;
            }
            
            // Check if stuck (not printing for > 30 seconds)
            var age = DateTime.Now - job.SubmittedTime;
            if (age.TotalSeconds > 30 && !job.Status.HasFlag(JobStatus.Printing))
            {
                return true;
            }
        }
    }
    return false;
}

// Get ALL jobs in queue with complete status
[HttpGet("/check-queue")]
public IActionResult CheckQueue()
{
    var allJobs = new List<object>();
    var summary = new Dictionary<string, int>();
    bool hasErrors = false;
    bool hasStuckJobs = false;
    int oldestJobAge = 0;
    
    foreach (string printerName in PrinterSettings.InstalledPrinters)
    {
        var jobs = GetPrinterJobsDetailed(printerName);
        
        foreach (var job in jobs)
        {
            var ageTimeSpan = DateTime.Now - job.SubmittedTime;
            var ageSeconds = (int)ageTimeSpan.TotalSeconds;
            
            // Determine if stuck (queued/spooling for > 30 seconds)
            bool isStuck = false;
            if (ageTimeSpan.TotalSeconds > 30)
            {
                if (!job.Status.HasFlag(JobStatus.Printing) && 
                    !job.Status.HasFlag(JobStatus.Printed))
                {
                    isStuck = true;
                    hasStuckJobs = true;
                }
            }
            
            // Check for errors
            bool isError = job.Status.HasFlag(JobStatus.Error) || 
                          job.Status.HasFlag(JobStatus.PaperOut) ||
                          job.Status.HasFlag(JobStatus.Offline) ||
                          job.Status.HasFlag(JobStatus.UserIntervention);
            
            if (isError) hasErrors = true;
            
            // Track oldest job
            if (ageSeconds > oldestJobAge)
                oldestJobAge = ageSeconds;
            
            // Determine status string
            string status = DetermineStatus(job.Status);
            
            // Update summary counts
            if (!summary.ContainsKey(status))
                summary[status] = 0;
            summary[status]++;
            
            allJobs.Add(new
            {
                guid = job.DocumentName,  // Document name IS the GUID
                spoolerId = job.JobId,
                printerName = printerName,
                status = status,
                errorMessage = isError ? GetErrorMessage(job.Status) : null,
                documentName = job.DocumentName,
                position = job.Position,
                pagesPrinted = job.PagesPrinted,
                totalPages = job.TotalPages,
                submittedAt = job.SubmittedTime,
                ageSeconds = ageSeconds,
                ageFormatted = FormatAge(ageTimeSpan),
                isStuck = isStuck,
                isError = isError
            });
        }
    }
    
    return Json(new
    {
        totalJobs = allJobs.Count,
        hasErrors = hasErrors,
        hasStuckJobs = hasStuckJobs,
        jobs = allJobs.OrderBy(j => j.position).ThenBy(j => j.printerName),
        summary = new
        {
            total = allJobs.Count,
            printing = summary.GetValueOrDefault("printing", 0),
            queued = summary.GetValueOrDefault("queued", 0),
            spooling = summary.GetValueOrDefault("spooling", 0),
            paused = summary.GetValueOrDefault("paused", 0),
            error = summary.GetValueOrDefault("error", 0),
            stuckCount = allJobs.Count(j => j.isStuck),
            oldestJobAge = oldestJobAge,
            oldestJobAgeFormatted = FormatAge(TimeSpan.FromSeconds(oldestJobAge))
        }
    });
}

// Format age for display
private string FormatAge(TimeSpan age)
{
    if (age.TotalSeconds < 60)
        return "just now";
    if (age.TotalMinutes < 60)
        return $"{(int)age.TotalMinutes} minute{((int)age.TotalMinutes != 1 ? "s" : "")}";
    if (age.TotalHours < 24)
        return $"{(int)age.TotalHours} hour{((int)age.TotalHours != 1 ? "s" : "")}";
    return $"{(int)age.TotalDays} day{((int)age.TotalDays != 1 ? "s" : "")}";
}

// Get detailed job info using JOB_INFO_2
private List<JobInfo> GetPrinterJobsDetailed(string printerName)
{
    var jobs = new List<JobInfo>();
    IntPtr hPrinter;
    
    if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        return jobs;
    
    try
    {
        int cbNeeded = 0;
        int cReturned = 0;
        
        // Use level 2 for more details
        EnumJobs(hPrinter, 0, 999, 2, IntPtr.Zero, 0, out cbNeeded, out cReturned);
        
        if (cbNeeded == 0) return jobs;
        
        IntPtr pJob = Marshal.AllocHGlobal(cbNeeded);
        try
        {
            if (EnumJobs(hPrinter, 0, 999, 2, pJob, cbNeeded, out cbNeeded, out cReturned))
            {
                IntPtr current = pJob;
                for (int i = 0; i < cReturned; i++)
                {
                    var jobInfo = (JOB_INFO_2)Marshal.PtrToStructure(current, typeof(JOB_INFO_2));
                    
                    jobs.Add(new JobInfo
                    {
                        JobId = jobInfo.JobId,
                        DocumentName = Marshal.PtrToStringAuto(jobInfo.pDocument),
                        Status = (JobStatus)jobInfo.Status,
                        Position = jobInfo.Position,
                        PagesPrinted = jobInfo.PagesPrinted,
                        TotalPages = jobInfo.TotalPages,
                        SubmittedTime = SystemTimeToDateTime(jobInfo.Submitted),
                        Size = jobInfo.Size
                    });
                    
                    current = IntPtr.Add(current, Marshal.SizeOf(typeof(JOB_INFO_2)));
                }
            }
        }
        finally { Marshal.FreeHGlobal(pJob); }
    }
    finally { ClosePrinter(hPrinter); }
    
    return jobs;
}

private string GetErrorMessage(JobStatus status)
{
    if (status.HasFlag(JobStatus.PaperOut)) return "Paper out";
    if (status.HasFlag(JobStatus.Offline)) return "Printer offline";
    if (status.HasFlag(JobStatus.Paused)) return "Job paused";
    if (status.HasFlag(JobStatus.UserIntervention)) return "User intervention required";
    if (status.HasFlag(JobStatus.Error)) return "Printer error";
    return "Unknown error";
}
```

### Printer App: GUID Tracking Service
```csharp
public class GuidSpoolerTracker
{
    // Check if GUID exists in any printer's spooler
    public static SpoolerJobInfo CheckGuidInSpooler(string guid)
    {
        foreach (string printerName in PrinterSettings.InstalledPrinters)
        {
            IntPtr hPrinter;
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                continue;
            
            try
            {
                // Enumerate jobs
                int cbNeeded, cReturned;
                EnumJobs(hPrinter, 0, 999, 1, IntPtr.Zero, 0, out cbNeeded, out cReturned);
                
                if (cbNeeded == 0) continue;
                
                IntPtr pJob = Marshal.AllocHGlobal(cbNeeded);
                try
                {
                    if (EnumJobs(hPrinter, 0, 999, 1, pJob, cbNeeded, out cbNeeded, out cReturned))
                    {
                        IntPtr current = pJob;
                        for (int i = 0; i < cReturned; i++)
                        {
                            var jobInfo = (JOB_INFO_1)Marshal.PtrToStructure(current, typeof(JOB_INFO_1));
                            string documentName = Marshal.PtrToStringAuto(jobInfo.pDocument);
                            
                            // Check if document name is our GUID
                            if (documentName == guid)
                            {
                                return new SpoolerJobInfo
                                {
                                    JobId = jobInfo.JobId,
                                    PrinterName = printerName,
                                    DocumentName = documentName,
                                    Guid = guid,
                                    Status = (JobStatus)jobInfo.Status
                                };
                            }
                            
                            current = IntPtr.Add(current, Marshal.SizeOf(typeof(JOB_INFO_1)));
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(pJob); }
            }
            finally { ClosePrinter(hPrinter); }
        }
        
        return null;  // Not found = completed or never existed
    }
}
```

### POS: Complete Print Service
```javascript
class POSPrintService {
    constructor() {
        this.apiUrl = 'http://127.0.0.1:9877';
        this.pollingInterval = 20000;  // 20 seconds
    }
    
    // Main print function
    async print(printerTask) {
        const guid = printerTask._id.id;
        
        try {
            const response = await fetch(`${this.apiUrl}/print`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(printerTask)
            });
            
            const result = await response.json();
            
            if (result.success) {
                // Store both IDs
                await this.saveToDatabase({
                    id: result.guid,
                    spoolerId: result.spoolerId,
                    printerName: result.printerName,
                    status: 'printing'
                });
                
                console.log(`Print submitted: GUID=${result.guid}, SpoolerID=${result.spoolerId}`);
                
                // CRITICAL: Check if there are OTHER errored jobs
                if (result.hasErrors) {
                    console.log('System has errored jobs - checking...');
                    await this.checkAndUpdateErroredJobs();
                }
                
                return result;
                
            } else {
                // Handle duplicate or error
                if (result.status === 'completed') {
                    await this.updateStatus(guid, 'completed');
                    console.log('Already printed');
                } else if (result.status === 'printing') {
                    console.log('Currently printing');
                }
                
                return result;
            }
            
        } catch (error) {
            console.error('Print failed:', error);
            throw error;
        }
    }
    
    // Check queue status and update database
    async checkAndUpdateQueueStatus() {
        try {
            const response = await fetch(`${this.apiUrl}/check-queue`);
            const data = await response.json();
            
            console.log(`Queue has ${data.totalJobs} jobs, ${data.summary.stuckCount} stuck`);
            
            for (const job of data.jobs) {
                // Update job status in POS database
                if (job.isError) {
                    await this.updateStatus(job.guid, 'error', {
                        spoolerId: job.spoolerId,
                        errorMessage: job.errorMessage,
                        printerName: job.printerName
                    });
                    
                    // Notify UI about error
                    this.notifyError(job.guid, job.errorMessage);
                    
                } else if (job.isStuck) {
                    await this.updateStatus(job.guid, 'stuck', {
                        spoolerId: job.spoolerId,
                        ageSeconds: job.ageSeconds,
                        position: job.position
                    });
                    
                    // Notify about stuck job
                    this.notifyStuck(job.guid, job.ageFormatted);
                    
                } else if (job.status === 'printing') {
                    await this.updateStatus(job.guid, 'printing', {
                        pagesPrinted: job.pagesPrinted,
                        totalPages: job.totalPages
                    });
                }
            }
            
            // Show summary if problematic
            if (data.hasErrors || data.hasStuckJobs) {
                this.showQueueWarning(`Queue has issues: ${data.summary.error} errors, ${data.summary.stuckCount} stuck`);
            }
            
        } catch (error) {
            console.error('Failed to check queue:', error);
        }
    }
    
    // Check status by GUID
    async checkStatusByGuid(guid) {
        const response = await fetch(`${this.apiUrl}/status/guid/${guid}`);
        const status = await response.json();
        
        if (!status.found) {
            // Not in spooler = completed
            await this.updateStatus(guid, 'completed');
        }
        
        return status;
    }
    
    // Check status by Spooler ID
    async checkStatusBySpoolerId(spoolerId) {
        const response = await fetch(`${this.apiUrl}/status/spooler/${spoolerId}`);
        const status = await response.json();
        
        if (!status.found) {
            // Not in spooler = completed
            await this.updateStatusBySpoolerId(spoolerId, 'completed');
        }
        
        return status;
    }
    
    // Batch status check
    async checkAllActiveJobs() {
        // Get active jobs from database
        const activeJobs = await db.query(
            `SELECT id, spooler_id FROM printer_tasks 
             WHERE status IN ('printing', 'spooling')
             AND created_at > NOW() - INTERVAL '1 hour'`
        );
        
        // Get current spooler status
        const response = await fetch(`${this.apiUrl}/status`);
        const spoolerData = await response.json();
        
        // Create lookup maps
        const spoolerJobsByGuid = new Map();
        const spoolerJobsById = new Map();
        
        spoolerData.jobs.forEach(job => {
            spoolerJobsByGuid.set(job.guid, job);
            spoolerJobsById.set(job.spoolerId, job);
        });
        
        // Update statuses
        for (const job of activeJobs) {
            const spoolerJob = spoolerJobsByGuid.get(job.id) || 
                              spoolerJobsById.get(job.spooler_id);
            
            if (!spoolerJob) {
                // Not in spooler = completed
                await this.updateStatus(job.id, 'completed');
            } else if (spoolerJob.isError) {
                await this.updateStatus(job.id, 'error');
            }
        }
    }
    
    // Start polling
    startPolling() {
        setInterval(() => {
            this.checkAllActiveJobs();
        }, this.pollingInterval);
    }
}
```

---

## 📋 High-Level POS Implementation Guide

### How the POS Should Use This System

#### 1. **On POS Startup**
```javascript
// Initialize printing system
async function initializePrinting() {
    // 1. Check if printing is enabled in settings
    if (!settings.printingEnabled) return;
    
    // 2. Check if printer app is running
    const health = await checkPrinterHealth();
    if (!health.ok) {
        // Try to launch printer app
        await launchPrinterApp();
    }
    
    // 3. Check for any stuck/errored jobs from previous session
    const queue = await checkQueue();
    if (queue.hasErrors || queue.hasStuckJobs) {
        // Alert staff about existing problems
        showQueueProblems(queue);
    }
    
    // 4. Start background monitoring (every 20 seconds)
    startQueueMonitoring();
}
```

#### 2. **When User Clicks Print**
```javascript
async function handlePrintButton(order) {
    // 1. Create PrinterTask with unique GUID
    const printerTask = {
        _id: { id: generateGuid() },  // Unique ID
        template: { body: generateReceipt(order) },
        templateData: JSON.stringify(order.data)
    };
    
    // 2. Send to printer app
    const result = await printService.print(printerTask);
    
    // 3. Handle response
    if (result.success) {
        // Save to database
        await saveTask(result.guid, result.spoolerId);
        
        // Check if queue has problems
        if (result.hasErrors || result.hasStuckJobs) {
            // Don't block user, but check queue in background
            setTimeout(() => checkAndHandleQueueProblems(), 100);
        }
        
        showSuccess("Print sent successfully");
    } else {
        if (result.status === "completed") {
            showInfo("Already printed");
        } else if (result.status === "printing") {
            showInfo("Already printing");
        } else {
            showError("Print failed: " + result.message);
        }
    }
}
```

#### 3. **Background Queue Monitoring**
```javascript
// Run every 20 seconds
async function monitorQueue() {
    const queue = await checkQueue();
    
    // Process each job
    for (const job of queue.jobs) {
        if (job.isError) {
            // Mark as error in database
            await updateTaskStatus(job.guid, 'error', job.errorMessage);
            
            // Alert staff once
            if (!alreadyAlerted(job.guid)) {
                alertStaff(`Print error: ${job.errorMessage} (${job.ageFormatted} ago)`);
            }
            
        } else if (job.isStuck) {
            // Job stuck for > 30 seconds
            if (!alreadyAlerted(job.guid)) {
                alertStaff(`Print stuck: ${job.guid} waiting ${job.ageFormatted}`);
            }
            
        } else if (job.status === "printing") {
            // Update progress
            await updateTaskProgress(job.guid, job.pagesPrinted, job.totalPages);
        }
    }
    
    // Check for completed jobs (not in queue anymore)
    const activeTasks = await getActiveTasks();
    for (const task of activeTasks) {
        const inQueue = queue.jobs.find(j => j.guid === task.id);
        if (!inQueue) {
            // Not in queue = completed successfully
            await updateTaskStatus(task.id, 'completed');
        }
    }
}
```

#### 4. **Handling Common Scenarios**

##### Scenario A: Paper Out
```javascript
// Queue check returns job with "Paper out" error
if (job.errorMessage === "Paper out") {
    // 1. Alert staff
    showAlert("Printer out of paper!");
    
    // 2. After paper added, retry queue
    await retryQueue(job.printerName);
}
```

##### Scenario B: Multiple Jobs Stuck
```javascript
// Queue shows multiple stuck jobs
if (queue.summary.stuckCount > 3) {
    // Major problem - alert manager
    alertManager(`Print queue backed up: ${queue.summary.stuckCount} jobs stuck`);
    
    // Show queue status to staff
    showQueueStatus(queue);
}
```

##### Scenario C: Old Job Still in Queue
```javascript
// Job older than 5 minutes still in queue
if (job.ageSeconds > 300) {
    // Likely needs intervention
    alertStaff(`Old print job needs attention: ${job.ageFormatted} old`);
    
    // Option to cancel
    if (confirm(`Cancel job ${job.guid}?`)) {
        await cancelJob(job.spoolerId);
    }
}
```

### POS Dashboard Display

```
┌─────────────────────────────────────────┐
│ Print Queue Status                      │
├─────────────────────────────────────────┤
│ Total Jobs: 5                           │
│ ⚠️ 1 Error | 2 Stuck | 2 Printing       │
│                                         │
│ Recent Prints:                          │
│ ✅ Order #1234 - Completed (2 min ago)  │
│ 🖨️ Order #1235 - Printing (30s ago)     │
│ ⚠️ Order #1236 - Stuck (1 min ago)      │
│ ❌ Order #1237 - Paper Out (3 min ago)  │
│                                         │
│ [Retry Failed] [Check Queue] [Settings] │
└─────────────────────────────────────────┘
```

### Decision Flow Chart

```
User Clicks Print
    ↓
Send to Printer App
    ↓
Response Success?
    ├─ Yes → Check hasErrors/hasStuckJobs?
    │         ├─ Yes → Background check queue
    │         │        Update all job statuses
    │         └─ No → Done
    │
    └─ No → Already printed or printing?
            ├─ Yes → Show info
            └─ No → Show error

Background (Every 20 seconds)
    ↓
Call /check-queue
    ↓
For each job:
    ├─ Error? → Alert once, update DB
    ├─ Stuck? → Alert if > 30 seconds
    └─ Missing? → Mark as completed
```

---

## 🔴 Critical Queue Monitoring Flow

### How Queue Monitoring Works
Every successful print response includes `hasErrors` and `hasStuckJobs` flags that indicate if there are ANY problematic jobs in the system. This triggers the POS to check the entire queue status.

```
1. POS sends print job A → Success with hasErrors: false, hasStuckJobs: false
   ✓ Job A printing normally, queue is healthy

2. POS sends print job B → Success with hasErrors: true, hasStuckJobs: true
   ✓ Job B submitted successfully
   ⚠️ BUT there are problems in the queue!
   → POS calls /check-queue to get ALL jobs
   → Sees complete queue status:
      - Job C: Error (paper out) - 3 minutes old
      - Job D: Stuck (queued) - 5 minutes old  
      - Job E: Printing normally
   → Updates ALL job statuses in database

3. This provides complete visibility:
   - All jobs currently in queue
   - How long each has been waiting
   - Which are stuck (not progressing)
   - Which have errors
   - Queue position for each job
```

### Implementation Flow
```javascript
// Every print response triggers queue checking if needed
if (printResponse.success) {
    if (printResponse.hasErrors || printResponse.hasStuckJobs) {
        // Check entire queue status
        const queue = await fetch('/check-queue');
        // Update ALL jobs in POS database
        // Alert about stuck/errored jobs
    }
}
```

### What Makes a Job "Stuck"
A job is considered stuck if:
- In queue for > 30 seconds AND
- Status is NOT "printing" or "printed"
- Examples: Queued for 45 seconds, Spooling for 1 minute

This design provides:
- **Complete queue visibility** - See ALL jobs, not just errors
- **Stuck job detection** - Identify jobs not progressing
- **Age tracking** - Know how long jobs have been waiting
- **Queue position** - See order of processing
- **Proactive alerts** - Notify about problems before users complain

---

## 🎯 Key Features Implemented

### 1. **Duplicate Prevention**
- Uses `PrinterTask._id.id` as unique identifier
- Checks if GUID exists in Windows spooler before printing
- Returns appropriate status for duplicates

### 2. **Dual Tracking System**
- **GUID**: Permanent identifier from POS, used as document name
- **Spooler ID**: Windows-assigned job ID for direct queries
- Both stored in POS database for redundancy

### 3. **Real-time Status Monitoring**
- Query by GUID or Spooler ID
- Bulk status checks for efficiency
- Automatic completion detection (job disappears from spooler)

### 4. **Error Recovery**
- Detect error states in spooler
- Clear error jobs and retry
- Retry queue for paper-out scenarios

### 5. **Performance Optimizations**
- Spooler ID queries are faster than GUID searches
- Bulk status checks reduce API calls
- 20-second polling interval balances load and responsiveness

---

## 📊 Status Flow Diagram

```
┌──────────────┐
│   Created    │ PrinterTask created with _id.id
└──────┬───────┘
       ↓
┌──────────────┐
│   Sending    │ POST /print with PrinterTask
└──────┬───────┘
       ↓
┌──────────────┐
│ Duplicate?   │ Check if GUID exists in spooler
└──┬────┬──────┘
   No   Yes → Return existing status
   ↓
┌──────────────┐
│  Submitted   │ Sent to Windows spooler
└──────┬───────┘ Returns: GUID + Spooler ID
       ↓
┌──────────────┐
│   Spooling   │ In Windows queue
└──────┬───────┘
       ↓
┌──────────────┐
│   Printing   │ Being printed
└──┬────┬──────┘
   OK   Error → Mark as error
   ↓
┌──────────────┐
│  Completed   │ Disappears from spooler
└──────────────┘
```

---

## 🧪 Testing Scenarios

### Test 1: Normal Print Flow
```powershell
# Send print job
$json = @'
{
    "_id": {"id": "test-001", "siteId": "site-001"},
    "template": {"body": "<root><text>Test Print</text></root>"},
    "templateData": "{\"printerDeviceName\":\"Microsoft Print to PDF\"}",
    "isOpenCashDrawer": false
}
'@

$response = Invoke-RestMethod -Uri "http://127.0.0.1:9877/print" -Method Post -Body $json -ContentType "application/json"
Write-Host "GUID: $($response.guid), Spooler ID: $($response.spoolerId)"
```

### Test 2: Duplicate Prevention
```powershell
# Send same job twice
$response1 = Invoke-RestMethod -Uri "http://127.0.0.1:9877/print" -Method Post -Body $json -ContentType "application/json"
$response2 = Invoke-RestMethod -Uri "http://127.0.0.1:9877/print" -Method Post -Body $json -ContentType "application/json"

# Second response should indicate duplicate
Write-Host "First: Success=$($response1.success)"
Write-Host "Second: Success=$($response2.success), Status=$($response2.status)"
```

### Test 3: Status Tracking
```powershell
# Check by GUID
$guid = "test-001"
$status = Invoke-RestMethod -Uri "http://127.0.0.1:9877/status/guid/$guid" -Method Get
Write-Host "Status by GUID: $($status.status)"

# Check by Spooler ID
$spoolerId = $response.spoolerId
$status = Invoke-RestMethod -Uri "http://127.0.0.1:9877/status/spooler/$spoolerId" -Method Get
Write-Host "Status by Spooler ID: $($status.status)"
```

---

## 🚨 Error Handling

### Printer Offline
```javascript
// Detected in spooler status
if (status.isError && status.errorMessage.includes('offline')) {
    await notifyUser('Printer is offline');
    await scheduleRetry(taskId, 60000);  // Retry in 1 minute
}
```

### Paper Out
```javascript
// Paper out error
if (status.isError && status.errorMessage.includes('paper')) {
    await notifyUser('Printer out of paper');
    // After paper added:
    await fetch('/retry-queue', {
        method: 'POST',
        body: JSON.stringify({ printerName: 'passkitchen' })
    });
}
```

### Job Stuck
```javascript
// Job printing for too long
if (status.isPrinting && (Date.now() - job.startedAt) > 120000) {
    await cancelJob(job.spoolerId);
    await retryPrint(job.id);
}
```

---

## 📈 Performance Metrics

| Operation | Expected Time | Details |
|-----------|--------------|---------|
| Print submission | < 100ms | Includes duplicate check |
| GUID lookup | < 50ms | Scan all printer queues |
| Spooler ID lookup | < 10ms | Direct job query |
| Bulk status (100 jobs) | < 200ms | Single enumeration |
| Completion detection | 0-20s | Based on polling interval |

---

## 🔐 Security Considerations

1. **Local Only**: Service binds to 127.0.0.1
2. **GUID Validation**: Always validate GUID format
3. **Input Sanitization**: Clean template data before processing
4. **Error Messages**: Don't expose internal paths in errors
5. **Rate Limiting**: Consider adding rate limits for production

---

## 📝 Configuration

### Printer App Settings
```json
{
    "server": {
        "port": 9877,
        "host": "127.0.0.1"
    },
    "spooler": {
        "scanInterval": 2000,
        "duplicateCheckTimeout": 1000,
        "maxTrackedJobs": 1000
    }
}
```

### POS Settings
```json
{
    "printing": {
        "apiUrl": "http://127.0.0.1:9877",
        "pollingInterval": 20000,
        "retryLimit": 3,
        "retryDelay": 5000,
        "completionTimeout": 300000
    }
}
```

---

## ✅ Implementation Checklist

### Printer App
- [x] Extract GUID from `PrinterTask._id.id`
- [x] Check for duplicate GUIDs in spooler
- [x] Submit with GUID as document name
- [x] Return both GUID and Spooler ID
- [x] Implement status endpoints (by GUID and Spooler ID)
- [x] Enumerate all spooler jobs
- [x] Handle error states

### POS System
- [x] Send PrinterTask with `_id.id`
- [x] Store both GUID and Spooler ID
- [x] Implement status polling
- [x] Handle completion detection
- [x] Update database based on status
- [x] Implement retry logic
- [x] Handle error scenarios

---

## 📚 Summary

This implementation provides a robust, efficient print tracking system that:

1. **Prevents Duplicates**: GUID checking ensures no double prints
2. **Tracks Reliably**: Dual ID system (GUID + Spooler ID) ensures tracking even if one fails
3. **Detects Completion**: Jobs disappearing from spooler indicates success
4. **Handles Errors**: Clear error detection and recovery mechanisms
5. **Performs Well**: Optimized queries using Spooler ID when available
6. **Scales**: Can handle hundreds of concurrent print jobs

The system uses the Windows spooler as the source of truth, eliminating the need for complex state management while providing reliable print job tracking.

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-17  
**Status**: Complete Implementation Specification