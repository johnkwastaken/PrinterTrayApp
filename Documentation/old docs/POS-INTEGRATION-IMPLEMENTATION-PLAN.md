# 🎯 POS Integration Implementation Plan
**Integrating Windows Printer Tray App with dnero-desktop-pos**

## 📋 Executive Summary

The POS currently uses a native Node.js addon (`printers.node`) for direct Windows printing and Ditto for data sync. We need to integrate the new Windows Printer Tray App HTTP API while maintaining backward compatibility and leveraging existing POS infrastructure.

**Key Integration Points:**
1. Replace `printDirect()` calls with HTTP API calls
2. Enhance printer discovery to use Tray App endpoints
3. Add queue monitoring and error recovery
4. Implement offline printer detection
5. Maintain PrinterTask structure compatibility

---

## 🏗️ Current POS Architecture Analysis

### Existing Printer Components

```
src/
├── printers/                   # Native printer module
│   ├── printers.node          # C++ Node addon (current)
│   ├── printers.d.ts          # TypeScript definitions
│   └── enums.ts               # Job status enums
│
├── contexts/
│   └── PrinterContext.tsx     # Main printing logic
│
├── modules/
│   ├── printer/               # Print task generation
│   │   ├── hooks/            # Print task hooks
│   │   ├── templates/        # Receipt templates
│   │   └── helpers/          # Command builders
│   │
│   └── settings/
│       └── containers/       # Printer management UI
│           └── PrintersContainer.tsx
│
└── entities/
    ├── printerTaskEntities.ts  # PrinterTask model
    └── printerSettingsEntities.ts
```

### Current Flow
1. **PrinterContext** manages print queue
2. **printDirect()** sends RAW data to Windows spooler
3. **Ditto** syncs PrinterTasks across devices
4. **Native module** provides printer discovery

---

## 🔄 Integration Strategy

### Phase 1: Create HTTP Service Layer

Create a new service to handle Tray App communication:

```typescript
// src/services/PrinterTrayAppService.ts
export class PrinterTrayAppService {
  private readonly baseUrl = 'http://127.0.0.1:9877';
  private isAvailable = false;
  private lastHealthCheck: Date | null = null;
  
  // Check if Tray App is running
  async checkHealth(): Promise<boolean> {
    try {
      const response = await fetch(`${this.baseUrl}/health`);
      const data = await response.json();
      this.isAvailable = data.ok;
      this.lastHealthCheck = new Date();
      return data.ok;
    } catch {
      this.isAvailable = false;
      return false;
    }
  }
  
  // Submit print job
  async print(printerTask: PrinterTask): Promise<PrintResult> {
    const response = await fetch(`${this.baseUrl}/print`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(printerTask)
    });
    
    const result = await response.json();
    
    // Check for queue issues
    if (result.hasErrors || result.hasPrinterIssues) {
      // Trigger background queue check
      setTimeout(() => this.checkQueue(), 100);
    }
    
    return {
      success: result.success,
      guid: result.guid || printerTask._id.id,
      spoolerId: result.spoolerId,
      hasErrors: result.hasErrors,
      hasPrinterIssues: result.hasPrinterIssues
    };
  }
  
  // Get printer list with status
  async getPrinters(): Promise<PrinterInfo[]> {
    const response = await fetch(`${this.baseUrl}/printers`);
    const data = await response.json();
    
    // Map to POS format
    return data.printers.map(p => ({
      name: p.name,
      deviceName: p.name,
      customName: p.displayName || p.name,
      isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
      isOnline: p.isOnline,
      status: p.status,
      jobCount: p.jobCount,
      supportsRaw: p.supportsRaw,
      port: p.port,
      hasIssues: !p.isOnline || p.status !== 'Ready'
    }));
  }
  
  // Check print queue
  async checkQueue(): Promise<QueueStatus> {
    const response = await fetch(`${this.baseUrl}/check-queue`);
    return await response.json();
  }
  
  // Launch Tray App if not running
  async launchTrayApp(): Promise<boolean> {
    const { exec } = window.require('child_process');
    const path = 'C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe';
    
    return new Promise((resolve) => {
      exec(`"${path}"`, (error) => {
        if (error) {
          console.error('Failed to launch Tray App:', error);
          resolve(false);
        } else {
          // Wait for app to be ready
          setTimeout(async () => {
            const ready = await this.checkHealth();
            resolve(ready);
          }, 3000);
        }
      });
    });
  }
}

export const printerTrayApp = new PrinterTrayAppService();
```

