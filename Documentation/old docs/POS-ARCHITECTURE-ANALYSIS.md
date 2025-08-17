# 🏗️ POS System Architecture Analysis & C# Implementation Guide

## Executive Summary
This document analyzes an existing TypeScript/Node.js POS printing system and provides a detailed roadmap for implementing equivalent functionality in C#/.NET for the PrinterTrayApp.

---

## 📊 Current System Overview

### Architecture Components
1. **Native printer bindings** – C++ addon exposing Windows printer operations via Node-API
2. **Printer domain models** – Entities representing printers, tasks, and job status
3. **Command and template generation** – XML templates → ESC/POS command strings
4. **Printer task lifecycle** – Queue management, retries, and device dispatch
5. **Task creation utilities** – Hooks for receipts, dockets, self-tests, cash-drawer operations
6. **Printer management UI** – Discovery, configuration, and testing interface

---

## 🔍 Detailed Analysis

### 1. Native Printer Bindings
**Current Implementation:**
- **Technology:** Node-API addon written in C++
- **File:** `printers.node` with TypeScript definitions in `src/printers/printers.d.ts`
- **Core Functions:**
  - `GetPrinters()` - Enumerate system printers
  - `GetPrinterStatus(name)` - Query printer status
  - `GetPrinterJobs(name)` - List print queue
  - `PrintDirect(name, data)` - Send raw bytes to printer
  - `SendJobCommand(name, jobId, command)` - Control print jobs
- **Windows APIs Used:** `SetJob`, `WritePrinter`, `EnumPrinters`
- **Error Handling:** Native exceptions mapped to JS errors
- **Platform Support:** Windows-only (stubs throw "not implemented" on other OS)

### 2. Printer Domain Models
**Key Entities:**
```typescript
// JobCommand enum - Maps to Windows spooler constants
enum JobCommand {
  JOB_CONTROL_PAUSE = 1,
  JOB_CONTROL_RESUME = 2,
  JOB_CONTROL_CANCEL = 3,
  JOB_CONTROL_RESTART = 4,
  JOB_CONTROL_DELETE = 5
}

// JobStatus enum - Windows job states
enum JobStatus {
  PAUSED = 0x00000001,
  ERROR = 0x00000002,
  DELETING = 0x00000004,
  SPOOLING = 0x00000008,
  PRINTING = 0x00000010,
  OFFLINE = 0x00000020,
  PAPEROUT = 0x00000040,
  PRINTED = 0x00000080
}

// PrinterSettings - Device metadata
interface PrinterSettings {
  id: string
  name: string
  isOnline: boolean
  lastSeen: Date
  location?: string
  isDefault: boolean
  isCashDrawer: boolean
}

// PrinterTask - Unit of work
interface PrinterTask {
  id: string
  template: string        // XML template body
  templateData: object    // JSON data for placeholders
  templateType: 'Receipt' | 'Docket' | 'Report'
  retryCount: number
  maxRetries: number
  currentPrinterId: string
  secondaryPrinterId?: string
  isOpenCashDrawer: boolean
  status: 'pending' | 'printing' | 'completed' | 'failed'
  createdAt: Date
}
```

### 3. Command and Template Generation

#### Template Structure
**XML Template Example:**
```xml
<root>
  <text align="center" size="wide">{{storeName}}</text>
  <blank lines="1"/>
  <separator char="-"/>
  <text>Order #{{orderNumber}}</text>
  <receipt-section>
    <text>{{item.name}} x{{item.quantity}}</text>
    <text align="right">${{item.price}}</text>
  </receipt-section>
  <separator char="="/>
  <text size="high" align="right">Total: ${{total}}</text>
  <blank lines="3"/>
  <command cmd="cut"/>
</root>
```

#### Rendering Pipeline
1. **Template Parsing:** XML string → DOM tree
2. **Token Replacement:** `{{path.to.value}}` → actual values from templateData
3. **Section Expansion:** Custom tags like `<receipt-section>` expanded based on context
4. **Node Removal:** Unmatched tokens result in node deletion
5. **Command Generation:** DOM nodes → ESC/POS byte sequences

