# 🖨️ POS Settings - Printer Discovery Changes
**Simple changes to use Tray App for printer discovery**

## 📋 Current Flow (No Change)
1. User goes to Settings → Printers
2. Clicks "Refresh" to discover printers
3. Sees list of available printers
4. Clicks "Add" on a printer to add it to PrinterSettings

## 🔄 What Changes: Just the Discovery Source

### On Page Load (useEffect)
```typescript
// OLD: Calls getPrinters() on mount
useEffect(() => {
  const discovered = getPrinters();
  setAddPrinters(discovered);
}, []);

// NEW: Call Tray App API on mount
useEffect(() => {
  const loadPrinters = async () => {
    try {
      const response = await fetch('http://127.0.0.1:9877/printers');
      const data = await response.json();
      
      const discoveredPrinters = data.printers.map(p => ({
        name: p.name,
        description: p.displayName || p.name,
        isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
        isOnline: p.isOnline,
        status: p.status
      }));
      
      setAddPrinters(discoveredPrinters);
    } catch (error) {
      // Fallback or empty list
      setAddPrinters([]);
    }
  };
  
  loadPrinters();
}, []);
```

### OLD: Native Module Discovery
```typescript
// src/modules/settings/containers/PrintersContainer.tsx
import { getPrinters } from "../../../printers/printers";

const refreshPrinters = async () => {
  // OLD: Get from native module
  const discoveredPrinters = getPrinters();
  setAddPrinters(discoveredPrinters);
};
```

### NEW: Tray App Discovery
```typescript
// src/modules/settings/containers/PrintersContainer.tsx

const refreshPrinters = async () => {
  try {
    // NEW: Get from Tray App API
    const response = await fetch('http://127.0.0.1:9877/printers');
    const data = await response.json();
    
    // Map to existing format
    const discoveredPrinters = data.printers.map(p => ({
      name: p.name,                           // Windows printer name
      description: p.displayName || p.name,   // Display name
      isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
      
      // NEW: Extra data available (optional to use)
      isOnline: p.isOnline,
      status: p.status,
      port: p.port,
      driver: p.driver,
      jobCount: p.jobCount
    }));
    
    setAddPrinters(discoveredPrinters);
    
    // Optional: Show printer status
    if (data.hasPrinterIssues) {
      showNotification({
        type: 'warning',
        message: 'Some printers have issues'
      });
    }
    
  } catch (error) {
    console.error('Failed to discover printers:', error);
    
    // Optional: Fallback to native if Tray App not available
    try {
      const nativePrinters = getPrinters();
      setAddPrinters(nativePrinters);
    } catch {
      showNotification({
        type: 'error',
        message: 'Could not discover printers'
      });
    }
  }
};
```

## 🎨 Enhanced Add Printer Modal (Optional)

Show more information when adding a printer:

```typescript
// src/modules/settings/components/AddPrinterModal.tsx

const AddPrinterModal = ({ printer, onAdd }) => {
  return (
    <Modal>
      <h2>Add Printer</h2>
      
      <PrinterInfo>
        <Row>
          <Label>Name:</Label>
          <Value>{printer.name}</Value>
        </Row>
        
        <Row>
          <Label>Description:</Label>
          <Value>{printer.description}</Value>
        </Row>
        
        {/* NEW: Show status if available */}
        {printer.status && (
          <Row>
            <Label>Status:</Label>
            <Value className={printer.isOnline ? 'online' : 'offline'}>
              {printer.status}
            </Value>
          </Row>
        )}
        
        <Row>
          <Label>Type:</Label>
          <Value>{printer.isNetwork ? 'Network' : 'Local'}</Value>
        </Row>
        
        {/* NEW: Show port if available */}
        {printer.port && (
          <Row>
            <Label>Port:</Label>
            <Value>{printer.port}</Value>
          </Row>
        )}
      </PrinterInfo>
      
      {/* Optional: Warn if printer is offline */}
      {printer.isOnline === false && (
        <Warning>
          ⚠️ This printer appears to be offline
        </Warning>
      )}
      
      <Actions>
        <Button onClick={() => onAdd(printer)}>Add Printer</Button>
        <Button onClick={onCancel}>Cancel</Button>
      </Actions>
    </Modal>
  );
};
```

## 📊 Complete PrintersContainer Changes

