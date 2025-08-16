# 🖨️ Windows Printer Tray App – Development Guide for Claude

## Project Overview
Building a Windows Tray application that runs an HTTP API server for ESC/POS thermal printer management. The app accepts print jobs via REST API and sends them to Windows printers.

## 🔑 Critical Development Rules

### 1. MILESTONE DOCUMENTATION
- **CREATE** a `MILESTONE-X.md` document before starting each milestone
- Include detailed plan, test scenarios, success criteria
- Update the document as features are completed
- Mark items with ✅ when done, 🚧 in progress, ⏳ pending

### 2. PLAN FIRST, CODE SECOND
- **ALWAYS** write a detailed PLAN before any code
- List steps, inputs, outputs, edge cases
- Wait for user approval before implementing
- Break complex tasks into testable parts

### 3. Incremental Development
- User wants to **test and run after EACH part**
- Build features incrementally
- Each part must be independently testable
- Provide clear test instructions after each change

### 4. Code Quality Rules
- NO COMMENTS in code unless explicitly requested
- Follow existing code patterns and conventions
- Check for existing libraries before adding new ones
- Never expose or log secrets/keys
- Always use absolute paths, not relative

### 5. Testing Requirements
- After completing features, run lint/typecheck if available
- Test each endpoint with PowerShell/Postman
- Verify console logging works
- Check tray icon functionality

## 📋 Development Milestones

### ✅ Milestone 1: Core Skeleton & API [COMPLETED]
**Goal:** Windows Tray app with HTTP server on `127.0.0.1:9877`

**Implemented Features:**
- Windows Forms tray application with icon
- Kestrel HTTP server running in background
- Toggle console window from tray menu
- Basic endpoints:
  - `GET /health` - Returns server status
  - `POST /print` - Placeholder (501)
  - `GET /self-test` - Placeholder (501)
- Single instance enforcement
- Console logging with timestamps

**Files Created:**
- `PrinterTrayApp.csproj` - .NET 8 Windows Forms project
- `Program.cs` - Entry point with mutex
- `HttpServer.cs` - Kestrel server setup
- `TrayApplicationContext.cs` - Tray icon management
- `ConsoleWindow.cs` - Toggle console output
- `Models/HealthResponse.cs` - API response model

### 🚧 Milestone 2: Printer Discovery & Mapping [IN PROGRESS]
**Goal:** Enumerate Windows printers and manage logical mappings

**Planned Features:**
- Discover all installed Windows printers
- Support `printers.json` config for name mappings
- Map logical names (e.g., "kitchen") to Windows printer names
- Return printer list in `/health` and new `/printers` endpoint
- Show printer online/offline status

**Files to Create:**
- `PrinterService.cs` - Printer discovery logic
- `Models/PrinterMapping.cs` - Mapping model
- `printers.json` - Optional configuration

### 📅 Milestone 3: Print Job Intake [PENDING]
**Goal:** Accept and validate print jobs via API

**Print Job Schema v1.0:**
```json
{
  "version": "1.0",
  "jobId": "uuid",
  "targetPrinter": { "logicalName": "kitchen" },
  "content": {
    "type": "escpos-template",
    "template": { 
      "body": "<root>...</root>", 
      "templateType": "Receipt|Docket" 
    },
    "templateData": { }
  },
  "options": { "copies": 1, "cut": true }
}
```

**Validation Rules:**
- `version` must be "1.0"
- Payload max 2MB
- `jobId` required for idempotency
- Unknown printer → 404
- Invalid schema → 400

### 📅 Milestone 4: Template Rendering [PENDING]
**Goal:** Parse XML templates with token substitution

**XML Elements (v1):**
- `<text font-family="a|b" font-style="b" size="normal|wide|high|wide-high" align="left|center|right">`
- `<blank lines="N"/>`
- `<separator char="-"/>`
- `<command cmd="cut"/>`

**Token Format:** `{{path.to.value}}`
- Empty substitutions skip entire node
- Output neutral Instruction List (IR)

### 📅 Milestone 5: ESC/POS Compilation [PENDING]
**Goal:** Convert IR to ESC/POS byte arrays

**Features:**
- Text with alignment/sizing
- Newlines and separators
- Paper cut commands
- Send via `winspool.drv` (RAW print)

### 📅 Milestone 6: Job Processing & Status [PENDING]
**Goal:** Queue management and job tracking

**Features:**
- In-memory job queue
- States: `queued → printing → completed|error`
- Retry once on failure
- `GET /jobs/:id` endpoint for status

### 📅 Milestone 7: Self-Test Feature [PENDING]
**Goal:** Built-in diagnostic receipt

**Endpoint:** `GET /self-test`
**Prints:**
- App name + version
- Date/time
- Installed printers list
- Test patterns (text styles, cut)

## 🧪 Test Commands

### PowerShell Testing
```powershell
# Health check
Invoke-WebRequest -Uri http://127.0.0.1:9877/health

# Print job (when implemented)
$body = @{
    version = "1.0"
    jobId = "test-123"
    targetPrinter = @{ logicalName = "kitchen" }
    content = @{
        type = "escpos-template"
        template = @{
            body = "<root><text>Test</text></root>"
            templateType = "Receipt"
        }
        templateData = @{}
    }
    options = @{ copies = 1; cut = $true }
} | ConvertTo-Json -Depth 10

Invoke-WebRequest -Uri http://127.0.0.1:9877/print -Method POST -Body $body -ContentType "application/json"

# Self-test
Invoke-WebRequest -Uri http://127.0.0.1:9877/self-test
```

## 🏗️ Project Structure
```
PrinterTrayApp/
├── PrinterTrayApp.csproj      # .NET 8 Windows Forms project
├── Program.cs                 # Entry point + mutex
├── HttpServer.cs              # Kestrel HTTP server
├── TrayApplicationContext.cs  # System tray management
├── ConsoleWindow.cs           # Console toggle functionality
├── PrinterService.cs          # [TODO] Printer discovery
├── Models/
│   ├── HealthResponse.cs     # Health endpoint response
│   ├── PrinterMapping.cs     # [TODO] Printer mappings
│   └── PrintJob.cs           # [TODO] Job schema
├── printers.json             # [TODO] Optional printer config
└── CLAUDE.md                 # This file - Development guide
```

## 🛠️ Tech Stack
- **.NET 8** - Latest LTS framework
- **Windows Forms** - System tray icon
- **ASP.NET Core Kestrel** - Lightweight HTTP server
- **System.Drawing.Printing** - Windows printer access
- **System.Text.Json** - JSON serialization

## 📝 Current Status
- ✅ Tray app running with HTTP server
- ✅ Console window toggle
- ✅ Basic API endpoints
- 🚧 Working on printer discovery
- ⏳ Print job processing pending
- ⏳ ESC/POS compilation pending

## 🎯 Next Steps
1. Complete printer discovery implementation
2. Test with actual Windows printers
3. Add printer mapping configuration
4. Begin print job intake (Milestone 3)

## 💡 Important Notes
- App runs on fixed port `127.0.0.1:9877`
- Single instance enforced via mutex
- Console shows live HTTP request logs
- Tray icon provides quick access to console and status
- All endpoints return JSON responses
- Eventually will publish as single .exe file

## 🚨 Remember
1. **ALWAYS PLAN FIRST** - Never jump into code
2. **TEST AFTER EACH PART** - User wants incremental testing
3. **NO COMMENTS** - Unless explicitly requested
4. **FOLLOW PATTERNS** - Check existing code style first