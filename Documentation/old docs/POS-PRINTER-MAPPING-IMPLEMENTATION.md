# 🗺️ POS Printer Mapping Implementation
**Complete flow for discovering, mapping, and managing printers in POS**

## 📋 Overview

The POS needs to:
1. **Discover** printers from Tray App
2. **Map** them to PrinterSettings in Ditto database
3. **Create** new PrinterSettings for new printers
4. **Update** status for existing printers
5. **Handle** printer selection for different print types

---

## 🔄 Complete Printer Mapping Flow

### 1. Printer Discovery & Sync Service

Create a new service to handle printer discovery and mapping:

```typescript
// src/services/PrinterMappingService.ts
import { Collection } from "@ditto/collections";
import { PrinterSettings } from "@entities/printerSettingsEntities";
import { newSiteObject } from "@entities/baseEntities";

export class PrinterMappingService {
  private trayAppUrl = 'http://127.0.0.1:9877';
  
  /**
   * Discover printers from Tray App and sync with Ditto
   */
  async discoverAndSyncPrinters(
    existingPrinters: PrinterSettings[],
    printerOps: any
  ): Promise<void> {
    try {
      // 1. Get printers from Tray App
      const response = await fetch(`${this.trayAppUrl}/printers`);
      const data = await response.json();
      
      // 2. Process each discovered printer
      for (const trayPrinter of data.printers) {
        await this.syncPrinter(trayPrinter, existingPrinters, printerOps);
      }
      
      // 3. Mark missing printers as offline
      await this.markMissingPrintersOffline(
        existingPrinters, 
        data.printers, 
        printerOps
      );
      
    } catch (error) {
      console.error('Failed to discover printers:', error);
      throw error;
    }
  }
  
  /**
   * Sync a single printer with Ditto
   */
  private async syncPrinter(
    trayPrinter: any,
    existingPrinters: PrinterSettings[],
    printerOps: any
  ): Promise<void> {
    // Find existing printer by device name
    const existing = existingPrinters.find(
      p => p.deviceName === trayPrinter.name
    );
    
    if (existing) {
      // UPDATE existing printer with latest status
      await printerOps.updateById(existing._id, {
        // Status fields
        isOnline: trayPrinter.isOnline,
        status: trayPrinter.status,
        statusFlags: trayPrinter.statusFlags,
        hasIssues: !trayPrinter.isOnline || trayPrinter.status !== 'Ready',
        jobCount: trayPrinter.jobCount,
        
        // Connection info
        port: trayPrinter.port,
        portType: trayPrinter.portType,
        isNetwork: trayPrinter.portType === 'NetworkIP' || 
                  trayPrinter.portType === 'Network',
        
        // Capabilities
        supportsRaw: trayPrinter.supportsRaw,
        supportedPaperSizes: trayPrinter.supportedPaperSizes,
        
        // Metadata
        driver: trayPrinter.driver,
        location: trayPrinter.location || existing.location,
        comment: trayPrinter.comment || existing.comment,
        lastSeenAt: new Date(),
        lastStatusCheck: new Date()
      });
      
    } else {
      // CREATE new printer
      await this.createNewPrinter(trayPrinter, printerOps);
    }
  }
  
  /**
   * Create a new PrinterSettings entry
   */
  private async createNewPrinter(
    trayPrinter: any,
    printerOps: any
  ): Promise<void> {
    const newPrinter: PrinterSettings = {
      ...newSiteObject(),
      
      // Identity
      deviceName: trayPrinter.name,
      customName: trayPrinter.displayName || trayPrinter.name,
      
      // Configuration
      active: false,  // Start as inactive until configured
      on: true,       // Enabled by default
      isDefault: trayPrinter.isDefault,
      
      // Status
      isOnline: trayPrinter.isOnline,
      status: trayPrinter.status,
      statusFlags: trayPrinter.statusFlags,
      hasIssues: !trayPrinter.isOnline || trayPrinter.status !== 'Ready',
      jobCount: trayPrinter.jobCount,
      
      // Connection
      port: trayPrinter.port,
      portType: trayPrinter.portType,
      isNetwork: trayPrinter.portType === 'NetworkIP' || 
                trayPrinter.portType === 'Network',
      ipAddress: this.extractIpAddress(trayPrinter.port),
      
      // Capabilities
      supportsRaw: trayPrinter.supportsRaw,
      supportedPaperSizes: trayPrinter.supportedPaperSizes,
      
      // Metadata
      driver: trayPrinter.driver,
      location: trayPrinter.location || '',
      comment: trayPrinter.comment || '',
      
      // Printer role configuration (to be set by user)
      printerType: null,  // 'receipt', 'kitchen', 'bar', etc.
      assignedRegisters: [],
      assignedStations: [],
      
      // Timestamps
      discoveredAt: new Date(),
      lastSeenAt: new Date(),
      lastStatusCheck: new Date()
    };
    
    await printerOps.insert(newPrinter);
    console.log(`Created new printer: ${trayPrinter.name}`);
  }
  
  /**
   * Mark printers not found in discovery as offline
   */
  private async markMissingPrintersOffline(
    existingPrinters: PrinterSettings[],
    discoveredPrinters: any[],
    printerOps: any
  ): Promise<void> {
    for (const existing of existingPrinters) {
      const stillExists = discoveredPrinters.find(
        p => p.name === existing.deviceName
      );
      
      if (!stillExists && existing.isOnline !== false) {
        await printerOps.updateById(existing._id, {
          isOnline: false,
          status: 'Not Found',
          hasIssues: true,
          lastSeenAt: existing.lastSeenAt, // Keep last seen time
          lastStatusCheck: new Date()
        });
        console.warn(`Printer ${existing.deviceName} not found in discovery`);
      }
    }
  }
  
  /**
   * Extract IP address from port string
   */
  private extractIpAddress(port: string): string | null {
    if (!port) return null;
    
    // Match IP address pattern
    const ipMatch = port.match(/\d+\.\d+\.\d+\.\d+/);
    return ipMatch ? ipMatch[0] : null;
  }
  
  /**
   * Get printer for specific purpose
   */
  async getPrinterForTask(
    printers: PrinterSettings[],
    taskType: 'receipt' | 'kitchen' | 'bar' | 'report',
    registerId?: string
  ): Promise<PrinterSettings | null> {
    // Filter active and online printers
    const availablePrinters = printers.filter(
      p => p.active && p.on && p.isOnline && !p.hasIssues
    );
    
    // Find printer by type and register assignment
    let printer = availablePrinters.find(p => {
      if (p.printerType !== taskType) return false;
      if (registerId && p.assignedRegisters?.length > 0) {
        return p.assignedRegisters.includes(registerId);
      }
      return true;
    });
    
    // Fallback to any printer of the right type
    if (!printer) {
      printer = availablePrinters.find(p => p.printerType === taskType);
    }
    
    // Fallback to default printer
    if (!printer) {
      printer = availablePrinters.find(p => p.isDefault);
    }
    
    return printer || null;
  }
}

export const printerMapping = new PrinterMappingService();
```

