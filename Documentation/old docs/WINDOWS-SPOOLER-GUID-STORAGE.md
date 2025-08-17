# 📋 Windows Spooler GUID Storage Strategy

## Overview
This document explains how to store and retrieve GUIDs in Windows print spooler jobs for tracking POS print tasks.

## Available Storage Locations

### 1. Document Name Field (RECOMMENDED)
The Document Name is the most reliable field for storing custom metadata like GUIDs.

#### Option A: GUID as Suffix
```csharp
// Format: JobNumber#GUID
string documentName = $"PRT-{DateTime.Now:yyyyMMddHHmmss}#{posGuid}";
PrintDirect.Print(printerName, documentName, "RAW", printData);

// Parse it back
string[] parts = job.DocumentName.Split('#');
string jobNumber = parts[0];
string posGuid = parts.Length > 1 ? parts[1] : null;
```

#### Option B: GUID as Entire Name
```csharp
// Use GUID directly as document name
PrintDirect.Print(printerName, posGuid, "RAW", printData);

// Easy to track - document name IS the GUID
var trackedJob = GetJobByDocumentName(posGuid);
```

#### Option C: JSON in Document Name
```csharp
// Encode metadata as JSON
var metadata = new { guid = posGuid, timestamp = DateTime.UtcNow };
string documentName = Convert.ToBase64String(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata))
);
PrintDirect.Print(printerName, documentName, "RAW", printData);
```

### 2. Using PrintDirect with DEVMODE (Advanced)
```csharp
[DllImport("winspool.drv", SetLastError = true)]
static extern bool SetJob(IntPtr hPrinter, int JobId, int Level, 
    IntPtr pJob, int Command);

// Can potentially use DEVMODE private data area
// However, this is printer-driver specific and not recommended
```

## Recommended Implementation

### Enhanced PrintDirect.cs
```csharp
public static class PrintDirect
{
    // Enhanced print method with GUID support
    public static int PrintWithGuid(
        string printerName, 
        string posGuid,
        string displayName,
        string documentType, 
        byte[] data)
    {
        // Combine GUID with display name
        string documentName = $"{displayName}|{posGuid}";
        
        // Ensure it fits in the 256 char limit
        if (documentName.Length > 256)
        {
            // Truncate display name, keep GUID
            int maxDisplayLen = 256 - posGuid.Length - 1;
            displayName = displayName.Substring(0, maxDisplayLen);
            documentName = $"{displayName}|{posGuid}";
        }
        
        return Print(printerName, documentName, documentType, data);
    }
    
    // Parse GUID from document name
    public static string ExtractGuid(string documentName)
    {
        if (string.IsNullOrEmpty(documentName))
            return null;
            
        // Check for pipe separator
        int pipeIndex = documentName.LastIndexOf('|');
        if (pipeIndex > 0 && pipeIndex < documentName.Length - 1)
        {
            return documentName.Substring(pipeIndex + 1);
        }
        
        // Check for hash separator
        int hashIndex = documentName.LastIndexOf('#');
        if (hashIndex > 0 && hashIndex < documentName.Length - 1)
        {
            return documentName.Substring(hashIndex + 1);
        }
        
        // Assume entire name is GUID if it looks like one
        if (Guid.TryParse(documentName, out _))
        {
            return documentName;
        }
        
        return null;
    }
}
```

### Updated HttpServer Print Endpoint
```csharp
[HttpPost("/print")]
public IActionResult Print([FromBody] PrintRequest request)
{
    lock (_lock)
    {
        // Check for duplicate by scanning spooler
        if (IsGuidInSpooler(request.PosGuid))
        {
            var existingJob = GetJobByGuid(request.PosGuid);
            
            if (existingJob != null)
            {
                if (existingJob.Status == JobStatus.Printed)
                {
                    return Json(new
                    {
                        success = false,
                        action = "AlreadyDone",
                        alreadyCompleted = true
                    });
                }
                else if (existingJob.Status.HasFlag(JobStatus.Printing))
                {
                    return Json(new
                    {
                        success = false,
                        action = "Wait",
                        message = "Currently printing"
                    });
                }
            }
        }
        
        // Generate display name for readability
        var displayName = $"PRT-{DateTime.Now:yyyyMMddHHmmss}";
        
        // Process template
        var printData = GeneratePrintCommands(request.PrinterTask);
        
        // Submit with GUID embedded in document name
        int spoolerId = PrintDirect.PrintWithGuid(
            request.PrinterName ?? GetDefaultPrinter(),
            request.PosGuid,
            displayName,
            "RAW",
            printData
        );
        
        return Json(new
        {
            success = true,
            jobNumber = displayName,
            spoolerId = spoolerId,
            guid = request.PosGuid
        });
    }
}
```