#### Node-to-Command Mapping
```typescript
// makeCommands - Main conversion function
function makeCommands(template: string, data: object): string {
  const dom = renderTemplate(template, data)
  const commands: string[] = []
  
  dom.childNodes.forEach(node => {
    commands.push(makeCommandFromElement(node))
  })
  
  return commands.join(POS80Commands.nextLine)
}

// makeCommandFromElement - Node type dispatch
function makeCommandFromElement(element: Element): string {
  switch(element.tagName) {
    case 'text':
      return createTextCommand(element)
    case 'blank':
      return createBlankCommand(element)
    case 'separator':
      return createSeparatorCommand(element)
    case 'command':
      return createCommandCommand(element)
    default:
      return ''
  }
}
```

### 4. ESC/POS Command Building

#### POS80Commands Object
```typescript
const POS80Commands = {
  // Initialization
  init: '\x1B\x40',
  
  // Font commands
  fontA: '\x1B\x4D\x00',
  fontB: '\x1B\x4D\x01',
  
  // Text size
  sizeNormal: '\x1D\x21\x00',
  sizeWide: '\x1D\x21\x10',
  sizeHigh: '\x1D\x21\x01',
  sizeWideHigh: '\x1D\x21\x11',
  
  // Alignment
  alignLeft: '\x1B\x61\x00',
  alignCenter: '\x1B\x61\x01',
  alignRight: '\x1B\x61\x02',
  
  // Special commands
  cut: '\x1D\x56\x42\x00',
  partialCut: '\x1D\x56\x41\x00',
  cashDrawer: '\x1B\x70\x00\x19\xFA',
  beep: '\x1B\x42\x05\x09',
  
  // Line control
  nextLine: '\x0A',
  lineSpacing: '\x1B\x33'
}
```

#### Text Command Generation
```typescript
function createTextCommand(element: Element): string {
  const commands: string[] = []
  
  // Font family
  const fontFamily = element.getAttribute('font-family')
  if (fontFamily === 'b') {
    commands.push(POS80Commands.fontB)
  } else {
    commands.push(POS80Commands.fontA)
  }
  
  // Text size
  const size = element.getAttribute('size')
  switch(size) {
    case 'wide':
      commands.push(POS80Commands.sizeWide)
      break
    case 'high':
      commands.push(POS80Commands.sizeHigh)
      break
    case 'wide-high':
      commands.push(POS80Commands.sizeWideHigh)
      break
    default:
      commands.push(POS80Commands.sizeNormal)
  }
  
  // Alignment
  const align = element.getAttribute('align')
  switch(align) {
    case 'center':
      commands.push(POS80Commands.alignCenter)
      break
    case 'right':
      commands.push(POS80Commands.alignRight)
      break
    default:
      commands.push(POS80Commands.alignLeft)
  }
  
  // Text content
  const text = element.textContent || ''
  commands.push(text)
  
  // Reset to defaults
  commands.push(POS80Commands.sizeNormal)
  commands.push(POS80Commands.alignLeft)
  
  return commands.join('')
}
```

### 5. Printer Task Lifecycle

#### Task Processing Flow
```typescript
class PrinterContextProvider {
  async processTasks() {
    const tasks = await getPendingTasks(this.deviceId)
    
    for (const task of tasks) {
      try {
        // 1. Mark as printing
        await updateTaskStatus(task.id, 'printing')
        
        // 2. Generate commands
        const commands = makeCommands(task.template, task.templateData)
        
        // 3. Add cash drawer pulse if needed
        const finalCommands = task.isOpenCashDrawer 
          ? commands + POS80Commands.cashDrawer 
          : commands
        
        // 4. Send to printer
        await printDirect(task.currentPrinterId, finalCommands)
        
        // 5. Mark complete
        await updateTaskStatus(task.id, 'completed')
        
      } catch (error) {
        // 6. Handle failure
        task.retryCount++
        
        if (task.retryCount < task.maxRetries) {
          // Retry with same printer
          await updateTask(task)
        } else if (task.secondaryPrinterId) {
          // Failover to secondary
          task.currentPrinterId = task.secondaryPrinterId
          task.retryCount = 0
          await updateTask(task)
        } else {
          // Mark failed
          await updateTaskStatus(task.id, 'failed')
        }
      }
    }
  }
}
```

---

## 🚀 C# Implementation Guide