---

## 🔧 Updated PrintersContainer.tsx

Update the UI to handle printer mapping:

```typescript
// src/modules/settings/containers/PrintersContainer.tsx
import { printerMapping } from '../../../services/PrinterMappingService';

const PrintersContainer = () => {
  const [isDiscovering, setIsDiscovering] = useState(false);
  const [lastDiscovery, setLastDiscovery] = useState<Date | null>(null);
  const [printerStatuses, setPrinterStatuses] = useState<Map<string, any>>(new Map());
  
  // Existing Ditto hooks
  const printers = useFindMany<PrinterSettings>(Collection.PrinterSettings);
  const printerOps = useOps<PrinterSettings>(Collection.PrinterSettings);
  
  /**
   * Discover and sync printers
   */
  const discoverPrinters = useCallback(async () => {
    setIsDiscovering(true);
    
    try {
      // Run discovery and sync
      await printerMapping.discoverAndSyncPrinters(printers, printerOps);
      setLastDiscovery(new Date());
      
      // Show success
      showNotification({
        type: 'success',
        message: 'Printers refreshed successfully'
      });
    } catch (error) {
      showNotification({
        type: 'error',
        message: 'Failed to discover printers'
      });
    } finally {
      setIsDiscovering(false);
    }
  }, [printers, printerOps]);
  
  /**
   * Auto-discover on mount
   */
  useEffect(() => {
    discoverPrinters();
    
    // Refresh every 5 minutes
    const interval = setInterval(discoverPrinters, 5 * 60 * 1000);
    return () => clearInterval(interval);
  }, []);
  
  /**
   * Configure printer mapping (assign roles)
   */
  const configurePrinterMapping = useCallback(async (
    printer: PrinterSettings,
    config: {
      printerType?: string;
      assignedRegisters?: string[];
      assignedStations?: string[];
      isDefault?: boolean;
    }
  ) => {
    await printerOps.updateById(printer._id, {
      printerType: config.printerType,
      assignedRegisters: config.assignedRegisters,
      assignedStations: config.assignedStations,
      isDefault: config.isDefault,
      active: true  // Activate when configured
    });
    
    showNotification({
      type: 'success',
      message: `Printer ${printer.customName} configured`
    });
  }, [printerOps]);
  
  // Enhanced columns with mapping info
  const printersCols: ColDef<PrinterSettings> = useMemo(
    () => [
      // Name
      textCol({
        title: "Name",
        val: (x) => x.customName,
        editable: true,
        onEdit: async (printer, newName) => {
          await printerOps.updateById(printer._id, {
            customName: newName
          });
        }
      }),
      
      // Device Name (from Windows)
      textCol({
        title: "Device",
        val: (x) => x.deviceName,
        color: (x) => x.isOnline ? null : 'gray'
      }),
      
      // Status with color coding
      textCol({
        title: "Status",
        val: (x) => {
          if (!x.isOnline) return "Offline";
          if (x.hasIssues) return x.status || "Error";
          return x.status || "Ready";
        },
        color: (x) => {
          if (!x.isOnline) return 'red';
          if (x.hasIssues) return 'orange';
          return 'green';
        }
      }),
      
      // Printer Type/Role
      selectCol({
        title: "Type",
        val: (x) => x.printerType || 'Not Set',
        options: [
          { value: null, label: 'Not Set' },
          { value: 'receipt', label: 'Receipt' },
          { value: 'kitchen', label: 'Kitchen' },
          { value: 'bar', label: 'Bar' },
          { value: 'report', label: 'Reports' },
          { value: 'label', label: 'Labels' }
        ],
        onEdit: async (printer, type) => {
          await configurePrinterMapping(printer, { printerType: type });
        }
      }),
      
      // Connection Type
      textCol({
        title: "Connection",
        val: (x) => {
          if (x.portType === 'NetworkIP') return `Network (${x.ipAddress || x.port})`;
          if (x.portType === 'USB') return 'USB';
          if (x.portType === 'Virtual') return 'Virtual';
          return x.portType || 'Unknown';
        }
      }),
      
      // Job Count
      textCol({
        title: "Jobs",
        val: (x) => x.jobCount?.toString() || '0',
        align: 'center'
      }),
      
      // Active toggle
      checkBoxCol({
        title: "Active",
        val: (x) => x.active,
        onChange: async (printer, active) => {
          await printerOps.updateById(printer._id, { active });
        },
        align: "center"
      }),
      
      // Default printer
      radioCol({
        title: "Default",
        val: (x) => x.isDefault,
        onChange: async (printer) => {
          // Clear other defaults
          for (const p of printers) {
            if (p.isDefault && p._id.id !== printer._id.id) {
              await printerOps.updateById(p._id, { isDefault: false });
            }
          }
          // Set this as default
          await printerOps.updateById(printer._id, { isDefault: true });
        },
        align: "center"
      }),
      
      // Actions
      actionsCol({
        title: "Actions",
        val: () => undefined,
        options: {
          actions: [
            {
              title: "Configure",
              icon: <FontAwesomeIcon icon={faCog} />,
              onClick: (printer) => {
                setSelectedPrinter(printer);
                setShowConfigModal(true);
              }
            },
            {
              title: "Test Print",
              icon: <FontAwesomeIcon icon={faPrint} />,
              onClick: async (printer) => {
                if (!printer.isOnline) {
                  showNotification({
                    type: 'error',
                    message: 'Printer is offline'
                  });
                  return;
                }
                
                const task = await makeTask(register, printer);
                await printerTaskOps.insert(task);
              },
              disabled: (printer) => !printer.isOnline
            },
            {
              title: "View Queue",
              icon: <FontAwesomeIcon icon={faList} />,
              onClick: async (printer) => {
                const response = await fetch('http://127.0.0.1:9877/check-queue');
                const queue = await response.json();
                const jobs = queue.jobs.filter(j => j.printerName === printer.deviceName);
                
                showModal({
                  title: `Queue for ${printer.customName}`,
                  content: <QueueList jobs={jobs} />
                });
              }
            }
          ]
        },
        align: "center"
      })
    ],
    [printers, printerOps]
  );
  
  return (
    <Container>
      {/* Discovery Status Bar */}
      <StatusBar>
        <StatusInfo>
          <span>Printers: {printers.length}</span>
          <span>Online: {printers.filter(p => p.isOnline).length}</span>
          <span>Issues: {printers.filter(p => p.hasIssues).length}</span>
          {lastDiscovery && (
            <span>Last Check: {formatTime(lastDiscovery)}</span>
          )}
        </StatusInfo>
        
        <Actions>
          <Button 
            onClick={discoverPrinters} 
            loading={isDiscovering}
            icon={<FontAwesomeIcon icon={faSync} />}
          >
            Discover Printers
          </Button>
          
          <Button
            onClick={() => setShowAddModal(true)}
            icon={<FontAwesomeIcon icon={faAdd} />}
            variant="primary"
          >
            Add Manual
          </Button>
        </Actions>
      </StatusBar>
      
      {/* Printer List */}
      <DataView
        data={printers}
        columns={printersCols}
        sortBy="customName"
        groupBy="printerType"
      />
      
      {/* Configuration Modal */}
      {showConfigModal && selectedPrinter && (
        <PrinterConfigModal
          printer={selectedPrinter}
          onSave={configurePrinterMapping}
          onClose={() => setShowConfigModal(false)}
        />
      )}
    </Container>
  );
};
```