---

### Phase 2: Modify PrinterContext

Update `PrinterContext.tsx` to use Tray App when available:

```typescript
// src/contexts/PrinterContext.tsx
import { printerTrayApp } from '../services/PrinterTrayAppService';

export const PrinterContextProvider = ({ children }: PropsWithChildren<{}>) => {
  const [useTrayApp, setUseTrayApp] = useState(false);
  const [trayAppStatus, setTrayAppStatus] = useState<'checking' | 'available' | 'unavailable'>('checking');
  
  // Check Tray App on startup
  useEffect(() => {
    const checkTrayApp = async () => {
      const available = await printerTrayApp.checkHealth();
      if (!available) {
        // Try to launch it
        const launched = await printerTrayApp.launchTrayApp();
        setUseTrayApp(launched);
        setTrayAppStatus(launched ? 'available' : 'unavailable');
      } else {
        setUseTrayApp(true);
        setTrayAppStatus('available');
      }
    };
    
    checkTrayApp();
    
    // Health check every 60 seconds
    const interval = setInterval(checkTrayApp, 60000);
    return () => clearInterval(interval);
  }, []);
  
  // Modified print function
  const printTasks = useCallback(async () => {
    for (let printerTask of printerTasksToPrint) {
      if (alreadyPrinting.current[printerTask._id.id]) {
        continue;
      }
      alreadyPrinting.current[printerTask._id.id] = true;
      
      try {
        if (useTrayApp) {
          // Use Tray App API
          const result = await printerTrayApp.print(printerTask);
          
          if (result.success) {
            await printerTaskOps.updateById(printerTask._id, {
              inProgress: true,
              jobId: `${result.spoolerId}`,
              // Store Windows spooler ID
              windowsSpoolerId: result.spoolerId,
              trayAppGuid: result.guid
            });
            
            // Handle queue issues
            if (result.hasErrors || result.hasPrinterIssues) {
              console.warn('Queue has issues, checking status...');
              const queue = await printerTrayApp.checkQueue();
              await handleQueueIssues(queue);
            }
          } else {
            throw new Error(result.message || 'Print failed');
          }
        } else {
          // Fallback to native module
          let commands = await makeCommands(currency, printerTask);
          const jobId = printDirect(
            printerTask.printerDeviceName,
            printerTask._id.id,
            "RAW",
            commands
          );
          
          await printerTaskOps.updateById(printerTask._id, {
            inProgress: true,
            jobId: `${jobId}`
          });
        }
      } catch (e: any) {
        // Error handling...
        alreadyPrinting.current[printerTask._id.id] = false;
        await handlePrintError(printerTask, e);
      }
    }
  }, [printerTasksToPrint, useTrayApp]);
  
  // Handle queue issues
  const handleQueueIssues = async (queue: QueueStatus) => {
    for (const job of queue.jobs) {
      if (job.isError) {
        // Find matching PrinterTask by GUID
        const task = await printerTaskOps.findOne(
          `_id.id == "${job.guid}"`
        );
        
        if (task) {
          await printerTaskOps.updateById(task._id, {
            error: job.errorMessage,
            isComplete: false,
            inProgress: false
          });
          
          // Alert user
          showNotification({
            type: 'error',
            title: 'Print Error',
            message: `${task.name}: ${job.errorMessage}`
          });
        }
      }
      
      if (job.isStuck && job.ageSeconds > 30) {
        // Alert about stuck job
        showNotification({
          type: 'warning',
          title: 'Print Stuck',
          message: `Job ${job.guid} stuck for ${job.ageFormatted}`
        });
      }
    }
  };
  
  // Status monitoring
  useEffect(() => {
    if (!useTrayApp) return;
    
    const interval = setInterval(async () => {
      const activeTasks = printerTasksToPrint.filter(t => t.inProgress);
      if (activeTasks.length === 0) return;
      
      const queue = await printerTrayApp.checkQueue();
      
      for (const task of activeTasks) {
        const queueJob = queue.jobs.find(j => j.guid === task._id.id);
        
        if (!queueJob) {
          // Not in queue = completed
          await printerTaskOps.updateById(task._id, {
            isComplete: true,
            inProgress: false,
            error: null
          });
          alreadyPrinting.current[task._id.id] = false;
        }
      }
    }, 20000); // Every 20 seconds
    
    return () => clearInterval(interval);
  }, [printerTasksToPrint, useTrayApp]);
};
```

