# 🖨️ PrinterTrayApp - Windows Thermal Printer Service

A Windows system tray application that receives print jobs from POS systems via REST API and sends them to thermal printers using ESC/POS commands.

## 📋 Table of Contents
- [Overview](#overview)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Running the Application](#running-the-application)
- [API Documentation](#api-documentation)
- [Configuration](#configuration)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Development](#development)

## 🎯 Overview

PrinterTrayApp is a Windows service that:
- Runs in the system tray with a clean UI
- Accepts print jobs via HTTP REST API on port 9877
- Processes XML templates with token replacement
- Converts formatted content to ESC/POS commands
- Sends commands directly to thermal printers via Windows spooler
- Supports multiple printers and automatic discovery
- Provides real-time printer status monitoring

## 🏗️ Architecture

### System Architecture
```
┌─────────────────┐       HTTP POST        ┌──────────────────┐
│                 │ ───────────────────────>│                  │
│   POS System    │    PrinterTask JSON     │  PrinterTrayApp  │
│                 │<─────────────────────── │   (Port 9877)    │
└─────────────────┘      Job Status         └──────────────────┘
                                                      │
                                                      │ ESC/POS
                                                      │ Commands
                                                      ▼
                                             ┌──────────────────┐
                                             │ Thermal Printer  │
                                             │   (Network/USB)   │
                                             └──────────────────┘
```

### Component Architecture

```
PrinterTrayApp/
│
├── Core Components
│   ├── Program.cs                 # Application entry point
│   ├── TrayApplicationContext.cs  # System tray management
│   ├── HttpServer.cs             # Kestrel HTTP server
│   └── ConsoleWindow.cs          # Debug console
│
├── Services Layer
│   ├── TemplateHelpers.cs        # XML template processing
│   ├── CommandBuilder.cs         # ESC/POS command generation
│   ├── PrintDirect.cs            # Windows spooler interface
│   ├── PrinterService.cs         # Printer discovery & management
│   └── JsonCleaner.cs            # MongoDB type cleaning
│
├── Models
│   ├── PrinterTask.cs            # POS print job model
│   ├── PrinterInfo.cs            # Printer information
│   └── HealthResponse.cs         # API response models
│
└── Forms
    ├── PrintTestForm.cs          # JSON test interface
    └── PrinterManagementForm.cs  # Printer management UI
```

### Data Flow

1. **Receive Request** → POS system sends PrinterTask JSON to `/print` endpoint
2. **Extract Data** → Extract templateData JSON string from request
3. **Clean Data** → Remove MongoDB types ($date, $oid) from JSON
4. **Render Template** → Process XML template with token replacement
5. **Generate Commands** → Convert XML to ESC/POS command strings
6. **Send to Printer** → Use Windows spooler to send RAW commands
7. **Return Status** → Send job ID and status back to POS

## 📦 Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8.0 Runtime** or later
- **Thermal Printer** with Windows driver installed
- **Network Access** for network printers
- **Administrator Rights** for installation (optional)

## 🚀 Installation

### Option 1: Run from Source

1. **Clone the repository**
```bash
git clone https://github.com/yourusername/PrinterTrayApp.git
cd PrinterTrayApp
```

2. **Install .NET 8 SDK**
Download from: https://dotnet.microsoft.com/download/dotnet/8.0

3. **Restore dependencies**
```bash
dotnet restore
```

4. **Build the application**
```bash
dotnet build
```

### Option 2: Use Published Binary

1. **Build for deployment**
```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

2. **Find executable**
Navigate to: `bin\Release\net8.0-windows\win-x64\publish\PrinterTrayApp.exe`

## 🏃 Running the Application

### Development Mode

```bash
# Run from project directory
dotnet run

# Or with verbose output
dotnet run --console
```

### Production Mode

1. **Double-click** `PrinterTrayApp.exe` to start
2. **System tray icon** will appear (look for "PT" icon)
3. **Right-click tray icon** for menu options

### Command Line Options

```bash
# Run with console window visible
PrinterTrayApp.exe --console

# Run in test mode
PrinterTrayApp.exe --test

# Show help
PrinterTrayApp.exe --help
```

### Verify It's Running

```bash
# Check health endpoint
curl http://127.0.0.1:9877/health

# Response:
{
  "ok": true,
  "version": "0.1.0",
  "printers": ["passkitchen", "Microsoft Print to PDF"],
  "uptimeSeconds": 120
}
```

## 📡 API Documentation

### Base URL
```
http://127.0.0.1:9877
```

### Endpoints

#### GET /health
Health check and printer status

**Response:**
```json
{
  "ok": true,
  "version": "0.1.0",
  "printers": ["passkitchen"],
  "uptimeSeconds": 300
}
```

#### GET /printers
Get detailed printer information

**Response:**
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

#### POST /print
Send print job to printer

**Request Body:**
```json
{
  "_id": {
    "id": "test-001",
    "siteId": "site-001"
  },
  "template": {
    "body": "<root><text>{{storeName}}</text></root>",
    "name": "Receipt",
    "templateType": 0
  },
  "templateData": "{\"storeName\":\"My Store\"}",
  "printerDeviceName": "passkitchen",
  "isOpenCashDrawer": false,
  "retryCount": 0
}
```

**Response:**
```json
{
  "jobId": "PRT-20250117-000001",
  "spoolJobId": 42,
  "printerName": "passkitchen",
  "status": "Sent to printer",
  "timestamp": "2025-01-17T10:30:00Z"
}
```

## ⚙️ Configuration

### Printer Configuration (Optional)

Create `printers.json` in the application directory:

```json
{
  "mappings": [
    {
      "logicalName": "kitchen",
      "windowsPrinterName": "passkitchen"
    },
    {
      "logicalName": "bar",
      "windowsPrinterName": "pasbar"
    }
  ],
  "fallbackPrinter": "passkitchen"
}
```

### Template Structure

XML templates support these elements:

```xml
<root charset="utf-8">
    <!-- Text with formatting -->
    <text align="center" size="wide">{{header}}</text>
    
    <!-- Line separator -->
    <separator char="-" />
    
    <!-- Blank lines -->
    <blank lines="2" />
    
    <!-- Table layout -->
    <table>
        <column width="30">{{item.name}}</column>
        <column width="10" align="right">{{item.price}}</column>
    </table>
    
    <!-- Printer commands -->
    <command cmd="cut" />
    <command cmd="opencashdrawer" />
    
    <!-- Barcode/QR code -->
    <barcode type="code128">{{orderNumber}}</barcode>
    <qrcode>{{qrData}}</qrcode>
</root>
```

## 🧪 Testing

### Using the Test Form

1. **Right-click tray icon** → "Test Print (JSON)"
2. **Paste PrinterTask JSON** in the text area
3. **Select printer** from dropdown
4. **Click "Send to Printer"**

### Using PowerShell

```powershell
# Test print endpoint
$json = Get-Content test-receipt.json -Raw
Invoke-RestMethod -Uri "http://127.0.0.1:9877/print" `
    -Method POST `
    -Body $json `
    -ContentType "application/json"
```

### Using curl

```bash
# Test health check
curl http://127.0.0.1:9877/health

# Send test print
curl -X POST http://127.0.0.1:9877/print \
  -H "Content-Type: application/json" \
  -d @test-receipt.json
```

## 🔧 Troubleshooting

### Application Won't Start

1. **Check if already running**
   - Look for "PT" icon in system tray
   - Check Task Manager for PrinterTrayApp.exe

2. **Port already in use**
   - Another application using port 9877
   - Change port in Constants.cs and rebuild

3. **Missing .NET Runtime**
   - Install .NET 8.0 Runtime from Microsoft

### Printer Not Found

1. **Check Windows Printers**
   ```powershell
   Get-Printer | Select-Object Name, PortName, PrinterStatus
   ```

2. **Install printer driver**
   - Download from manufacturer website
   - Use Windows "Add Printer" wizard

3. **Network printer issues**
   - Ensure printer is on same network
   - Check firewall settings
   - Ping printer IP address

### Print Jobs Not Printing

1. **Check printer queue**
   - Open Windows Printer Queue
   - Clear any stuck jobs

2. **Verify RAW printing support**
   - Not all printers support RAW mode
   - Check printer documentation

3. **Test with simple text**
   ```json
   {
     "template": {
       "body": "<root><text>TEST PRINT</text><command cmd=\"cut\"/></root>"
     },
     "templateData": "{}",
     "printerDeviceName": "your-printer-name"
   }
   ```

### View Console Output

- **Right-click tray icon** → "Show Console"
- Check for error messages
- Monitor print job processing

## 🛠️ Development

### Project Structure
```
PrinterTrayApp.csproj     # Project file
Program.cs                # Entry point
TrayApplicationContext.cs # System tray
HttpServer.cs            # REST API server
Services/                # Business logic
Models/                  # Data models
Forms/                   # UI forms
```

### Build Commands
```bash
# Clean build
dotnet clean
dotnet build

# Run tests
dotnet test

# Publish for production
dotnet publish -c Release -r win-x64 --self-contained true

# Create single file executable
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

### Key Technologies
- **.NET 8.0** - Modern C# framework
- **Windows Forms** - System tray and UI
- **ASP.NET Core** - HTTP server (Kestrel)
- **Newtonsoft.Json** - JSON processing
- **System.Drawing** - Printer interaction
- **P/Invoke** - Windows API calls

### ESC/POS Commands
The app generates ESC/POS commands as strings (not bytes):
- Initialize: `ESC@`
- Cut paper: `EscP`
- Open drawer: `Escpulse`
- Text size: `Escsize1`, `Escsize2`
- Bold: `EscboldOn`, `EscboldOff`
- Alignment: `EscalignCenter`, `EscalignLeft`

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open Pull Request

## 📞 Support

- **GitHub Issues**: Report bugs and request features
- **Documentation**: Check CLAUDE.md for detailed implementation notes
- **Logs**: Enable console window for debugging

---

**Version**: 0.1.0  
**Status**: Production Ready  
**Last Updated**: January 2025