### Phase 1: Core Models

#### PrinterTask.cs
```csharp
namespace PrinterTrayApp.Models
{
    public class PrinterTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Template { get; set; }
        public Dictionary<string, object> TemplateData { get; set; }
        public TemplateType TemplateType { get; set; }
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        public string CurrentPrinterId { get; set; }
        public string SecondaryPrinterId { get; set; }
        public bool IsOpenCashDrawer { get; set; }
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public enum TemplateType
    {
        Receipt,
        Docket,
        Report,
        SelfTest
    }
    
    public enum TaskStatus
    {
        Pending,
        Printing,
        Completed,
        Failed
    }
}
```

### Phase 2: Template Engine

#### TemplateRenderer.cs
```csharp
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PrinterTrayApp.Services
{
    public class TemplateRenderer
    {
        private static readonly Regex TokenRegex = new Regex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
        
        public XDocument RenderTemplate(string templateXml, Dictionary<string, object> data)
        {
            var doc = XDocument.Parse(templateXml);
            var jsonData = JObject.FromObject(data);
            
            // Process all elements recursively
            ProcessElement(doc.Root, jsonData);
            
            return doc;
        }
        
        private void ProcessElement(XElement element, JObject data)
        {
            // Replace tokens in text nodes
            foreach (var textNode in element.DescendantNodes().OfType<XText>().ToList())
            {
                var newText = ReplaceTokens(textNode.Value, data);
                if (newText == null)
                {
                    // Remove parent element if token not found
                    textNode.Parent?.Remove();
                }
                else
                {
                    textNode.ReplaceWith(new XText(newText));
                }
            }
            
            // Replace tokens in attributes
            foreach (var attr in element.Attributes().ToList())
            {
                var newValue = ReplaceTokens(attr.Value, data);
                if (newValue != null)
                {
                    attr.Value = newValue;
                }
            }
            
            // Expand custom sections
            ExpandCustomSections(element, data);
            
            // Process child elements
            foreach (var child in element.Elements().ToList())
            {
                ProcessElement(child, data);
            }
        }
        
        private string ReplaceTokens(string text, JObject data)
        {
            return TokenRegex.Replace(text, match =>
            {
                var path = match.Groups[1].Value;
                var token = data.SelectToken(path);
                return token?.ToString() ?? null;
            });
        }
        
        private void ExpandCustomSections(XElement element, JObject data)
        {
            // Handle receipt-section, docket-section, etc.
            if (element.Name.LocalName.EndsWith("-section"))
            {
                var sectionType = element.Name.LocalName.Replace("-section", "");
                if (ShouldRenderSection(sectionType, data))
                {
                    // Replace section element with its contents
                    element.ReplaceWith(element.Elements());
                }
                else
                {
                    element.Remove();
                }
            }
        }
        
        private bool ShouldRenderSection(string sectionType, JObject data)
        {
            // Logic to determine if section should be rendered
            var templateType = data.SelectToken("_templateType")?.ToString();
            return templateType?.ToLower() == sectionType;
        }
    }
}
```

### Phase 3: ESC/POS Command Builder

#### EscPosCommands.cs
```csharp
namespace PrinterTrayApp.Services
{
    public static class EscPosCommands
    {
        // Initialization
        public static readonly byte[] Init = { 0x1B, 0x40 };
        
        // Font commands
        public static readonly byte[] FontA = { 0x1B, 0x4D, 0x00 };
        public static readonly byte[] FontB = { 0x1B, 0x4D, 0x01 };
        
        // Text size
        public static readonly byte[] SizeNormal = { 0x1D, 0x21, 0x00 };
        public static readonly byte[] SizeWide = { 0x1D, 0x21, 0x10 };
        public static readonly byte[] SizeHigh = { 0x1D, 0x21, 0x01 };
        public static readonly byte[] SizeWideHigh = { 0x1D, 0x21, 0x11 };
        
        // Alignment
        public static readonly byte[] AlignLeft = { 0x1B, 0x61, 0x00 };
        public static readonly byte[] AlignCenter = { 0x1B, 0x61, 0x01 };
        public static readonly byte[] AlignRight = { 0x1B, 0x61, 0x02 };
        
        // Special commands
        public static readonly byte[] Cut = { 0x1D, 0x56, 0x42, 0x00 };
        public static readonly byte[] PartialCut = { 0x1D, 0x56, 0x41, 0x00 };
        public static readonly byte[] CashDrawer = { 0x1B, 0x70, 0x00, 0x19, 0xFA };
        public static readonly byte[] Beep = { 0x1B, 0x42, 0x05, 0x09 };
        
        // Line control
        public static readonly byte[] NextLine = { 0x0A };
        public static readonly byte[] LineSpacing = { 0x1B, 0x33 };
    }
}
```

