# 🔧 POS Refactoring Guide - Replace Old Printer System
**Complete removal of native printer module and replacement with Tray App API**

## 🎯 What Needs to Change - Function by Function

### 1. ❌ REMOVE Native Printer Module

**DELETE these files:**
```
src/printers/
├── printers.node          ❌ DELETE
├── printers.js            ❌ DELETE  
├── printers.d.ts          ❌ DELETE (replace with new service)
└── enums.ts               ✅ KEEP (still need job status enums)
```

**REMOVE from package.json:**
```json
{
  "dependencies": {
    "node-printer": "...",  ❌ REMOVE
  }
}
```

---

## 📝 Function Replacements

### 1. getPrinters() → GET /printers

**OLD CODE (PrintersContainer.tsx):**
```typescript
import { getPrinters } from "../../../printers/printers";

// In refreshPrinters function
const discoveredPrinters = getPrinters();
// Returns: { name: string, description: string, isNetwork: boolean }[]
```

**NEW CODE:**
```typescript
import { printerTrayApp } from "../../../services/PrinterTrayAppService";

// In refreshPrinters function
const response = await fetch('http://127.0.0.1:9877/printers');
const data = await response.json();
const discoveredPrinters = data.printers.map(p => ({
  name: p.name,
  description: p.displayName || p.comment || p.name,
  isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
  // NEW data we get:
  isOnline: p.isOnline,
  status: p.status,
  jobCount: p.jobCount,
  hasIssues: !p.isOnline || p.status !== 'Ready'
}));
```

### 2. getPrinterStatus() → GET /printers or /health

**OLD CODE:**
```typescript
import { getPrinterStatus } from "../../../printers/printers";

const status = getPrinterStatus(printerName);
// Returns: { status: number, isOffline: boolean, attributes: number }
```

**NEW CODE:**
```typescript
// Option 1: Get all printers and find the one
const response = await fetch('http://127.0.0.1:9877/printers');
const data = await response.json();
const printer = data.printers.find(p => p.name === printerName);
const status = {
  status: printer.statusFlags,
  isOffline: !printer.isOnline,
  attributes: printer.statusFlags,
  // NEW: Better status info
  statusText: printer.status,
  errorMessage: printer.error
};

// Option 2: Use health endpoint for quick check
const response = await fetch('http://127.0.0.1:9877/health');
const data = await response.json();
const printer = data.printers.find(p => p.name === printerName);
```

### 3. printDirect() → POST /print

**OLD CODE (PrinterContext.tsx):**
```typescript
import { printDirect } from "../printers/printers";

// In printTasks function
const jobId = printDirect(
  printerTask.printerDeviceName,  // printer name
  printerTask._id.id,             // document name
  "RAW",                           // type
  commands                         // ESC/POS commands
);
```

**NEW CODE:**
```typescript
// Send the ENTIRE PrinterTask object - Tray App handles everything!
const response = await fetch('http://127.0.0.1:9877/print', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(printerTask)  // Send whole task, not just commands!
});

const result = await response.json();
const jobId = result.spoolerId;

// IMPORTANT: Check for issues
if (result.hasErrors || result.hasPrinterIssues) {
  // Check queue for problems
  const queueResponse = await fetch('http://127.0.0.1:9877/check-queue');
  const queue = await queueResponse.json();
  // Handle errors...
}
```

### 4. getPrinterJobs() → GET /check-queue

**OLD CODE:**
```typescript
import { getPrinterJobs } from "../printers/printers";

const jobs = getPrinterJobs(printerName);
// Returns: { jobId: string, status: string }[]
```

**NEW CODE:**
```typescript
// Get ALL jobs from ALL printers with much more detail
const response = await fetch('http://127.0.0.1:9877/check-queue');
const data = await response.json();

// Filter for specific printer if needed
const jobs = data.jobs
  .filter(j => j.printerName === printerName)
  .map(j => ({
    jobId: j.spoolerId.toString(),
    status: j.status,
    // NEW data available:
    guid: j.guid,
    isError: j.isError,
    isStuck: j.isStuck,
    ageSeconds: j.ageSeconds,
    errorMessage: j.errorMessage,
    position: j.position
  }));
```

### 5. getPrinterJob() → GET /status/spooler/{id}

**OLD CODE:**
```typescript
import { getPrinterJob } from "../printers/printers";

const job = getPrinterJob(printerName, jobId);
// Returns: { jobId: number, status: number }
```

