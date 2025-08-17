# 🔄 POS Printer Status Refresh Implementation
**Replace getPrinterStatus() with Tray App API**

## 📋 What refreshLocalStatus Does

The `refreshLocalStatus` function in PrinterContext:
1. Loops through all printers in PrinterSettings
2. Calls `getPrinterStatus()` to check if each is online/offline
3. Updates the `livenessCheckByDeviceId` in database
4. Used to show printer online/offline status in UI

## 🔄 Changes Required

### OLD: Using Native Module
```typescript
// src/contexts/PrinterContext.tsx
import { getPrinterStatus } from "../printers/printers";

const updatePrinterStatuses = useCallback(
  async (setRequestTime?: Date) => {
    if (curPrinters.current) {
      for (let printer of curPrinters.current) {
        const time = new Date();
        let isOnline = false;
        try {
          // OLD: Native module call
          const result = getPrinterStatus(printer.deviceName);
          console.log({ ...printer, getPrinterResult: result });
          isOnline = !result.isOffline;
        } catch (e: any) {
          console.log({ ...printer, error: e });
        }
        
        // Update liveness check...
      }
    }
  },
  [deviceId, printerUpdates]
);
```

### NEW: Using Tray App API
```typescript
// src/contexts/PrinterContext.tsx

const updatePrinterStatuses = useCallback(
  async (setRequestTime?: Date) => {
    if (curPrinters.current) {
      try {
        // NEW: Get ALL printer statuses in one call
        const response = await fetch('http://127.0.0.1:9877/printers');
        const data = await response.json();
        
        // Create a map for quick lookup
        const statusMap = new Map();
        data.printers.forEach(p => {
          statusMap.set(p.name, {
            isOnline: p.isOnline,
            status: p.status,
            statusFlags: p.statusFlags,
            jobCount: p.jobCount
          });
        });
        
        // Update each printer
        for (let printer of curPrinters.current) {
          const time = new Date();
          let isOnline = false;
          
          // Look up status from Tray App data
          const printerStatus = statusMap.get(printer.deviceName);
          if (printerStatus) {
            isOnline = printerStatus.isOnline;
            console.log({ ...printer, trayAppStatus: printerStatus });
          } else {
            // Printer not found in Tray App
            console.log({ ...printer, error: 'Not found in Tray App' });
          }
          
          // Rest of the logic stays the same...
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
          
          // NEW: Also update status and job count if available
          if (printerStatus) {
            update.status = printerStatus.status;
            update.jobCount = printerStatus.jobCount;
          }
          
          if (setRequestTime) {
            update.requestTime = setRequestTime;
          }
          
          if (livenessCheck != null || setRequestTime) {
            await printerUpdates.updateWithUpsert(printer._id, update);
          }
        }
      } catch (error) {
        console.error('Failed to refresh printer statuses:', error);
        
        // Optional: Fallback to native module
        for (let printer of curPrinters.current) {
          try {
            const result = getPrinterStatus(printer.deviceName);
            // ... existing logic
          } catch (e) {
            console.log({ ...printer, error: e });
          }
        }
      }
    }
  },
  [deviceId, printerUpdates]
);
```

## 🎯 Optimization: Single API Call

Instead of calling `getPrinterStatus()` for EACH printer individually, the Tray App API returns ALL printers in one call:

**OLD Way (Multiple Calls):**
```
getPrinterStatus("Printer1") → API call
getPrinterStatus("Printer2") → API call  
getPrinterStatus("Printer3") → API call
// 3 printers = 3 calls
```

**NEW Way (Single Call):**
```
fetch('/printers') → Returns ALL printer statuses
// 1 call for all printers!
```

## 📊 Alternative: Use Health Endpoint

For a quicker status check, you could use `/health`:

```typescript
const updatePrinterStatuses = useCallback(
  async (setRequestTime?: Date) => {
    if (curPrinters.current) {
      try {
        // Quick health check
        const response = await fetch('http://127.0.0.1:9877/health');
        const data = await response.json();
        
        // Create status map from health response
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
          
          // ... rest of update logic
        }
      } catch (error) {
        // Fallback or handle error
      }
    }
  },
  [deviceId, printerUpdates]
);
```

## 🔧 Also Update Job Status Check

The interval that checks job status also uses `getPrinterJob()`:

```typescript
// OLD: Using native module
const job = getPrinterJob(printerTask.printerDeviceName, jobId);

// NEW: Using Tray App
const response = await fetch(`http://127.0.0.1:9877/status/spooler/${jobId}`);
const job = await response.json();

// Or check all jobs at once
const queueResponse = await fetch('http://127.0.0.1:9877/check-queue');
const queue = await queueResponse.json();
const job = queue.jobs.find(j => j.spoolerId == jobId);
```

## 🎯 Summary

### Functions to Change:
1. **updatePrinterStatuses** (refreshLocalStatus) → Use `/printers` or `/health` API
2. **Job status check interval** → Use `/status/spooler/{id}` or `/check-queue`
3. **getPrinters** (discovery) → Use `/printers` API

### Benefits:
- **Fewer API calls** - One call gets all printer statuses
- **More information** - Get job counts, error messages, etc.
- **Better performance** - Single HTTP call vs multiple native calls
- **Consistent data** - All status from same source

### What Stays the Same:
- Database update logic
- Liveness check structure
- Poll request handling
- UI display of status

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Purpose**: Update printer status refresh to use Tray App API