---

### Phase 3: Update Printer Discovery

Modify `PrintersContainer.tsx` to use Tray App discovery:

```typescript
// src/modules/settings/containers/PrintersContainer.tsx
import { printerTrayApp } from '../../../services/PrinterTrayAppService';

const PrintersContainer = () => {
  const [trayAppPrinters, setTrayAppPrinters] = useState<PrinterInfo[]>([]);
  const [refreshing, setRefreshing] = useState(false);
  
  // Refresh printer list
  const refreshPrinters = useCallback(async () => {
    setRefreshing(true);
    
    try {
      // Try Tray App first
      const trayPrinters = await printerTrayApp.getPrinters();
      setTrayAppPrinters(trayPrinters);
      
      // Update status in Ditto
      for (const printer of trayPrinters) {
        const existing = printers.find(p => p.deviceName === printer.name);
        if (existing) {
          await printerOps.updateById(existing._id, {
            isOnline: printer.isOnline,
            status: printer.status,
            lastStatusCheck: new Date(),
            hasIssues: printer.hasIssues
          });
        }
      }
    } catch {
      // Fallback to native discovery
      const nativePrinters = getPrinters();
      // ... handle native printers
    } finally {
      setRefreshing(false);
    }
  }, [printers, printerOps]);
  
  // Auto-refresh on mount and periodically
  useEffect(() => {
    refreshPrinters();
    const interval = setInterval(refreshPrinters, 60000);
    return () => clearInterval(interval);
  }, []);
  
  // Enhanced columns with status
  const printersCols: ColDef<PrinterSettings> = useMemo(
    () => [
      textCol({
        title: "Name",
        val: (x) => x.customName,
      }),
      textCol({
        title: "Device Name",
        val: (x) => x.deviceName,
      }),
      // NEW: Status column
      textCol({
        title: "Status",
        val: (x) => {
          const trayPrinter = trayAppPrinters.find(p => p.name === x.deviceName);
          return trayPrinter?.status || 'Unknown';
        },
        color: (x) => {
          const trayPrinter = trayAppPrinters.find(p => p.name === x.deviceName);
          if (!trayPrinter) return 'gray';
          if (trayPrinter.isOnline && trayPrinter.status === 'Ready') return 'green';
          if (trayPrinter.hasIssues) return 'red';
          return 'orange';
        }
      }),
      // NEW: Job count
      textCol({
        title: "Jobs",
        val: (x) => {
          const trayPrinter = trayAppPrinters.find(p => p.name === x.deviceName);
          return trayPrinter?.jobCount || 0;
        }
      }),
      checkBoxCol({
        title: "Active",
        val: (x) => x.active,
        filter: setFilterWithInitialSelection(true),
        align: "center",
      }),
      actionsCol({
        title: "Actions",
        val: () => undefined,
        options: {
          actions: [
            {
              title: "Test Print",
              icon: <FontAwesomeIcon icon={faPrint} />,
              onClick: async (x) => {
                // Use Tray App if available
                if (await printerTrayApp.checkHealth()) {
                  const task = await makeTask(register, x);
                  await printerTrayApp.print(task);
                } else {
                  // Fallback to existing method
                  const task = await makeTask(register, x);
                  await printerTaskOps.insert(task);
                }
              },
            },
            // NEW: Refresh status action
            {
              title: "Refresh Status",
              icon: <FontAwesomeIcon icon={faSync} />,
              onClick: async () => {
                await refreshPrinters();
              }
            }
          ],
        },
        align: "center",
      }),
    ],
    [trayAppPrinters, register, makeTask, printerTaskOps, refreshPrinters]
  );
  
  return (
    <div>
      {/* Status bar */}
      <StatusBar>
        <span>Printer Service: {trayAppStatus}</span>
        {trayAppPrinters.some(p => p.hasIssues) && (
          <Alert type="warning">Some printers have issues</Alert>
        )}
        <Button onClick={refreshPrinters} loading={refreshing}>
          Refresh
        </Button>
      </StatusBar>
      
      {/* Existing DataView */}
      <DataView
        data={printers}
        columns={printersCols}
        // ...
      />
    </div>
  );
};
```