**NEW CODE:**
```typescript
const response = await fetch(`http://127.0.0.1:9877/status/spooler/${jobId}`);
const job = await response.json();

// Returns much more detail:
{
  spoolerId: jobId,
  guid: "...",
  found: true,
  status: "printing",
  isError: false,
  isPrinting: true
}
```

### 6. sendJobCommand() → POST /retry-queue

**OLD CODE:**
```typescript
import { sendJobCommand } from "../printers/printers";
import { JobCommand } from "../printers/enums";

sendJobCommand(printerName, jobId, JobCommand.Restart);
sendJobCommand(printerName, jobId, JobCommand.Cancel);
```

**NEW CODE:**
```typescript
// For retry
const response = await fetch('http://127.0.0.1:9877/retry-queue', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ printerName })
});

// Note: Individual job cancel might need to be added to Tray App API
// Or use Windows API directly through Tray App
```

---

## 🔄 Complete PrinterContext.tsx Refactor

**REMOVE:**
```typescript
import { getPrinterJob, getPrinterStatus, printDirect, sendJobCommand } from "../printers/printers";
```

**ADD:**
```typescript
// New Tray App Service
class TrayAppService {
  private baseUrl = 'http://127.0.0.1:9877';
  
  async print(printerTask: PrinterTask) {
    const response = await fetch(`${this.baseUrl}/print`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(printerTask)
    });
    return response.json();
  }
  
  async checkQueue() {
    const response = await fetch(`${this.baseUrl}/check-queue`);
    return response.json();
  }
  
  async getPrinters() {
    const response = await fetch(`${this.baseUrl}/printers`);
    return response.json();
  }
  
  async getHealth() {
    const response = await fetch(`${this.baseUrl}/health`);
    return response.json();
  }
  
  async retryQueue(printerName: string) {
    const response = await fetch(`${this.baseUrl}/retry-queue`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ printerName })
    });
    return response.json();
  }
}

const trayApp = new TrayAppService();
```

**REPLACE printTasks function:**
```typescript
const printTasks = useCallback(async () => {
  for (let printerTask of printerTasksToPrint) {
    if (alreadyPrinting.current[printerTask._id.id]) {
      continue;
    }
    alreadyPrinting.current[printerTask._id.id] = true;

    try {
      // OLD: Generate commands and use printDirect
      // let commands = await makeCommands(currency, printerTask);
      // const jobId = printDirect(...);
      
      // NEW: Send entire task to Tray App
      const result = await trayApp.print(printerTask);
      
      if (result.success) {
        await printerTaskOps.updateById(printerTask._id, {
          inProgress: true,
          jobId: result.spoolerId.toString(),
          // Store additional info
          windowsSpoolerId: result.spoolerId,
          trayAppGuid: result.guid
        });
        
        // Check for queue issues
        if (result.hasErrors || result.hasPrinterIssues) {
          const queue = await trayApp.checkQueue();
          await handleQueueIssues(queue);
        }
      } else {
        throw new Error(result.message || 'Print failed');
      }
    } catch (e) {
      // Error handling...
    }
  }
}, [printerTasksToPrint]);
```

---

## 🔄 Complete PrintersContainer.tsx Refactor

**REPLACE refreshPrinters function:**
```typescript
const refreshPrinters = useCallback(async () => {
  try {
    // OLD: const nativePrinters = getPrinters();
    
    // NEW: Get from Tray App
    const response = await trayApp.getPrinters();
    
    // Create or update printers in Ditto
    for (const printer of response.printers) {
      const existing = printers.find(p => p.deviceName === printer.name);
      
      if (existing) {
        // Update existing
        await printerOps.updateById(existing._id, {
          active: printer.isOnline,
          on: printer.isOnline,
          status: printer.status,
          jobCount: printer.jobCount,
          port: printer.port,
          isNetwork: printer.portType === 'NetworkIP'
        });
      } else {
        // Create new
        await printerOps.insert({
          ...newSiteObject(),
          deviceName: printer.name,
          customName: printer.displayName || printer.name,
          active: printer.isOnline,
          on: printer.isOnline,
          status: printer.status,
          isNetwork: printer.portType === 'NetworkIP',
          port: printer.port
        });
      }
    }
    
    // Remove printers that no longer exist
    for (const existing of printers) {
      if (!response.printers.find(p => p.name === existing.deviceName)) {
        await printerOps.updateById(existing._id, {
          active: false,
          status: 'Not Found'
        });
      }
    }
  } catch (error) {
    console.error('Failed to refresh printers:', error);
  }
}, [printers, printerOps]);
```

---

## 📊 Status Monitoring Changes

**ADD new monitoring effect in PrinterContext:**
```typescript
// Monitor print queue every 20 seconds
useEffect(() => {
  const interval = setInterval(async () => {
    try {
      const queue = await trayApp.checkQueue();
      
      // Update status for all active tasks
      for (const task of printerTasksToPrint) {
        const queueJob = queue.jobs.find(j => j.guid === task._id.id);
        
        if (!queueJob && task.inProgress) {
          // Job disappeared = completed
          await printerTaskOps.updateById(task._id, {
            isComplete: true,
            inProgress: false
          });
          alreadyPrinting.current[task._id.id] = false;
          
        } else if (queueJob?.isError) {
          // Job has error
          await printerTaskOps.updateById(task._id, {
            error: queueJob.errorMessage,
            inProgress: false
          });
          alreadyPrinting.current[task._id.id] = false;
          
        } else if (queueJob?.isStuck) {
          // Job is stuck
          console.warn(`Job ${task._id.id} is stuck for ${queueJob.ageSeconds} seconds`);
        }
      }
    } catch (error) {
      console.error('Queue check failed:', error);
    }
  }, 20000); // Every 20 seconds
  
  return () => clearInterval(interval);
}, [printerTasksToPrint, printerTaskOps]);
```

---

## 🚀 Launch Tray App on Startup

**ADD to main index.ts or App.tsx:**
```typescript
// On app startup
async function initializePrinting() {
  try {
    // Check if Tray App is running
    const health = await trayApp.getHealth();
    
    if (!health.ok) {
      // Try to launch it
      const { exec } = window.require('child_process');
      const trayAppPath = 'C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe';
      
      exec(`"${trayAppPath}"`, (error) => {
        if (error) {
          console.error('Could not launch Tray App:', error);
          alert('Printer service not available. Please start PrinterTrayApp manually.');
        }
      });
      
      // Wait for it to start
      await new Promise(resolve => setTimeout(resolve, 3000));
      
      // Check again
      const retry = await trayApp.getHealth();
      if (!retry.ok) {
        console.error('Tray App failed to start');
      }
    }
    
    // Check for printer issues
    if (health.hasPrinterIssues) {
      console.warn('Some printers have issues');
      // Show notification to user
    }
  } catch (error) {
    console.error('Failed to initialize printing:', error);
  }
}

