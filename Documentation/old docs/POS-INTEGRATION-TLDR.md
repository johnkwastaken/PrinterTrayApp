# 🚀 POS Integration - TLDR

## What Changes (5 Functions)

| Old | New | Where |
|-----|-----|-------|
| `getPrinters()` | `GET /printers` | Settings page |
| `getPrinterStatus()` | `GET /printers` | refreshLocalStatus |
| `printDirect()` | `POST /print` | Print submission |
| `getPrinterJob()` | `GET /status/spooler/{id}` | Job monitoring |
| `sendJobCommand()` | `POST /retry-queue` | Retry jobs |

## The Code Changes

### 1. Add One Service File
```typescript
// src/services/TrayAppService.ts
class TrayAppService {
  async print(printerTask) {
    const res = await fetch('http://127.0.0.1:9877/print', {
      method: 'POST',
      body: JSON.stringify(printerTask)
    });
    return res.json();
  }
  
  async getPrinters() {
    const res = await fetch('http://127.0.0.1:9877/printers');
    return res.json();
  }
  
  // ... other endpoints
}
```

### 2. Change Print Submission
```typescript
// OLD
const jobId = printDirect(printerName, docName, "RAW", commands);

// NEW
const result = await trayApp.print(printerTask);
if (!result.success) throw new Error(result.message);
const jobId = result.spoolerId;
```

### 3. Change Printer Discovery
```typescript
// OLD
const printers = getPrinters();

// NEW  
const data = await trayApp.getPrinters();
const printers = data.printers;
```

### 4. Change Status Check
```typescript
// OLD - Multiple calls
for (printer of printers) {
  const status = getPrinterStatus(printer.name);
}

// NEW - One call for all
const data = await trayApp.getPrinters();
// Map statuses to all printers
```

## What Stays the Same

✅ PrinterTask structure  
✅ Ditto sync  
✅ Retry logic (3 retries)  
✅ Secondary printer fallback  
✅ Error handling  
✅ Database updates  
✅ UI (just gets more data)

## Benefits

- **No duplicates** - GUID prevents double prints
- **Better status** - Online/offline, errors, job counts
- **One API call** - Get all printer statuses at once
- **Fallback works** - Keep native module as backup

## That's It!

Replace 5 function calls. Everything else stays exactly the same.