# 📖 POS Integration Complete Guide
**Everything you need to integrate the Windows Printer Tray App with dnero-desktop-pos**

---

## 🎯 Quick Summary

Replace 5 native printer functions with HTTP API calls to the Tray App. Everything else stays the same.

| Old Function | New API | Purpose |
|-------------|---------|---------|
| `getPrinters()` | `GET /printers` | Discover printers |
| `getPrinterStatus()` | `GET /printers` or `/health` | Check printer status |
| `printDirect()` | `POST /print` | Submit print job |
| `getPrinterJob()` | `GET /status/spooler/{id}` | Check job status |
| `sendJobCommand()` | `POST /retry-queue` | Retry failed jobs |

---

## 📋 Table of Contents

1. [What Stays the Same](#what-stays-the-same)
2. [Step 1: Create TrayAppService](#step-1-create-trayappservice)
3. [Step 2: Update Print Submission](#step-2-update-print-submission)
4. [Step 3: Update Printer Discovery](#step-3-update-printer-discovery)
5. [Step 4: Update Status Refresh](#step-4-update-status-refresh)
6. [Step 5: Update Job Monitoring](#step-5-update-job-monitoring)
7. [Testing](#testing)
8. [Benefits](#benefits)

---

## ✅ What Stays the Same

**No changes needed to:**
- PrinterTask structure (already compatible)
- Ditto database sync
- Retry logic (3 retries)
- Secondary printer fallback
- Error handling
- All UI components (just get more data)
- Manual printer addition process

---

## Step 1: Create TrayAppService

Create a single service to handle all Tray App communication:

```typescript
// src/services/TrayAppService.ts
export class TrayAppService {
  private baseUrl = 'http://127.0.0.1:9877';
  private available: boolean | null = null;
  
  /**
   * Check if Tray App is running
   */
  async checkAvailable(): Promise<boolean> {
    try {
      const response = await fetch(`${this.baseUrl}/health`);
      this.available = response.ok;
      return this.available;
    } catch {
      this.available = false;
      return false;
    }
  }
  
  /**
   * Get all printers with status
   */
  async getPrinters(): Promise<any> {
    const response = await fetch(`${this.baseUrl}/printers`);
    if (!response.ok) throw new Error('Failed to get printers');
    return response.json();
  }
  
  /**
   * Get health check with printer summary
   */
  async getHealth(): Promise<any> {
    const response = await fetch(`${this.baseUrl}/health`);
    if (!response.ok) throw new Error('Health check failed');
    return response.json();
  }
  
  /**
   * Submit print job
   */
  async print(printerTask: PrinterTask): Promise<any> {
    const response = await fetch(`${this.baseUrl}/print`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(printerTask)
    });
    if (!response.ok) throw new Error('Print failed');
    return response.json();
  }
  
  /**
   * Check job status by spooler ID
   */
  async getJobStatus(spoolerId: number): Promise<any> {
    const response = await fetch(`${this.baseUrl}/status/spooler/${spoolerId}`);
    if (!response.ok) throw new Error('Failed to get job status');
    return response.json();
  }
  
  /**
   * Get complete queue status
   */
  async checkQueue(): Promise<any> {
    const response = await fetch(`${this.baseUrl}/check-queue`);
    if (!response.ok) throw new Error('Failed to check queue');
    return response.json();
  }
  
  /**
   * Retry failed jobs on a printer
   */
  async retryQueue(printerName: string): Promise<any> {
    const response = await fetch(`${this.baseUrl}/retry-queue`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ printerName })
    });
    if (!response.ok) throw new Error('Retry failed');
    return response.json();
  }
}

export const trayApp = new TrayAppService();
```

---

## Step 2: Update Print Submission

In `PrinterContext.tsx`, replace `printDirect()`:

### OLD Code
```typescript
import { printDirect } from "../printers/printers";

// In printTasks function
let commands = await makeCommands(currency, printerTask);
const jobId = printDirect(
  printerTask.printerDeviceName,
  printerTask._id.id,
  "RAW",
  commands
);
await printerTaskOps.updateById(printerTask._id, {
  inProgress: true,
  jobId: `${jobId}`,
});
```

### NEW Code
```typescript
import { trayApp } from '../services/TrayAppService';

// In printTasks function
// Check if Tray App available
if (await trayApp.checkAvailable()) {
  // Use Tray App - NO NEED to generate commands
  const result = await trayApp.print(printerTask);
  
  if (result.success) {
    jobId = result.spoolerId;
    await printerTaskOps.updateById(printerTask._id, {
      inProgress: true,
      jobId: `${jobId}`,
    });
    
    // OPTIONAL: Check if queue has issues
    if (result.hasErrors || result.hasPrinterIssues) {
      console.warn('Queue has issues');
      // Could check details with trayApp.checkQueue()
    }
  } else {
    throw new Error(result.message || 'Print failed');
  }
} else {
  // FALLBACK: Use existing native method
  let commands = await makeCommands(currency, printerTask);
  jobId = printDirect(
    printerTask.printerDeviceName,
    printerTask._id.id,
    "RAW",
    commands
  );
  await printerTaskOps.updateById(printerTask._id, {
    inProgress: true,
    jobId: `${jobId}`,
  });
}
```

**Key Points:**
- Send entire PrinterTask object (Tray App generates commands)
- Check `result.success` for success/failure
- `hasErrors` and `hasPrinterIssues` flags tell you about OTHER jobs (optional to handle)
- Your existing error handling and retry logic still works

---

## Step 3: Update Printer Discovery

In `PrintersContainer.tsx`, replace `getPrinters()`:

### OLD Code
```typescript
import { getPrinters } from "../../../printers/printers";

// On page load
useEffect(() => {
  const discovered = getPrinters();
  setAddPrinters(discovered);
}, []);

// On refresh button
const refreshPrinters = () => {
  const discovered = getPrinters();
  setAddPrinters(discovered);
};
```

### NEW Code
```typescript
import { trayApp } from '../../../services/TrayAppService';

// Single function for both load and refresh
const loadPrinters = async () => {
  try {
    // Get from Tray App
    const data = await trayApp.getPrinters();
    
    // Map to expected format
    const discovered = data.printers.map(p => ({
      name: p.name,
      description: p.displayName || p.name,
      isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
      // NEW data available
      isOnline: p.isOnline,
      status: p.status,
      jobCount: p.jobCount
    }));
    
    // Filter out already added
    const existing = printers.map(p => p.deviceName);
    const newPrinters = discovered.filter(p => !existing.includes(p.name));
    
    setAddPrinters(newPrinters);
    
    // OPTIONAL: Show warning if printers have issues
    if (data.hasPrinterIssues) {
      showNotification({ type: 'warning', message: 'Some printers have issues' });
    }
  } catch (error) {
    console.error('Failed to load printers:', error);
    setAddPrinters([]);
  }
};

// Use for both load and refresh
useEffect(() => { loadPrinters(); }, []);
const refreshPrinters = loadPrinters;
```

**Benefits:**
- Get printer online/offline status
- See which printers have errors
- Know port type and job counts

---

## Step 4: Update Status Refresh

In `PrinterContext.tsx`, replace `getPrinterStatus()` in `updatePrinterStatuses` (aka `refreshLocalStatus`):

### OLD Code
```typescript
import { getPrinterStatus } from "../printers/printers";

const updatePrinterStatuses = useCallback(async () => {
  for (let printer of curPrinters.current) {
    let isOnline = false;
    try {
      // Individual call for EACH printer
      const result = getPrinterStatus(printer.deviceName);
      isOnline = !result.isOffline;
    } catch (e) {
      console.log({ printer, error: e });
    }
    // Update liveness check...
  }
}, []);
```

### NEW Code
```typescript
const updatePrinterStatuses = useCallback(async () => {
  try {
    // ONE call gets ALL printer statuses
    const data = await trayApp.getPrinters();
    
    // Create map for quick lookup
    const statusMap = new Map();
    data.printers.forEach(p => {
      statusMap.set(p.name, {
        isOnline: p.isOnline,
        status: p.status,
        jobCount: p.jobCount
      });
    });
    
    // Update each printer
    for (let printer of curPrinters.current) {
      const printerStatus = statusMap.get(printer.deviceName);
      const isOnline = printerStatus?.isOnline || false;
      
      // Rest of liveness check logic stays the same...
      let livenessCheck: LivenessCheck | null;
      if (isOnline) {
        livenessCheck = { foundTime: new Date(), online: true };
      } else {
        // ... existing offline logic
      }
      
      // Update database
      await printerUpdates.updateWithUpsert(printer._id, {
        livenessCheckByDeviceId: { [deviceId]: livenessCheck }
      });
    }
  } catch (error) {
    console.error('Failed to update statuses:', error);
  }
}, []);
```

**Performance Improvement:**
- OLD: 10 printers = 10 native calls
- NEW: 10 printers = 1 HTTP call

---

## Step 5: Update Job Monitoring

In `PrinterContext.tsx`, replace `getPrinterJob()` in the monitoring interval:

### OLD Code
```typescript
import { getPrinterJob } from "../printers/printers";

// In the interval that checks job status
const job = getPrinterJob(printerTask.printerDeviceName, jobId);
const complete = jobHasStatus(job.status, JobStatus.COMPLETE);
const error = jobHasStatus(job.status, JobStatus.ERROR);
```

### NEW Code
```typescript
// In the interval that checks job status
try {
  const job = await trayApp.getJobStatus(jobId);
  
  if (!job.found) {
    // Job not in spooler = completed
    await printerTaskOps.updateById(printerTask._id, {
      inProgress: false,
      isComplete: true,
      active: false,
    });
    alreadyPrinting.current[printerTask._id.id] = false;
    
  } else if (job.isError) {
    await setPrinterTaskError(printerTask, "Print error");
    
  } else {
    // Check for timeout (existing logic)
    if (isBefore(addSeconds(printerTask.updatedTime, SECONDS_BEFORE_TIMEOUT), new Date())) {
      await setPrinterTaskError(printerTask, "Print task timeout");
    }
  }
} catch (e) {
  // Can't get job = assume completed
  console.log("Assuming task was completed");
  await printerTaskOps.updateById(printerTask._id, {
    inProgress: false,
    isComplete: true,
    active: false,
  });
}
```

---

## 🧪 Testing

### 1. Test with Tray App Running
```bash
# Start Tray App
C:\Program Files\PrinterTrayApp\PrinterTrayApp.exe

# Test in POS
# Should see: "Sending to Tray App: {guid}"
```

### 2. Test Fallback (Tray App Not Running)
```bash
# Don't start Tray App
# Test in POS
# Should see: "Using native print: {guid}"
# Falls back to existing printDirect()
```

### 3. Test Discovery
- Go to Settings → Printers
- Click Refresh
- Should see available printers with status

### 4. Test Status Refresh
- Call `refreshLocalStatus()`
- Should update all printer statuses with one API call

---

## ✅ Benefits

### 1. **Duplicate Prevention**
- Tray App uses PrinterTask._id.id as GUID
- Prevents same job printing twice

### 2. **Better Performance**
- One API call for all printer statuses
- No need to generate ESC/POS commands in POS

### 3. **More Information**
- Printer online/offline status
- Error messages (paper out, door open, etc.)
- Job counts
- Queue visibility

### 4. **Backward Compatibility**
- Keep native module as fallback
- Works even if Tray App not running

---

## 🚨 Important Notes

1. **Port 9877** - Tray App uses this port, must be free
2. **PrinterTask format** - Already compatible, no changes needed
3. **Fallback works** - Native module still available if Tray App down
4. **Optional features** - `hasErrors` and `hasPrinterIssues` flags are optional to handle

---

## 📊 Response Handling Reference

### Print Response
```typescript
{
  success: true,                    // Did print succeed?
  spoolerId: 142,                   // Windows spooler job ID
  guid: "abc-123",                  // PrinterTask._id.id
  hasErrors: false,                 // Are OTHER jobs errored?
  hasPrinterIssues: false,         // Are any printers offline/errored?
  message: "Print job submitted"
}
```

### What the Flags Mean
- `success` - YOUR print job status
- `hasErrors` - OTHER jobs in queue have errors (optional to handle)
- `hasPrinterIssues` - Some printer is offline/errored (optional to handle)

Your print succeeded even if `hasErrors` or `hasPrinterIssues` is true!

---

## 🎯 Summary

1. Add `TrayAppService.ts` (one file)
2. Replace 5 function calls
3. Keep native module as fallback
4. Everything else stays the same
5. Get better status, no duplicates, better performance

**That's it!** The integration is designed to be minimal and non-breaking.

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Status**: Complete Integration Guide