### Scanning Spooler for GUIDs
```csharp
public class SpoolerScanner
{
    // Check if GUID exists in any spooler job
    public bool IsGuidInSpooler(string posGuid)
    {
        foreach (var printer in GetAllPrinters())
        {
            var jobs = GetPrinterJobs(printer);
            
            foreach (var job in jobs)
            {
                string jobGuid = PrintDirect.ExtractGuid(job.DocumentName);
                if (jobGuid == posGuid)
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    // Get job by GUID
    public PrintJobInfo GetJobByGuid(string posGuid)
    {
        foreach (var printer in GetAllPrinters())
        {
            var jobs = GetPrinterJobs(printer);
            
            foreach (var job in jobs)
            {
                string jobGuid = PrintDirect.ExtractGuid(job.DocumentName);
                if (jobGuid == posGuid)
                {
                    return job;
                }
            }
        }
        
        return null;
    }
    
    // Get all jobs with their GUIDs
    public Dictionary<string, PrintJobInfo> GetAllJobsWithGuids()
    {
        var result = new Dictionary<string, PrintJobInfo>();
        
        foreach (var printer in GetAllPrinters())
        {
            var jobs = GetPrinterJobs(printer);
            
            foreach (var job in jobs)
            {
                string jobGuid = PrintDirect.ExtractGuid(job.DocumentName);
                if (!string.IsNullOrEmpty(jobGuid))
                {
                    result[jobGuid] = job;
                }
            }
        }
        
        return result;
    }
}
```

## Windows API Implementation

### Getting Job Information with GUID
```csharp
[DllImport("winspool.drv", CharSet = CharSet.Auto)]
public static extern bool GetJob(IntPtr hPrinter, int JobId, int Level, 
    IntPtr pJob, int cbBuf, out int pcbNeeded);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public struct JOB_INFO_1
{
    public int JobId;
    public IntPtr pPrinterName;
    public IntPtr pMachineName;
    public IntPtr pUserName;
    public IntPtr pDocument;      // This is where our GUID lives!
    public IntPtr pDatatype;
    public IntPtr pStatus;
    public int Status;
    public int Priority;
    public int Position;
    public int TotalPages;
    public int PagesPrinted;
    public SYSTEMTIME Submitted;
}

public static string GetJobDocumentName(string printerName, int jobId)
{
    IntPtr hPrinter;
    if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        return null;
    
    try
    {
        int needed;
        GetJob(hPrinter, jobId, 1, IntPtr.Zero, 0, out needed);
        
        if (needed == 0)
            return null;
        
        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (GetJob(hPrinter, jobId, 1, buffer, needed, out needed))
            {
                var jobInfo = (JOB_INFO_1)Marshal.PtrToStructure(buffer, typeof(JOB_INFO_1));
                return Marshal.PtrToStringAuto(jobInfo.pDocument);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
    finally
    {
        ClosePrinter(hPrinter);
    }
    
    return null;
}
```

## Alternative: Using a Tracking Database

If you need more metadata than fits in the document name:

```csharp
public class PrintJobTracker
{
    private readonly Dictionary<int, PrintJobMetadata> _spoolerIdToMetadata = new();
    private readonly Dictionary<string, PrintJobMetadata> _guidToMetadata = new();
    
    public class PrintJobMetadata
    {
        public string PosGuid { get; set; }
        public int SpoolerId { get; set; }
        public string PrinterName { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string OrderId { get; set; }
        public string CustomerId { get; set; }
        public decimal Amount { get; set; }
        // Any other metadata you need
    }
    
    public int SubmitPrintJob(string printerName, string posGuid, 
        PrinterTask task, Dictionary<string, object> metadata)
    {
        // Use GUID as document name for easy tracking
        int spoolerId = PrintDirect.Print(printerName, posGuid, "RAW", 
            GeneratePrintData(task));
        
        // Store extended metadata
        var jobMetadata = new PrintJobMetadata
        {
            PosGuid = posGuid,
            SpoolerId = spoolerId,
            PrinterName = printerName,
            SubmittedAt = DateTime.UtcNow,
            OrderId = metadata["orderId"]?.ToString(),
            CustomerId = metadata["customerId"]?.ToString(),
            Amount = Convert.ToDecimal(metadata["amount"] ?? 0)
        };
        
        _spoolerIdToMetadata[spoolerId] = jobMetadata;
        _guidToMetadata[posGuid] = jobMetadata;
        
        return spoolerId;
    }
    
    public PrintJobMetadata GetMetadataByGuid(string posGuid)
    {
        return _guidToMetadata.TryGetValue(posGuid, out var metadata) 
            ? metadata : null;
    }
}
```

## Document Name Format Examples

### 1. Simple GUID
```
Document Name: "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"
```

### 2. Human Readable + GUID
```
Document Name: "Receipt-001|67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"
Document Name: "PRT-20250117-145030#67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f"
```

### 3. Structured Format
```
Document Name: "R:12345|G:67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f|T:1642435200"
// R = Receipt number, G = GUID, T = Timestamp
```

### 4. Base64 Encoded JSON (Complex Metadata)
```csharp
var metadata = new 
{
    guid = "67d8f94a-2b5e-4d3a-8c72-1a2b3c4d5e6f",
    orderId = "ORD-12345",
    amount = 25.99,
    items = 3
};

string json = JsonSerializer.Serialize(metadata);
string documentName = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
// Result: "eyJndWlkIjoiNjdkOGY5NGEtMmI1ZS00ZDNhLThjNzItMWEyYjNjNGQ1ZTZmIiwib3Jk..."
```