#### CommandBuilder.cs
```csharp
using System.Xml.Linq;
using System.Text;

namespace PrinterTrayApp.Services
{
    public class CommandBuilder
    {
        private readonly TemplateRenderer _renderer;
        
        public CommandBuilder(TemplateRenderer renderer)
        {
            _renderer = renderer;
        }
        
        public byte[] BuildCommands(string template, Dictionary<string, object> data)
        {
            var doc = _renderer.RenderTemplate(template, data);
            var commands = new List<byte>();
            
            // Initialize printer
            commands.AddRange(EscPosCommands.Init);
            
            // Process each element
            foreach (var element in doc.Root.Elements())
            {
                commands.AddRange(ProcessElement(element));
            }
            
            return commands.ToArray();
        }
        
        private byte[] ProcessElement(XElement element)
        {
            var commands = new List<byte>();
            
            switch (element.Name.LocalName.ToLower())
            {
                case "text":
                    commands.AddRange(CreateTextCommand(element));
                    break;
                    
                case "blank":
                    commands.AddRange(CreateBlankCommand(element));
                    break;
                    
                case "separator":
                    commands.AddRange(CreateSeparatorCommand(element));
                    break;
                    
                case "command":
                    commands.AddRange(CreateSpecialCommand(element));
                    break;
                    
                default:
                    // Process child elements for unknown tags
                    foreach (var child in element.Elements())
                    {
                        commands.AddRange(ProcessElement(child));
                    }
                    break;
            }
            
            return commands.ToArray();
        }
        
        private byte[] CreateTextCommand(XElement element)
        {
            var commands = new List<byte>();
            
            // Font family
            var fontFamily = element.Attribute("font-family")?.Value;
            if (fontFamily == "b")
            {
                commands.AddRange(EscPosCommands.FontB);
            }
            else
            {
                commands.AddRange(EscPosCommands.FontA);
            }
            
            // Text size
            var size = element.Attribute("size")?.Value;
            switch (size)
            {
                case "wide":
                    commands.AddRange(EscPosCommands.SizeWide);
                    break;
                case "high":
                    commands.AddRange(EscPosCommands.SizeHigh);
                    break;
                case "wide-high":
                    commands.AddRange(EscPosCommands.SizeWideHigh);
                    break;
                default:
                    commands.AddRange(EscPosCommands.SizeNormal);
                    break;
            }
            
            // Alignment
            var align = element.Attribute("align")?.Value;
            switch (align)
            {
                case "center":
                    commands.AddRange(EscPosCommands.AlignCenter);
                    break;
                case "right":
                    commands.AddRange(EscPosCommands.AlignRight);
                    break;
                default:
                    commands.AddRange(EscPosCommands.AlignLeft);
                    break;
            }
            
            // Text content
            var text = element.Value ?? "";
            commands.AddRange(Encoding.ASCII.GetBytes(text));
            
            // Line feed
            commands.AddRange(EscPosCommands.NextLine);
            
            // Reset to defaults
            commands.AddRange(EscPosCommands.SizeNormal);
            commands.AddRange(EscPosCommands.AlignLeft);
            
            return commands.ToArray();
        }
        
        private byte[] CreateBlankCommand(XElement element)
        {
            var commands = new List<byte>();
            var lines = int.Parse(element.Attribute("lines")?.Value ?? "1");
            
            for (int i = 0; i < lines; i++)
            {
                commands.AddRange(EscPosCommands.NextLine);
            }
            
            return commands.ToArray();
        }
        
        private byte[] CreateSeparatorCommand(XElement element)
        {
            var commands = new List<byte>();
            var character = element.Attribute("char")?.Value ?? "-";
            var width = 42; // Standard 80mm receipt width
            
            var separator = new string(character[0], width);
            commands.AddRange(Encoding.ASCII.GetBytes(separator));
            commands.AddRange(EscPosCommands.NextLine);
            
            return commands.ToArray();
        }
        
        private byte[] CreateSpecialCommand(XElement element)
        {
            var cmd = element.Attribute("cmd")?.Value;
            
            return cmd switch
            {
                "cut" => EscPosCommands.Cut,
                "partial-cut" => EscPosCommands.PartialCut,
                "cash-drawer" => EscPosCommands.CashDrawer,
                "beep" => EscPosCommands.Beep,
                _ => Array.Empty<byte>()
            };
        }
    }
}
```