---

## 🔄 Updated PrinterContext for Mapped Printers

```typescript
// src/contexts/PrinterContext.tsx
import { printerMapping } from '../services/PrinterMappingService';

export const PrinterContextProvider = ({ children }: PropsWithChildren<{}>) => {
  // ... existing code ...
  
  /**
   * Get the right printer for a print task
   */
  const getPrinterForTask = useCallback(async (
    printerTask: PrinterTask
  ): Promise<PrinterSettings | null> => {
    // First check if printer is specified in task
    if (printerTask.currentPrinterId) {
      const printer = printers.find(p => p._id.id === printerTask.currentPrinterId);
      if (printer?.isOnline && printer.active) {
        return printer;
      }
    }
    
    // Determine task type from template
    const taskType = getTaskType(printerTask.template?.templateType);
    
    // Get best printer for this task
    const printer = await printerMapping.getPrinterForTask(
      printers,
      taskType,
      registerId
    );
    
    if (!printer) {
      console.error(`No printer available for ${taskType} tasks`);
      return null;
    }
    
    return printer;
  }, [printers, registerId]);
  
  /**
   * Enhanced print function with printer mapping
   */
  const printTasks = useCallback(async () => {
    for (let printerTask of printerTasksToPrint) {
      if (alreadyPrinting.current[printerTask._id.id]) {
        continue;
      }
      
      // Get the right printer for this task
      const printer = await getPrinterForTask(printerTask);
      
      if (!printer) {
        await printerTaskOps.updateById(printerTask._id, {
          error: 'No printer available',
          inProgress: false
        });
        continue;
      }
      
      // Update task with selected printer
      printerTask.printerDeviceName = printer.deviceName;
      printerTask.printerName = printer.customName;
      
      alreadyPrinting.current[printerTask._id.id] = true;
      
      try {
        // Send to Tray App
        const response = await fetch('http://127.0.0.1:9877/print', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(printerTask)
        });
        
        const result = await response.json();
        
        if (result.success) {
          await printerTaskOps.updateById(printerTask._id, {
            inProgress: true,
            jobId: result.spoolerId.toString(),
            currentPrinterId: printer._id.id
          });
        } else {
          throw new Error(result.message);
        }
      } catch (e) {
        // Handle error...
      }
    }
  }, [printerTasksToPrint, getPrinterForTask]);
};
```