## Limitations

### Document Name Constraints
- **Maximum Length**: 256 characters (Windows limit)
- **Allowed Characters**: Alphanumeric, spaces, hyphens, underscores, periods
- **Avoid**: Special characters like `\/:*?"<>|` which may cause issues
- **Persistence**: Document name stays with job until it's removed from spooler
- **Visibility**: Users can see document name in print queue

### What You CANNOT Store in Spooler
- Binary data (must be text in document name)
- Large metadata (256 char limit)
- Secure/sensitive data (visible to users)
- Complex objects (unless serialized to fit)

## Best Practices

1. **Use Consistent Format**: Pick one format and stick with it
   ```csharp
   const string DOCUMENT_NAME_FORMAT = "{0}#{1}"; // DisplayName#GUID
   ```

2. **Validate GUIDs**: Always validate before using
   ```csharp
   if (!Guid.TryParse(extractedGuid, out _))
   {
       // Invalid GUID, handle error
   }
   ```

3. **Handle Missing GUIDs**: Not all jobs may have GUIDs
   ```csharp
   string guid = ExtractGuid(job.DocumentName);
   if (string.IsNullOrEmpty(guid))
   {
       // Legacy job or manual print, skip tracking
   }
   ```

4. **Clean Document Names**: Remove invalid characters
   ```csharp
   string SafeDocumentName(string input)
   {
       return Regex.Replace(input, @"[\\/:*?""<>|]", "");
   }
   ```

## Implementation Example

### Complete Enhanced Printer Service
```csharp
public class GuidAwarePrinterService
{
    // Submit print with GUID tracking
    public PrintResult PrintWithTracking(PrintRequest request)
    {
        // Check for duplicate in spooler
        var existingJob = ScanSpoolerForGuid(request.PosGuid);
        if (existingJob != null)
        {
            return new PrintResult
            {
                Success = false,
                Reason = "Duplicate",
                ExistingJobId = existingJob.JobId
            };
        }
        
        // Format document name with GUID
        string documentName = FormatDocumentName(request.PosGuid, request.DisplayName);
        
        // Submit to spooler
        int spoolerId = PrintDirect.Print(
            request.PrinterName,
            documentName,
            "RAW",
            request.PrintData
        );
        
        return new PrintResult
        {
            Success = true,
            SpoolerId = spoolerId,
            DocumentName = documentName,
            Guid = request.PosGuid
        };
    }
    
    // Scan spooler for GUID
    private PrintJobInfo ScanSpoolerForGuid(string posGuid)
    {
        foreach (var printer in GetAllPrinters())
        {
            IntPtr hPrinter;
            if (!OpenPrinter(printer, out hPrinter, IntPtr.Zero))
                continue;
            
            try
            {
                var jobs = EnumJobs(hPrinter);
                foreach (var job in jobs)
                {
                    string extractedGuid = ExtractGuidFromDocumentName(job.DocumentName);
                    if (extractedGuid == posGuid)
                    {
                        return job;
                    }
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
        
        return null;
    }
    
    // Format document name with GUID
    private string FormatDocumentName(string guid, string displayName)
    {
        // Use pipe separator format: DisplayName|GUID
        string formatted = $"{displayName}|{guid}";
        
        // Ensure within 256 char limit
        if (formatted.Length > 256)
        {
            // Truncate display name to fit
            int maxDisplay = 256 - guid.Length - 1;
            displayName = displayName.Substring(0, maxDisplay);
            formatted = $"{displayName}|{guid}";
        }
        
        return formatted;
    }
    
    // Extract GUID from document name
    private string ExtractGuidFromDocumentName(string documentName)
    {
        if (string.IsNullOrEmpty(documentName))
            return null;
        
        // Look for pipe separator
        int pipeIndex = documentName.LastIndexOf('|');
        if (pipeIndex > 0 && pipeIndex < documentName.Length - 1)
        {
            string possibleGuid = documentName.Substring(pipeIndex + 1);
            if (Guid.TryParse(possibleGuid, out _))
            {
                return possibleGuid;
            }
        }
        
        // Check if entire name is GUID
        if (Guid.TryParse(documentName, out _))
        {
            return documentName;
        }
        
        return null;
    }
}
```

## Summary

The **Document Name field** in the Windows print spooler is the best and most reliable place to store your POS GUID. It's:

1. **Always Available**: Every print job has a document name
2. **Persistent**: Stays with the job throughout its lifecycle
3. **Searchable**: Can enumerate all jobs and check document names
4. **Simple**: No complex API calls or driver-specific features
5. **Compatible**: Works with all printers and drivers

Recommended format: `DisplayName|GUID` or `DisplayName#GUID`

This allows you to:
- Prevent duplicate prints by checking if GUID exists in spooler
- Track jobs through their lifecycle
- Maintain human-readable names while embedding tracking data
- Work within Windows spooler limitations