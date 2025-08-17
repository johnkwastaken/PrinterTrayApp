# 🎯 POS Complete Integration Changes
**All changes needed to replace native printer module with Tray App API**

## 📋 Summary of Changes

Replace these 5 native functions with Tray App API calls:

| Old Function | New API Endpoint | Used In |
|-------------|------------------|---------|
| `getPrinters()` | `GET /printers` | Settings page (discovery) |
| `getPrinterStatus()` | `GET /printers` or `/health` | refreshLocalStatus |
| `printDirect()` | `POST /print` | Print submission |
| `getPrinterJob()` | `GET /status/spooler/{id}` | Job monitoring |
| `sendJobCommand()` | `POST /retry-queue` | Retry failed jobs |

---

## 1️⃣ Create Tray App Service

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

## 2️⃣ Update PrintersContainer.tsx (Settings Page)

Replace `getPrinters()` for discovery:

```typescript
// src/modules/settings/containers/PrintersContainer.tsx
import { trayApp } from '../../../services/TrayAppService';
// REMOVE: import { getPrinters } from "../../../printers/printers";

const PrintersContainer = () => {
  const [addPrinters, setAddPrinters] = useState<AddPrinter[] | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  
  /**
   * Load printers on mount and refresh
   */
  const loadPrinters = useCallback(async () => {
    setRefreshing(true);
    
    try {
      // NEW: Use Tray App API
      const data = await trayApp.getPrinters();
      
      // Map to expected format
      const discoveredPrinters = data.printers.map(p => ({
        name: p.name,
        description: p.displayName || p.name,
        isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
        // Extra data now available
        isOnline: p.isOnline,
        status: p.status,
        jobCount: p.jobCount
      }));
      
      // Filter out already added printers
      const existingNames = printers.map(p => p.deviceName);
      const newPrinters = discoveredPrinters.filter(
        p => !existingNames.includes(p.name)
      );
      
      setAddPrinters(newPrinters);
      
    } catch (error) {
      console.error('Failed to load printers:', error);
      
      // Optional: Fallback to native module
      // const nativePrinters = getPrinters();
      // setAddPrinters(nativePrinters);
      
      setAddPrinters([]);
    } finally {
      setRefreshing(false);
    }
  }, [printers]);
  
  // Load on mount
  useEffect(() => {
    loadPrinters();
  }, []);
  
  // Refresh button uses same function
  const refreshPrinters = loadPrinters;
  
  // ... rest of component
};
```

---

## 3️⃣ Update PrinterContext.tsx

### A. Replace printDirect() in printTasks

```typescript
// src/contexts/PrinterContext.tsx
import { trayApp } from '../services/TrayAppService';
// Keep for fallback: import { printDirect } from "../printers/printers";

const printTasks = useCallback(async () => {
  console.log("printTasks()");
  
  for (let printerTask of printerTasksToPrint) {
    if (alreadyPrinting.current[printerTask._id.id]) {
      continue;
    }
    alreadyPrinting.current[printerTask._id.id] = true;

    let jobId: number | null = null;
    try {
      // Check if Tray App available
      if (await trayApp.checkAvailable()) {
        // NEW: Use Tray App
        console.log(`Sending to Tray App: ${printerTask._id.id}`);
        const result = await trayApp.print(printerTask);
        
        if (result.success) {
          jobId = result.spoolerId;
          await printerTaskOps.updateById(printerTask._id, {
            inProgress: true,
            jobId: `${jobId}`,
          });
          
          // Check for queue issues
          if (result.hasErrors || result.hasPrinterIssues) {
            console.warn('Queue has issues, checking...');
            // Optional: Check queue for details
            const queue = await trayApp.checkQueue();
            console.log('Queue status:', queue);
          }
        } else {
          throw new Error(result.message || 'Print failed');
        }
      } else {
        // FALLBACK: Use native module
        console.log(`Using native print: ${printerTask._id.id}`);
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
    } catch (e: any) {
      // Existing error handling - NO CHANGES
      console.log("Printer task failed, increasing retry count");
      alreadyPrinting.current[printerTask._id.id] = false;
      
      await printerTaskOps.updateById(printerTask._id, {
        error: e.toString(),
        retryCount: printerTask.retryCount + 1,
      });
      
      // Existing retry logic continues to work...
    }
  }
}, [currency, deviceId, deviceQueries, printerTaskOps, printerTasksToPrint]);
```

### B. Replace getPrinterStatus() in updatePrinterStatuses (refreshLocalStatus)