---

## 📊 PrinterSettings Entity Updates

```typescript
// src/entities/printerSettingsEntities.ts
export interface PrinterSettings extends SiteObject {
  // Identity
  deviceName: string;        // Windows printer name
  customName: string;        // User-friendly name
  
  // Configuration
  active: boolean;           // Enabled for use
  on: boolean;              // Power state
  isDefault: boolean;       // Default printer
  
  // Mapping
  printerType: 'receipt' | 'kitchen' | 'bar' | 'report' | 'label' | null;
  assignedRegisters: string[];  // Which registers can use this
  assignedStations: string[];   // Which stations can use this
  
  // Status (from Tray App)
  isOnline: boolean;
  status: string;
  statusFlags: number;
  hasIssues: boolean;
  jobCount: number;
  
  // Connection
  port: string;
  portType: 'NetworkIP' | 'Network' | 'USB' | 'Serial' | 'Virtual';
  isNetwork: boolean;
  ipAddress: string | null;
  
  // Capabilities
  supportsRaw: boolean;
  supportedPaperSizes: string[];
  driver: string;
  
  // Metadata
  location: string;
  comment: string;
  
  // Timestamps
  discoveredAt: Date;
  lastSeenAt: Date;
  lastStatusCheck: Date;
}
```

---

## 🚀 Implementation Steps

### 1. Create Printer Mapping Service
- Discovery and sync logic
- Printer selection by type
- Status updates

### 2. Update PrintersContainer UI
- Discovery button
- Status indicators
- Printer type assignment
- Register/station mapping

### 3. Modify PrinterContext
- Use mapped printers
- Select right printer for task
- Handle offline printers

### 4. Database Migration
- Add new fields to PrinterSettings
- Set default values for existing printers

---

## 🎯 Benefits

1. **Automatic Discovery** - Find all printers automatically
2. **Smart Mapping** - Assign printers to specific roles
3. **Status Tracking** - Know which printers are online/offline
4. **Flexible Assignment** - Different printers for different registers
5. **Fallback Support** - Use default printer if preferred unavailable

---

**Document Version**: 1.0  
**Created**: 2025-01-17  
**Purpose**: Complete printer mapping implementation for POS