// Call on startup
initializePrinting();
```

---

## ✅ Complete Removal Checklist

### Files to DELETE:
- [ ] `src/printers/printers.node`
- [ ] `src/printers/printers.js`
- [ ] `src/printers/printers.d.ts`
- [ ] Remove `node-printer` from package.json
- [ ] Remove `binding.gyp` if only used for printers

### Code to REMOVE:
- [ ] All imports from `"../printers/printers"`
- [ ] `getPrinters()` calls
- [ ] `getPrinterStatus()` calls
- [ ] `printDirect()` calls
- [ ] `getPrinterJobs()` calls
- [ ] `getPrinterJob()` calls
- [ ] `sendJobCommand()` calls

### Code to ADD:
- [ ] TrayAppService class
- [ ] Tray App health checks
- [ ] Queue monitoring
- [ ] Auto-launch on startup
- [ ] Error/issue handling from API responses

### New Features to Implement:
- [ ] Show printer online/offline status
- [ ] Display job counts
- [ ] Monitor stuck jobs
- [ ] Show queue position
- [ ] Handle `hasErrors` flag
- [ ] Handle `hasPrinterIssues` flag

---

## 🎯 Migration Steps

1. **Create TrayAppService.ts** with all API calls
2. **Update PrinterContext.tsx** to use TrayAppService
3. **Update PrintersContainer.tsx** for discovery/status
4. **Add queue monitoring** (20-second interval)
5. **Add Tray App launcher** on startup
6. **Remove old imports** from printers module
7. **Delete old printer files**
8. **Test everything**

---

## ⚠️ Breaking Changes

1. **PrinterTask must be complete** - The Tray App expects the full PrinterTask object, not just commands
2. **Job IDs are different** - Windows spooler IDs instead of internal IDs
3. **Status values changed** - Text status ("printing", "error") instead of numeric flags
4. **Discovery returns more data** - Additional fields in printer info

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Purpose**: Complete replacement of native printer module with Tray App API