---

### Phase 4: Add Queue Monitoring UI

Create a new component for queue monitoring:

```typescript
// src/modules/printer/components/PrintQueueMonitor.tsx
import React, { useEffect, useState } from 'react';
import { printerTrayApp } from '../../../services/PrinterTrayAppService';

export const PrintQueueMonitor: React.FC = () => {
  const [queue, setQueue] = useState<QueueStatus | null>(null);
  const [visible, setVisible] = useState(false);
  
  useEffect(() => {
    const checkQueue = async () => {
      const queueStatus = await printerTrayApp.checkQueue();
      setQueue(queueStatus);
      
      // Show monitor if there are issues
      if (queueStatus.hasErrors || queueStatus.hasStuckJobs) {
        setVisible(true);
      }
    };
    
    // Check every 20 seconds
    const interval = setInterval(checkQueue, 20000);
    checkQueue(); // Initial check
    
    return () => clearInterval(interval);
  }, []);
  
  if (!visible || !queue) return null;
  
  return (
    <QueueMonitorPanel>
      <Header>
        <Title>Print Queue Status</Title>
        <CloseButton onClick={() => setVisible(false)} />
      </Header>
      
      <Summary>
        <Stat>
          <Label>Total Jobs:</Label>
          <Value>{queue.totalJobs}</Value>
        </Stat>
        {queue.summary.error > 0 && (
          <Stat className="error">
            <Label>Errors:</Label>
            <Value>{queue.summary.error}</Value>
          </Stat>
        )}
        {queue.summary.stuckCount > 0 && (
          <Stat className="warning">
            <Label>Stuck:</Label>
            <Value>{queue.summary.stuckCount}</Value>
          </Stat>
        )}
      </Summary>
      
      <JobList>
        {queue.jobs.map(job => (
          <JobItem key={job.guid} className={job.isError ? 'error' : job.isStuck ? 'stuck' : ''}>
            <JobInfo>
              <JobId>{job.guid.substring(0, 8)}...</JobId>
              <Printer>{job.printerName}</Printer>
              <Status>{job.status}</Status>
              <Age>{job.ageFormatted}</Age>
            </JobInfo>
            {job.errorMessage && (
              <ErrorMessage>{job.errorMessage}</ErrorMessage>
            )}
            <Actions>
              {job.isError && (
                <Button onClick={() => retryJob(job.guid)}>Retry</Button>
              )}
              <Button onClick={() => cancelJob(job.spoolerId)}>Cancel</Button>
            </Actions>
          </JobItem>
        ))}
      </JobList>
    </QueueMonitorPanel>
  );
};
```

---

## 📦 Required Refactoring

### 1. Update PrinterTask Entity

Add fields for Tray App integration:

```typescript
// src/entities/printerTaskEntities.ts
export interface PrinterTask extends BasePrinterTask, SiteObject {
  // Existing fields...
  
  // New fields for Tray App
  windowsSpoolerId?: number;      // Windows spooler job ID
  trayAppGuid?: string;           // GUID used in Tray App
  lastStatusCheck?: Date;         // Last status check time
  queuePosition?: number;         // Position in print queue
}
```

### 2. Update PrinterSettings Entity

Add status tracking fields:

```typescript
// src/entities/printerSettingsEntities.ts
export interface PrinterSettings extends SiteObject {
  // Existing fields...
  
  // New status fields
  isOnline?: boolean;
  status?: string;
  lastStatusCheck?: Date;
  hasIssues?: boolean;
  jobCount?: number;
  port?: string;
  portType?: string;
  supportsRaw?: boolean;
}
```

### 3. Add Configuration

Create settings for Tray App:

```typescript
// src/config/printerConfig.ts
export const PRINTER_CONFIG = {
  trayApp: {
    enabled: true,
    url: 'http://127.0.0.1:9877',
    path: 'C:\\Program Files\\PrinterTrayApp\\PrinterTrayApp.exe',
    autoLaunch: true,
    healthCheckInterval: 60000,  // 1 minute
    queueCheckInterval: 20000,   // 20 seconds
    stuckThreshold: 30000,       // 30 seconds
  },
  fallback: {
    useNativeModule: true,       // Use printers.node if Tray App unavailable
    maxRetries: 3,
  }
};
```

---

## 🚀 Implementation Steps

### Week 1: Foundation
1. [ ] Create `PrinterTrayAppService.ts`
2. [ ] Add configuration settings
3. [ ] Update entity types
4. [ ] Create health check mechanism

### Week 2: Core Integration
1. [ ] Modify `PrinterContext.tsx` to use Tray App
2. [ ] Implement fallback to native module
3. [ ] Add queue monitoring logic
4. [ ] Update print task submission

### Week 3: UI Enhancements
1. [ ] Update `PrintersContainer.tsx` with status
2. [ ] Create `PrintQueueMonitor` component
3. [ ] Add printer status indicators
4. [ ] Implement refresh functionality

### Week 4: Testing & Polish
1. [ ] Test with Tray App running
2. [ ] Test fallback when Tray App down
3. [ ] Test queue monitoring
4. [ ] Handle edge cases

---

## 🧪 Testing Strategy

### Test Scenarios

1. **Tray App Available**
   - Print submission via HTTP
   - Queue monitoring
   - Error detection
   - Printer discovery

2. **Tray App Unavailable**
   - Fallback to native module
   - Auto-launch attempt
   - Graceful degradation

3. **Mixed Environment**
   - Some printers via Tray App
   - Some via native module
   - Status synchronization

4. **Error Conditions**
   - Paper out
   - Printer offline
   - Stuck jobs
   - Network failures

---

## 📊 Migration Path

### Phase 1: Parallel Operation (Current + Tray App)
- Both systems run simultaneously
- Tray App used when available
- Native module as fallback

### Phase 2: Tray App Primary
- Tray App becomes default
- Native module only for specific cases
- Full queue monitoring

### Phase 3: Tray App Only (Future)
- Remove native module dependency
- All printing via HTTP API
- Complete feature parity

---

## 🎯 Benefits After Integration

1. **Better Error Handling**
   - Real-time queue status
   - Stuck job detection
   - Automatic error recovery

2. **Enhanced Monitoring**
   - Printer online/offline status
   - Job queue visibility
   - Performance metrics

3. **Improved Reliability**
   - Duplicate prevention via GUID
   - Windows spooler integration
   - Retry logic

4. **Better User Experience**
   - Visual queue status
   - Printer status indicators
   - Proactive error alerts

5. **Simplified Architecture**
   - HTTP API vs native module
   - Centralized printing service
   - Easier debugging

---

## 📝 Configuration Changes Needed

### package.json
```json
{
  "dependencies": {
    // Keep existing for backward compatibility
    "node-printer": "existing-version"
  },
  "config": {
    "printerTrayApp": {
      "enabled": true,
      "url": "http://127.0.0.1:9877"
    }
  }
}
```

### Environment Variables
```env
PRINTER_TRAY_APP_ENABLED=true
PRINTER_TRAY_APP_URL=http://127.0.0.1:9877
PRINTER_TRAY_APP_PATH=C:\Program Files\PrinterTrayApp\PrinterTrayApp.exe
PRINTER_TRAY_APP_AUTO_LAUNCH=true
```

---

## 🚨 Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Tray App not installed | Fallback to native module |
| Tray App crashes | Auto-restart + fallback |
| Network issues | Local HTTP only (127.0.0.1) |
| Performance impact | Async operations + caching |
| Breaking changes | Feature flag for rollback |

---

## 📚 References

- [Windows Printer Tray App API](./FINAL-POS-PRINTER-INTEGRATION-FLOW.md)
- [Current POS Architecture](C:\Users\johnk\repo\dnero-desktop-pos)
- [PrinterTask Model](../src/entities/printerTaskEntities.ts)

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Status**: Implementation Ready