```typescript
// src/modules/settings/containers/PrintersContainer.tsx

const PrintersContainer = () => {
  const [addPrinters, setAddPrinters] = useState<AddPrinter[] | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  
  // Refresh printers from Tray App
  const refreshPrinters = useCallback(async () => {
    setRefreshing(true);
    
    try {
      // Call Tray App API
      const response = await fetch('http://127.0.0.1:9877/printers');
      
      if (!response.ok) {
        throw new Error('Tray App not available');
      }
      
      const data = await response.json();
      
      // Map to expected format
      const discoveredPrinters = data.printers.map(p => ({
        name: p.name,
        description: p.displayName || p.comment || p.name,
        isNetwork: p.portType === 'NetworkIP' || p.portType === 'Network',
        // Extra fields for display
        isOnline: p.isOnline,
        status: p.status,
        port: p.port
      }));
      
      // Filter out already added printers
      const existingNames = printers.map(p => p.deviceName);
      const newPrinters = discoveredPrinters.filter(
        p => !existingNames.includes(p.name)
      );
      
      setAddPrinters(newPrinters);
      
      if (newPrinters.length === 0) {
        showNotification({
          type: 'info',
          message: 'No new printers found'
        });
      } else {
        showNotification({
          type: 'success',
          message: `Found ${newPrinters.length} printer(s)`
        });
      }
      
    } catch (error) {
      console.error('Failed to refresh printers:', error);
      
      // Optional: Try native module as fallback
      try {
        const nativePrinters = getPrinters();
        setAddPrinters(nativePrinters);
      } catch {
        setAddPrinters([]);
        showNotification({
          type: 'error',
          message: 'Could not discover printers. Is Printer Tray App running?'
        });
      }
    } finally {
      setRefreshing(false);
    }
  }, [printers]);
  
  // Add printer to PrinterSettings
  const handleAddPrinter = useCallback(async (printer: AddPrinter) => {
    const newPrinter: PrinterSettings = {
      ...newSiteObject(),
      deviceName: printer.name,
      customName: printer.description,
      active: true,
      on: true,
      isNetwork: printer.isNetwork,
      // Store extra info if available
      port: printer.port || '',
      isOnline: printer.isOnline !== undefined ? printer.isOnline : true,
      status: printer.status || 'Unknown'
    };
    
    await printerOps.insert(newPrinter);
    
    // Remove from available list
    setAddPrinters(prev => prev?.filter(p => p.name !== printer.name) || []);
    
    showNotification({
      type: 'success',
      message: `Added ${printer.description}`
    });
  }, [printerOps]);
  
  return (
    <Container>
      {/* Existing printer list */}
      <DataView
        data={printers}
        columns={printersCols}
      />
      
      {/* Discovery section */}
      <DiscoverySection>
        <Button 
          onClick={refreshPrinters}
          loading={refreshing}
          icon={<FontAwesomeIcon icon={faSync} />}
        >
          Refresh Printers
        </Button>
        
        {/* Show discovered printers */}
        {addPrinters && addPrinters.length > 0 && (
          <AvailablePrinters>
            <h3>Available Printers</h3>
            {addPrinters.map(printer => (
              <PrinterCard key={printer.name}>
                <PrinterName>{printer.description}</PrinterName>
                <PrinterDetails>
                  {printer.isNetwork ? 'Network' : 'Local'}
                  {printer.status && ` • ${printer.status}`}
                </PrinterDetails>
                <Button 
                  onClick={() => handleAddPrinter(printer)}
                  size="small"
                >
                  Add
                </Button>
              </PrinterCard>
            ))}
          </AvailablePrinters>
        )}
      </DiscoverySection>
    </Container>
  );
};
```

---

## 🎯 Summary of Changes

### What Changes:
1. **refreshPrinters()** - Calls `/printers` API instead of `getPrinters()`
2. **Data mapping** - Map Tray App response to existing format
3. **Optional**: Show printer status (online/offline)
4. **Optional**: Fallback to native if Tray App not available

### What Stays the Same:
- ✅ Manual add process
- ✅ PrinterSettings structure
- ✅ UI flow (refresh → see list → add)
- ✅ Printer configuration after adding

### Benefits:
- 🔍 Better printer information (status, port, driver)
- 🚨 Know if printer is offline before adding
- 📊 See job counts
- 🔄 Same user experience

---

**That's it!** Just change where the printer list comes from. Everything else stays exactly the same.