### Phase 4: Task Processor

#### PrinterTaskProcessor.cs
```csharp
namespace PrinterTrayApp.Services
{
    public class PrinterTaskProcessor
    {
        private readonly Queue<PrinterTask> _taskQueue = new();
        private readonly CommandBuilder _commandBuilder;
        private readonly SimplePrintService _printService;
        private readonly object _queueLock = new();
        private Timer _processTimer;
        
        public PrinterTaskProcessor(CommandBuilder commandBuilder, SimplePrintService printService)
        {
            _commandBuilder = commandBuilder;
            _printService = printService;
            
            // Process queue every second
            _processTimer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        
        public void EnqueueTask(PrinterTask task)
        {
            lock (_queueLock)
            {
                _taskQueue.Enqueue(task);
            }
            ConsoleWindow.WriteLine($"Task {task.Id} enqueued for printer {task.CurrentPrinterId}");
        }
        
        private async void ProcessQueue(object state)
        {
            PrinterTask task = null;
            
            lock (_queueLock)
            {
                if (_taskQueue.Count > 0)
                {
                    task = _taskQueue.Dequeue();
                }
            }
            
            if (task != null)
            {
                await ProcessTask(task);
            }
        }
        
        private async Task ProcessTask(PrinterTask task)
        {
            try
            {
                // Update status
                task.Status = TaskStatus.Printing;
                ConsoleWindow.WriteLine($"Processing task {task.Id}");
                
                // Generate commands
                var commands = _commandBuilder.BuildCommands(task.Template, task.TemplateData);
                
                // Add cash drawer pulse if needed
                if (task.IsOpenCashDrawer)
                {
                    var list = commands.ToList();
                    list.AddRange(EscPosCommands.CashDrawer);
                    commands = list.ToArray();
                }
                
                // Send to printer
                var success = await Task.Run(() => 
                    _printService.SendRawBytes(task.CurrentPrinterId, commands)
                );
                
                if (success)
                {
                    task.Status = TaskStatus.Completed;
                    ConsoleWindow.WriteLine($"Task {task.Id} completed successfully");
                }
                else
                {
                    throw new Exception("Print failed");
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Task {task.Id} failed: {ex.Message}");
                
                // Handle retry logic
                task.RetryCount++;
                
                if (task.RetryCount < task.MaxRetries)
                {
                    // Re-queue for retry
                    task.Status = TaskStatus.Pending;
                    EnqueueTask(task);
                    ConsoleWindow.WriteLine($"Task {task.Id} re-queued (retry {task.RetryCount}/{task.MaxRetries})");
                }
                else if (!string.IsNullOrEmpty(task.SecondaryPrinterId))
                {
                    // Try secondary printer
                    task.CurrentPrinterId = task.SecondaryPrinterId;
                    task.SecondaryPrinterId = null;
                    task.RetryCount = 0;
                    task.Status = TaskStatus.Pending;
                    EnqueueTask(task);
                    ConsoleWindow.WriteLine($"Task {task.Id} failing over to secondary printer");
                }
                else
                {
                    // Mark as failed
                    task.Status = TaskStatus.Failed;
                    ConsoleWindow.WriteError($"Task {task.Id} permanently failed");
                }
            }
        }
    }
}
```

### Phase 5: Enhanced Print Service

