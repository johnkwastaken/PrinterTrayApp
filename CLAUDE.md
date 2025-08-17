# 🖨️ Windows Printer Tray App – Development Guide for Claude

## Project Overview
Windows Tray application that accepts print jobs from a POS system via REST API and sends them to thermal printers using ESC/POS commands.

## 🚀 Current Status: **PRODUCTION READY**

### ✅ Completed Features
- Windows tray application with HTTP server
- Print job processing matching POS system format
- ESC/POS command generation (string-based)
- XML template rendering with token replacement
- Direct Windows printer spooler integration
- Retry logic with cash drawer safety (max 3 attempts)
- Human-readable job numbers (PRT-YYYYMMDD-######)
- Interactive test form for JSON input
- Printer discovery and status monitoring

## 📁 Project Structure
```
PrinterTrayApp/
├── 📄 Core Files
│   ├── Program.cs                 # Entry point with mutex
│   ├── TrayApplicationContext.cs  # System tray management
│   ├── HttpServer.cs             # Kestrel HTTP server & endpoints
│   ├── ConsoleWindow.cs          # Console output management
│   └── Constants.cs              # Application constants
│
├── 📂 Models/
│   ├── PrinterTask.cs            # POS print job model
│   ├── PrinterInfo.cs            # Printer information
│   ├── PortType.cs               # Port type detection
│   └── HealthResponse.cs         # API response models
│
├── 📂 Services/
│   ├── PrinterService.cs         # Printer discovery & management
│   ├── POS80Commands.cs          # ESC/POS command definitions
│   ├── TemplateHelpers.cs        # XML template rendering
│   ├── CommandBuilder.cs         # ESC/POS command generation
│   ├── PrintDirect.cs            # Windows spooler wrapper
│   └── SimplePrintService.cs     # Basic print testing
│
├── 📂 Forms/
│   ├── PrintTestForm.cs          # JSON input test interface
│   ├── PrinterManagementForm.cs  # Printer management UI
│   └── MemoryStatusForm.cs       # Memory diagnostics
│
├── 📂 Tests/                      # Test scripts and data
│   ├── TestPrintEndpoint.ps1
│   ├── TestPrintInteractive.ps1
│   └── test-scenarios.json
│
├── 📂 Documentation/              # Project documentation
│   ├── MILESTONE-*.md
│   ├── POS-ARCHITECTURE-ANALYSIS.md
│   └── TESTING-COMPLETE.md
│
└── 📄 Configuration
    ├── printers.json             # Printer mappings (optional)
    └── PrinterTrayApp.csproj     # Project configuration
```

## 🔌 API Endpoints

### `GET /health`
Returns server status and available printers
```json
{
  "ok": true,
  "version": "0.1.0",
  "printers": ["passkitchen", "Microsoft Print to PDF"],
  "uptimeSeconds": 120
}
```

### `GET /printers`
Returns detailed printer information
```json
{
  "printers": [{
    "logicalName": "passkitchen",
    "windowsPrinterName": "passkitchen",
    "compositeId": "passkitchen@192.168.86.80",
    "status": "Ready",
    "isOnline": true,
    "port": "192.168.86.80",
    "portType": "NetworkIP",
    "supportsRaw": true
  }]
}
```

### `POST /print`
Accepts PrinterTask JSON from POS system

⚠️ **CRITICAL: Following POS Architecture**
The app uses ONLY these fields from PrinterTask:
- `template.body` - XML template string
- `templateData` - JSON string containing ALL data (including printer name)
- `isOpenCashDrawer` - Boolean for cash drawer
- `_id.id` - Only for logging

**ALL OTHER FIELDS ARE IGNORED!** Root-level fields like `printerDeviceName` are deprecated.

```json
{
  "_id": { "id": "test-001", "siteId": "site-001" },
  "template": {
    "body": "<root>XML template with {{tokens}}</root>",
    "name": "Receipt",
    "templateType": "Docket"
  },
  "templateData": "{\"printerDeviceName\":\"passkitchen\",\"sites\":{...},\"orders\":{...}}",
  "isOpenCashDrawer": false
}
```

## 🎯 PrinterTask Model (POS Format)

### Fields Actually Used:
```csharp
public class PrinterTask {
    // USED FIELDS:
    public ReceiptTemplate template { get; set; }  // Only .body is used
    public string templateData { get; set; }       // Contains EVERYTHING
    public bool isOpenCashDrawer { get; set; }     // Cash drawer control
    
    // IGNORED FIELDS (for compatibility only):
    public ObjectId _id { get; set; }              // Only .id for logging
    public string printerDeviceName { get; set; }  // DEPRECATED - use templateData
    public string printerName { get; set; }        // DEPRECATED - use templateData
    // ... all other fields are ignored
}
```

### templateData Structure:
ALL data must be in templateData as JSON:
```json
{
  // REQUIRED: Printer specification
  "printerDeviceName": "passkitchen",  // or "printerName"
  
  // Data collections (lowercase keys!)
  "sites": {...},      // Store information
  "orders": {          // Order with products
    ...orderData,
    "mainProducts": [...]
  },
  "staff": {...},      // Staff member
  "registers": {...},  // Terminal
  "currencies": {...}, // Currency format
  "taxes": [...],      // Tax breakdown
  
  // Print metadata
  "dateOfPrinting": "17/01/2025, 2:30 PM",
  "header": "RECEIPT",
  "footer": "Thank you",
  
  // Alternative product locations
  "printerTaskProduct": [...],  // Alt products
  "products": [...]              // Test products
}
```

## 📝 Template System

### XML Template Structure
```xml
<root charset="utf-8">
    <text align="center" size="wide">{{header}}</text>
    <separator char="-" />
    <text>Date: {{dateOfPrinting}}</text>
    <text>Item: {{item.name}} - {{item.price}}</text>
    <blank lines="2" />
    <command cmd="cut" />
</root>
```

### Supported Elements
- `<text>` - Text with alignment, size, font options
- `<separator>` - Line separator with custom character
- `<blank>` - Empty lines
- `<table>` - Table layout with columns
- `<command>` - Printer commands (cut, opencashdrawer, beep)
- `<receipt-section>` - Special receipt formatting
- `<barcode>`, `<qrcode>` - Barcode generation

### Token Replacement
- Format: `{{path.to.value}}`
- Special tokens: `{{dateOfPrinting}}`, `{{currencyId}}`
- Supports nested JSON paths
- Empty tokens skip the entire element

## 🖨️ ESC/POS Commands
String-based commands (not bytes) matching POS implementation:
- Text formatting (size, alignment, bold)
- Paper operations (cut, feed)
- Cash drawer control
- Barcode/QR code printing

## 🧪 Testing

### Using the Test Form
1. Right-click tray icon → "🧪 Test Print (JSON)"
2. Paste PrinterTask JSON
3. Select printer from dropdown
4. Click "Send to Printer"
5. View response

### Command Line Testing
```powershell
# Test print endpoint
curl -X POST http://127.0.0.1:9877/print -H "Content-Type: application/json" -d @test.json

# Check printer status
curl http://127.0.0.1:9877/printers
```

## ⚙️ Configuration

### printers.json (Optional)
```json
{
  "mappings": [{
    "logicalName": "kitchen",
    "windowsPrinterName": "passkitchen"
  }],
  "fallbackPrinter": "passkitchen"
}
```

## 🔧 Build & Deploy

### Development
```bash
dotnet build
dotnet run
```

### Production Build
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## 🚦 Safety Features
- **Cash Drawer Safety**: Max 3 retry attempts for cash drawer operations
- **Job Tracking**: Human-readable job numbers for audit trail
- **Error Handling**: Detailed error responses with retry counts
- **Memory Monitoring**: Built-in memory leak detection

## 📋 Development Rules

### 1. Code Quality
- NO COMMENTS in code unless explicitly requested
- Follow existing patterns and conventions
- Check for existing libraries before adding new ones
- Never expose or log secrets/keys

### 2. Testing Requirements
- Test each endpoint after implementation
- Verify with actual thermal printer
- Run lint/typecheck if available
- Test edge cases and error conditions

### 3. Incremental Development
- Build features incrementally
- Each part must be independently testable
- Provide clear test instructions

## 🎯 Next Steps (When Ready)
1. Implement printer mapping by ID/location
2. Add job queue persistence
3. Implement job status tracking endpoint
4. Add printer-specific configurations
5. Create Windows installer

## 📝 Important Notes
- App runs on fixed port `127.0.0.1:9877`
- Single instance enforced via mutex
- Supports RAW printing for thermal printers
- Console window toggleable from tray menu
- All responses are JSON formatted

## 🧭 Quick Commands
```powershell
# Run the app
cd C:\Users\johnk\repo\printing\PrinterTrayApp
dotnet run

# Test print
curl -X POST http://127.0.0.1:9877/print -H "Content-Type: application/json" -d "{...}"

# Check health
curl http://127.0.0.1:9877/health
```

---
*Last Updated: 2025-01-16*
*Status: Production Ready - Awaiting printer mapping requirements*