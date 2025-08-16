# 📋 Milestone 2 Implementation Plan: Printer Discovery

## Approach: Using Windows Spooler API (winspool.drv)

Since we'll be sending raw ESC/POS bytes to printers, we need to use the Windows Spooler API directly via P/Invoke. This is the same approach used for RAW printing.

## Part 1: Basic Printer Discovery

### Step 1: Create Windows API Interop
```csharp
// WinSpoolInterop.cs
- EnumPrinters() - Get list of installed printers
- OpenPrinter() - Open printer handle (for later use)
- ClosePrinter() - Close printer handle
- GetPrinter() - Get printer info/status
```

### Step 2: Create PrinterService
```csharp
// PrinterService.cs
public class PrinterService
{
    - GetInstalledPrinters() - Returns list of printer names
    - GetPrinterStatus(name) - Check if online/offline
    - RefreshPrinters() - Update cached list
}
```

### Step 3: Wire into Application
- Initialize PrinterService on startup
- Log discovered printers to console
- Update /health endpoint with printer list

## Part 2: Printer Mapping

### Step 1: Configuration Model
```csharp
// Models/PrinterConfiguration.cs
public class PrinterMapping
{
    public string LogicalName { get; set; }
    public string WindowsPrinterName { get; set; }
    public bool IsDefault { get; set; }
}
```

### Step 2: Config File Support
- Load printers.json if exists
- Default: logical name = Windows name
- Watch file for changes (optional)

### Step 3: Mapping Resolution
```csharp
// PrinterService.cs additions
- ResolveLogicalName(logical) -> Windows name
- GetPrinterByLogicalName(logical) -> PrinterInfo
- ValidateMappings() - Check all mapped printers exist
```

## Part 3: API Endpoints

### New Endpoint: GET /printers
```json
Response:
{
  "printers": [
    {
      "logicalName": "kitchen",
      "windowsPrinterName": "EPSON TM-T88V",
      "isOnline": true,
      "isDefault": false
    }
  ]
}
```

### Updated: GET /health
```json
{
  "ok": true,
  "version": "0.1.0",
  "printers": ["EPSON TM-T88V", "Star TSP100"],
  "uptimeSeconds": 120
}
```

## Testing After Each Part

### Part 1 Test:
```powershell
# Should show Windows printer names
Invoke-WebRequest -Uri http://127.0.0.1:9877/health
```

### Part 2 Test:
1. Create printers.json with mappings
2. Restart app
3. Check console shows "Loaded X printer mappings"

### Part 3 Test:
```powershell
# Should show logical + Windows names
Invoke-WebRequest -Uri http://127.0.0.1:9877/printers
```

## Why This Approach?

1. **Direct Windows API** - Same method we'll use for RAW printing
2. **No unnecessary dependencies** - No System.Drawing needed
3. **Lightweight** - Minimal overhead
4. **ESC/POS ready** - Sets up for sending byte arrays in Milestone 5

Ready to implement Part 1?