#### SimplePrintService.cs (Enhanced)
```csharp
public static class SimplePrintService
{
    // ... existing code ...
    
    public static bool SendRawBytes(string printerName, byte[] data)
    {
        IntPtr hPrinter = IntPtr.Zero;
        
        try
        {
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                ConsoleWindow.WriteError($"Failed to open printer {printerName}, Win32 error: {error}");
                return false;
            }

            var docInfo = new DOC_INFO_1
            {
                pDocName = "POS Print Job",
                pDatatype = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, ref docInfo))
            {
                var error = Marshal.GetLastWin32Error();
                ConsoleWindow.WriteError($"Failed to start document, Win32 error: {error}");
                return false;
            }

            if (!StartPagePrinter(hPrinter))
            {
                var error = Marshal.GetLastWin32Error();
                ConsoleWindow.WriteError($"Failed to start page, Win32 error: {error}");
                EndDocPrinter(hPrinter);
                return false;
            }

            if (!WritePrinter(hPrinter, data, data.Length, out int written))
            {
                var error = Marshal.GetLastWin32Error();
                ConsoleWindow.WriteError($"Failed to write data, Win32 error: {error}");
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                return false;
            }

            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
            
            ConsoleWindow.WriteLine($"Successfully sent {written} bytes to {printerName}");
            return true;
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Exception in SendRawBytes: {ex.Message}");
            return false;
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
    }
}
```

---

## 📝 Integration Steps

### Step 1: Create Models

#### PrintJob.cs - Complete Job Model
```csharp
namespace PrinterTrayApp.Models
{
    public class PrintJob
    {
        // Identification
        public string JobId { get; set; }           // GUID from POS
        public string JobNumber { get; set; }       // Generated: PRT-20250116-000001
        public string DocumentRef { get; set; }     // From POS: "Order #12345" or "Cash Report"
        public string DocumentType { get; set; }    // Receipt, Docket, Report, CashDrawer
        
        // Printing
        public string Printer { get; set; }         // Target printer name
        public string Template { get; set; }        // XML template as string
        public Dictionary<string, object> TemplateData { get; set; }  // Data for tokens
        
        // Control
        public int MaxRetries { get; set; } = 3;
        public bool IsCashDrawer { get; set; }      // Cash drawer safety flag
        
        // Tracking
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
    }
    
    public class PrintResult
    {
        public string JobId { get; set; }
        public string JobNumber { get; set; }
        public string DocumentRef { get; set; }
        public string DocumentType { get; set; }
        public bool Success { get; set; }
        public int Attempts { get; set; }
        public string Error { get; set; }
        public bool Aborted { get; set; }           // True for cash drawer safety abort
        public string Printer { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.Now;
    }
    
    public static class JobNumberGenerator
    {
        private static int _counter = 0;
        private static readonly object _lock = new object();
        private static DateTime _lastDate = DateTime.Now.Date;
        
        public static string Generate(string prefix = "PRT")
        {
            lock (_lock)
            {
                // Reset counter daily
                if (DateTime.Now.Date != _lastDate)
                {
                    _counter = 0;
                    _lastDate = DateTime.Now.Date;
                }
                
                _counter++;
                
                // Format: PRT-20250116-000001
                return $"{prefix}-{DateTime.Now:yyyyMMdd}-{_counter:D6}";
            }
        }
    }
}

### Step 2: Implement Template Engine
1. Create `TemplateRenderer.cs` for XML processing
2. Add token replacement logic
3. Implement section expansion

### Step 3: Build Command Generator
1. Create `EscPosCommands.cs` with byte constants
2. Implement `CommandBuilder.cs` for node-to-byte conversion
3. Add text formatting and alignment logic

### Step 4: Create Job Processor with Retry Logic

#### PrintJobProcessor.cs - Job Processing with Safety
```csharp
namespace PrinterTrayApp.Services
{
    public class PrintJobProcessor
    {
        private readonly TemplateRenderer _renderer;
        private readonly CommandBuilder _commandBuilder;
        private readonly PrinterService _printerService;
        
        public PrintJobProcessor(TemplateRenderer renderer, CommandBuilder commandBuilder, PrinterService printerService)
        {
            _renderer = renderer;
            _commandBuilder = commandBuilder;
            _printerService = printerService;
        }
        