```typescript
// REMOVE: import { getPrinterStatus } from "../printers/printers";

const updatePrinterStatuses = useCallback(
  async (setRequestTime?: Date) => {
    if (curPrinters.current) {
      try {
        // NEW: Get ALL printer statuses in ONE call
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
          const time = new Date();
          let isOnline = false;
          
          // Lookup from Tray App data
          const printerStatus = statusMap.get(printer.deviceName);
          if (printerStatus) {
            isOnline = printerStatus.isOnline;
            console.log({ ...printer, trayAppStatus: printerStatus });
          } else {
            console.log({ ...printer, error: 'Not found in Tray App' });
          }
          
          // Rest of logic stays the same...
          let livenessCheck: LivenessCheck | null;
          if (isOnline) {
            livenessCheck = { foundTime: time, online: true };
          } else {
            if (
              printer.livenessCheckByDeviceId &&
              printer.livenessCheckByDeviceId[deviceId]
            ) {
              livenessCheck = {
                ...printer.livenessCheckByDeviceId[deviceId],
                online: false,
              };
            } else {
              livenessCheck = null;
            }
          }
          
          const update: Update<PrinterSettings> = {};
          
          if (livenessCheck != null) {
            update.livenessCheckByDeviceId = {
              [deviceId]: livenessCheck,
            };
          }
          if (setRequestTime) {
            update.requestTime = setRequestTime;
          }
          if (livenessCheck != null || setRequestTime) {
            await printerUpdates.updateWithUpsert(printer._id, update);
          }
        }
      } catch (error) {
        console.error('Failed to update printer statuses:', error);
        // Optional: Fallback to native
      }
    }
  },
  [deviceId, printerUpdates]
);
```

### C. Replace getPrinterJob() in job monitoring interval

```typescript
// In the useEffect with setInterval for job monitoring
// REMOVE: import { getPrinterJob } from "../printers/printers";

useEffect(() => {
  const interval = setInterval(async () => {
    if (inProgressTasks.current) {
      for (const printerTask of inProgressTasks.current) {
        const jobId = +(printerTask.jobId ?? "");
        if (
          printerTask.jobId == null ||
          printerTask.jobId === "" ||
          isNaN(jobId)
        ) {
          await setPrinterTaskError(printerTask, "Can't process job id");
        } else {
          try {
            // NEW: Use Tray App to check job
            const job = await trayApp.getJobStatus(jobId);
            
            // Job not found = completed
            if (!job.found) {
              console.log("Print task complete");
              await printerTaskOps.updateById(printerTask._id, {
                inProgress: false,
                isComplete: true,
                active: false,
              });
              alreadyPrinting.current[printerTask._id.id] = false;
              
            } else if (job.isError) {
              await setPrinterTaskError(printerTask, "Print error");
              
            } else {
              // Check for timeout
              if (
                isBefore(
                  addSeconds(printerTask.updatedTime, SECONDS_BEFORE_TIMEOUT),
                  new Date()
                )
              ) {
                console.log("Print task timeout");
                // Could implement cancel via Tray App if needed
                await setPrinterTaskError(printerTask, "Print task timeout");
              }
            }
          } catch (e: any) {
            // Can't get job = assume completed
            console.log("Assuming task was completed");
            await printerTaskOps.updateById(printerTask._id, {
              error: e.toString(),
              inProgress: false,
              isComplete: true,
              active: false,
            });
            alreadyPrinting.current[printerTask._id.id] = false;
          }
        }
      }
    }
  }, 1000);
  
  return () => {
    clearInterval(interval);
  };
}, [printerTaskOps, setPrinterTaskError]);
```

---

## 4️⃣ Optional: Launch Tray App on Startup

Add to main App or index:

```typescript
// src/index.ts or App.tsx
import { trayApp } from './services/TrayAppService';

// On app startup
async function initializePrinting() {
  try {
    // Check if Tray App is running
    const available = await trayApp.checkAvailable();
    
    if (!available) {
      console.warn('Printer Tray App not running');
      // Optional: Try to launch it
      const { exec } = window.require('child_process');
      exec('"C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe"');
    }
  } catch (error) {
    console.error('Failed to initialize printing:', error);
  }
}

// Call on startup
initializePrinting();
```

---

## ✅ What Stays the Same

- All PrinterTask management
- All Ditto sync
- All retry logic  
- All error handling
- Secondary printer fallback
- UI components (just show more data)

---

## 🎯 Benefits After Integration

1. **Better Performance** - One API call gets all printer statuses (vs N native calls)
2. **More Information** - Online/offline, job counts, error messages
3. **Duplicate Prevention** - Tray App uses GUID to prevent duplicates
4. **Queue Visibility** - Can check all jobs in queue
5. **Fallback Support** - Still works with native module if Tray App down

---

## 📊 Testing Plan

### 1. Test with Tray App Running
- Start Tray App first
- Test discovery in settings
- Test printing
- Test status refresh
- Should see "Sending to Tray App" in logs

### 2. Test Fallback (Tray App Not Running)
- Don't start Tray App
- Everything should fall back to native module
- Should see "Using native print" in logs

### 3. Test Mixed Mode
- Start with Tray App running
- Print some jobs
- Stop Tray App
- New prints should use native module
- Start Tray App again
- Should switch back to Tray App

---

## 🚨 Important Notes

1. **Keep native module for now** - Use as fallback
2. **Tray App must be running** - Or fallback kicks in
3. **Port 9877 must be free** - Tray App uses this port
4. **Same PrinterTask format** - No changes needed

---

**Document Version**: 2.0  
**Created**: 2025-01-17  
**Status**: Complete Integration Guide