# 🎯 POS Simple Integration Guide
**Minimal changes to use Tray App with existing POS printer system**

## 📋 What We're NOT Changing

- ❌ **NO auto-discovery** - Printers are already configured in Ditto
- ❌ **NO auto-creation** - Admin manually adds printers as before  
- ❌ **NO mapping changes** - Keep existing printer configuration
- ✅ **Just replace print submission** - Use Tray App instead of native module

---

## 🔄 Minimal Required Changes

### 1. Add Simple Tray App Service

```typescript
// src/services/TrayAppService.ts
export class TrayAppService {
  private baseUrl = 'http://127.0.0.1:9877';
  private available = false;
  
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
   * Send print job to Tray App
   */
  async print(printerTask: PrinterTask): Promise<any> {
    if (!this.available) {
      await this.checkAvailable();
      if (!this.available) {
        throw new Error('Printer Tray App not available');
      }
    }
    
    const response = await fetch(`${this.baseUrl}/print`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(printerTask)
    });
    
    if (!response.ok) {
      throw new Error(`Print failed: ${response.statusText}`);
    }
    
    return response.json();
  }
  
  /**
   * Optional: Get printer status for UI
   */
  async getPrinterStatus(printerName: string): Promise<any> {
    try {
      const response = await fetch(`${this.baseUrl}/printers`);
      const data = await response.json();
      const printer = data.printers.find(p => p.name === printerName);
      return {
        isOnline: printer?.isOnline || false,
        status: printer?.status || 'Unknown'
      };
    } catch {
      return { isOnline: false, status: 'Unknown' };
    }
  }
}

export const trayApp = new TrayAppService();
```

---

### 2. Modify PrinterContext.tsx (One Function Change)

```typescript
// src/contexts/PrinterContext.tsx
import { trayApp } from '../services/TrayAppService';

// ONLY CHANGE THE printTasks FUNCTION:
const printTasks = useCallback(async () => {
  console.log("printTasks()");
  
  for (let printerTask of printerTasksToPrint) {
    if (alreadyPrinting.current[printerTask._id.id]) {
      continue;
    }
    alreadyPrinting.current[printerTask._id.id] = true;

    let jobId: number | null = null;
    try {
      // CHECK IF TRAY APP IS AVAILABLE
      const trayAppAvailable = await trayApp.checkAvailable();
      
      if (trayAppAvailable) {
        // NEW: Use Tray App
        console.log(`Sending to Tray App: ${printerTask._id.id}`);
        const result = await trayApp.print(printerTask);
        
        if (result.success) {
          jobId = result.spoolerId;
          await printerTaskOps.updateById(printerTask._id, {
            inProgress: true,
            jobId: `${jobId}`,
          });
        } else {
          throw new Error(result.message || 'Print failed');
        }
      } else {
        // FALLBACK: Use existing native method
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
      // YOUR EXISTING ERROR HANDLING - NO CHANGES
      console.log(
        "Printer task failed, increasing retry count",
        printerTask.printerDeviceName,
        printerTask._id.id
      );
      alreadyPrinting.current[printerTask._id.id] = false;

      await printerTaskOps.updateById(printerTask._id, {
        error: e.toString(),
        retryCount: printerTask.retryCount + 1,
      });

      // YOUR EXISTING RETRY LOGIC - NO CHANGES
      if (printerTask.retryCount + 1 === MAX_RETRY_COUNT) {
        // ... existing secondary printer logic ...
      }
    }
  }
}, [currency, deviceId, deviceQueries, printerTaskOps, printerTasksToPrint]);
```

---

### 3. Optional: Show Printer Status in UI

If you want to show printer online/offline status (optional):

```typescript
// src/modules/settings/containers/PrintersContainer.tsx
import { trayApp } from '../../../services/TrayAppService';

const PrintersContainer = () => {
  const [printerStatuses, setPrinterStatuses] = useState({});
  
  // Optional: Check printer status
  const refreshStatus = useCallback(async () => {
    const statuses = {};
    for (const printer of printers) {
      const status = await trayApp.getPrinterStatus(printer.deviceName);
      statuses[printer.deviceName] = status;
    }
    setPrinterStatuses(statuses);
  }, [printers]);
  
  // Add status column to table
  const printersCols: ColDef<PrinterSettings> = useMemo(
    () => [
      // ... existing columns ...
      
      // OPTIONAL: Add status column
      textCol({
        title: "Status",
        val: (x) => {
          const status = printerStatuses[x.deviceName];
          if (!status) return "Checking...";
          return status.isOnline ? "Online" : "Offline";
        },
        color: (x) => {
          const status = printerStatuses[x.deviceName];
          if (!status) return 'gray';
          return status.isOnline ? 'green' : 'red';
        }
      }),
      
      // ... rest of columns ...
    ],
    [printers, printerStatuses]
  );
  
  return (
    <div>
      {/* OPTIONAL: Add refresh button */}
      <Button onClick={refreshStatus}>Check Status</Button>
      
      {/* Existing DataView */}
      <DataView
        data={printers}
        columns={printersCols}
      />
    </div>
  );
};
```

---

## 🚀 That's It!

### What Changed:
1. ✅ **One new file** - TrayAppService.ts (50 lines)
2. ✅ **One function modified** - printTasks() in PrinterContext
3. ✅ **Optional status display** - Show online/offline in UI

### What Stayed the Same:
- ✅ All printer configuration
- ✅ All retry logic
- ✅ All error handling
- ✅ All Ditto sync
- ✅ All existing UI

### Benefits You Get:
- 🎯 **Duplicate prevention** - Tray App uses GUID
- 📊 **Better error messages** - From Windows spooler
- 🔄 **Fallback support** - Still works if Tray App is down
- 👁️ **Optional status** - Can show printer online/offline

---

## 🔧 Testing

### 1. With Tray App Running:
```bash
# Start Tray App
C:\Program Files\PrinterTrayApp\PrinterTrayApp.exe

# Test print from POS
# Should see: "Sending to Tray App: {guid}"
```

### 2. Without Tray App:
```bash
# Don't start Tray App

# Test print from POS
# Should see: "Using native print: {guid}"
# Falls back to existing printDirect()
```

---

## 📝 Future Enhancements (When You're Ready)

Later, you could add:
- Notification when new printers are discovered
- Queue monitoring
- Better status display
- Remove native module completely

But for now, this minimal integration gets you all the benefits with almost no changes!

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Purpose**: Simplest possible integration keeping existing POS printer system