        public async Task<PrintResult> ProcessJob(PrintJob job)
        {
            // Generate tracking number
            job.JobNumber = JobNumberGenerator.Generate();
            
            // Build display name for logging
            var displayName = BuildDisplayName(job);
            
            ConsoleWindow.WriteLine($"[{job.JobNumber}] Starting: {displayName}");
            ConsoleWindow.WriteLine($"[{job.JobNumber}] Printer: {job.Printer}, Type: {job.DocumentType}");
            
            // Check if printer exists
            var printer = _printerService.ResolvePrinter(job.Printer);
            if (printer == null)
            {
                return new PrintResult 
                { 
                    JobId = job.JobId,
                    JobNumber = job.JobNumber,
                    DocumentRef = job.DocumentRef,
                    Success = false,
                    Error = $"Printer '{job.Printer}' not found"
                };
            }
            
            // Detect and handle cash drawer
            bool isCashDrawer = DetectCashDrawerCommand(job);
            if (isCashDrawer)
            {
                job.IsCashDrawer = true;
                job.MaxRetries = Math.Min(job.MaxRetries, 3);
                ConsoleWindow.WriteLine($"[{job.JobNumber}] WARNING: Cash drawer command - max 3 attempts");
            }
            
            // Render template and generate commands
            byte[] commands;
            try
            {
                var rendered = _renderer.RenderTemplate(job.Template, job.TemplateData);
                commands = _commandBuilder.BuildCommands(rendered.ToString(), job.TemplateData);
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"[{job.JobNumber}] Template rendering failed: {ex.Message}");
                return new PrintResult 
                { 
                    JobId = job.JobId,
                    JobNumber = job.JobNumber,
                    DocumentRef = job.DocumentRef,
                    Success = false,
                    Error = $"Template error: {ex.Message}"
                };
            }
            
            // Try printing with retries
            int attempts = 0;
            string lastError = "";
            
            while (attempts < job.MaxRetries)
            {
                attempts++;
                ConsoleWindow.WriteLine($"[{job.JobNumber}] Attempt {attempts}/{job.MaxRetries}");
                
                try
                {
                    var success = SimplePrintService.SendRawBytes(job.Printer, commands);
                    
                    if (success)
                    {
                        ConsoleWindow.WriteLine($"[{job.JobNumber}] SUCCESS: {displayName}");
                        
                        if (isCashDrawer)
                        {
                            LogCashDrawerAttempt(job, true, attempts);
                        }
                        
                        return new PrintResult 
                        { 
                            JobId = job.JobId,
                            JobNumber = job.JobNumber,
                            DocumentRef = job.DocumentRef,
                            DocumentType = job.DocumentType,
                            Success = true,
                            Attempts = attempts,
                            Printer = job.Printer
                        };
                    }
                    
                    lastError = "Print spooler rejected job";
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    ConsoleWindow.WriteError($"[{job.JobNumber}] Attempt {attempts} failed: {ex.Message}");
                }
                
                // CRITICAL: Cash drawer safety
                if (isCashDrawer && attempts >= 3)
                {
                    ConsoleWindow.WriteError($"[{job.JobNumber}] CASH DRAWER SAFETY: Aborting after 3 attempts");
                    LogCashDrawerAttempt(job, false, attempts);
                    
                    return new PrintResult 
                    { 
                        JobId = job.JobId,
                        JobNumber = job.JobNumber,
                        DocumentRef = job.DocumentRef,
                        DocumentType = job.DocumentType,
                        Success = false,
                        Attempts = attempts,
                        Error = "Cash drawer command failed - aborted for safety",
                        Aborted = true,
                        Printer = job.Printer
                    };
                }
                
                // Wait before retry
                if (attempts < job.MaxRetries)
                {
                    await Task.Delay(1000);
                }
            }
            
            // All retries failed
            ConsoleWindow.WriteError($"[{job.JobNumber}] FAILED: {displayName} after {attempts} attempts");
            
            if (isCashDrawer)
            {
                LogCashDrawerAttempt(job, false, attempts);
            }
            
            return new PrintResult 
            { 
                JobId = job.JobId,
                JobNumber = job.JobNumber,
                DocumentRef = job.DocumentRef,
                DocumentType = job.DocumentType,
                Success = false,
                Attempts = attempts,
                Error = lastError,
                Printer = job.Printer
            };
        }
        
        private string BuildDisplayName(PrintJob job)
        {
            if (!string.IsNullOrEmpty(job.DocumentRef))
                return job.DocumentRef;
            
            return job.DocumentType switch
            {
                "Receipt" => "Receipt",
                "Docket" => "Kitchen Docket",
                "Report" => "Report",
                "CashDrawer" => "Cash Drawer",
                _ => "Print Job"
            };
        }
        
        private bool DetectCashDrawerCommand(PrintJob job)
        {
            // Multiple detection methods
            return job.IsCashDrawer ||
                   job.DocumentType?.Equals("CashDrawer", StringComparison.OrdinalIgnoreCase) == true ||
                   job.Template?.Contains("<command cmd=\"cash-drawer\"") == true ||
                   job.Template?.Contains("\x1B\x70") == true ||         // ESC p
                   job.Template?.Contains("\\x1B\\x70") == true;        // Escaped ESC p
        }
        
        private void LogCashDrawerAttempt(PrintJob job, bool success, int attempts)
        {
            var log = $"[CASH DRAWER] {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Job: {job.JobNumber} ({job.JobId}) | Success: {success} | Attempts: {attempts} | Printer: {job.Printer}";
            
            ConsoleWindow.WriteLine(log);
            
            // Audit trail
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CashDrawerLog.txt");
                File.AppendAllText(logPath, log + Environment.NewLine);
            }
            catch { }
        }
    }
}

### Step 5: Update API Endpoint
1. Modify `/print` endpoint in `HttpServer.cs`
2. Accept JSON matching schema
3. Create and enqueue `PrinterTask`

### Step 6: Testing
1. Create sample templates
2. Test with various data payloads
3. Verify ESC/POS output on thermal printer

---

## 🧪 Sample Template & Test Data

### Receipt Template
```xml
<root>
  <text align="center" size="wide">{{store.name}}</text>
  <text align="center">{{store.address}}</text>
  <blank lines="1"/>
  <separator char="-"/>
  <text>Order #{{order.number}}</text>
  <text>Date: {{order.date}}</text>
  <blank lines="1"/>
  <separator char="-"/>
  <text size="wide">Items:</text>
  <text>{{items[0].name}} x{{items[0].qty}}</text>
  <text align="right">${{items[0].price}}</text>
  <separator char="-"/>
  <text size="high" align="right">Total: ${{order.total}}</text>
  <blank lines="3"/>
  <text align="center">Thank you!</text>
  <blank lines="3"/>
  <command cmd="cut"/>
</root>
```

### Test Data
```json
{
  "store": {
    "name": "Test Store",
    "address": "123 Main St"
  },
  "order": {
    "number": "12345",
    "date": "2025-01-16",
    "total": "25.99"
  },
  "items": [
    {
      "name": "Test Item",
      "qty": 2,
      "price": "12.99"
    }
  ]
}
```

---

## 🎯 Success Criteria

1. **Template Rendering:** XML templates parse and render correctly
2. **Token Replacement:** All `{{tokens}}` replaced with data
3. **ESC/POS Generation:** Correct byte sequences for all commands
4. **Printer Communication:** RAW bytes sent successfully
5. **Task Management:** Queue, retry, and failover working
6. **API Integration:** `/print` endpoint accepts and processes jobs

---

## 📚 Resources

- [ESC/POS Command Reference](https://reference.epson-biz.com/modules/ref_escpos/index.php)
- [Windows Spooler API](https://docs.microsoft.com/en-us/windows/win32/printdocs/printing-and-print-spooler)
- [XML Processing in C#](https://docs.microsoft.com/en-us/dotnet/standard/linq/linq-xml-overview)

---

## 🔄 Migration Checklist

- [ ] Port domain models to C#
- [ ] Implement template renderer
- [ ] Create ESC/POS command builder
- [ ] Build task processor with queue
- [ ] Enhance print service for byte arrays
- [ ] Update API to accept print jobs
- [ ] Test with actual thermal printer
- [ ] Add error handling and logging
- [ ] Implement retry and failover logic
- [ ] Document API changes

---

*This document provides a complete blueprint for migrating the TypeScript POS printing system to C#/.NET while maintaining feature parity and compatibility with existing ESC/POS printers.*