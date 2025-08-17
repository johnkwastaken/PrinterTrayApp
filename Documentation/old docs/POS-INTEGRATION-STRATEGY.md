# 🔗 POS Integration Strategy

## Overview
This document outlines the recommended integration strategy between the POS system and the Windows Printer Tray App for optimal reliability, performance, and scalability.

## Current Architecture Analysis

### ✅ Current State
- **Protocol**: HTTP REST API on `127.0.0.1:9877`
- **Communication**: Synchronous request/response
- **Data Format**: JSON with PrinterTask structure
- **Data Source**: All print data contained in `templateData` JSON string
- **Retry Logic**: Built-in retry with cash drawer safety (max 3 attempts)
- **Job Tracking**: Human-readable job numbers (`PRT-YYYYMMDD-######`)

### 🚨 Current Limitations
1. **Blocking Operations**: POS waits for print completion
2. **No Job Persistence**: Jobs lost if app restarts
3. **Limited Status Visibility**: No real-time status updates
4. **Single Job Processing**: One print job at a time
5. **No Failure Recovery**: Failed jobs require manual intervention
6. **Basic Error Handling**: Limited error classification

## 🚀 Recommended Integration Improvements

### 1. Enhanced REST API Design

#### New Endpoints
```http
# Async Operations
POST /print/async              # Non-blocking print submission
GET /jobs/{jobNumber}          # Check individual job status
GET /jobs                      # List all jobs with filters
DELETE /jobs/{jobNumber}       # Cancel pending job

# Bulk Query Operations
POST /jobs/bulk-status         # Check status of multiple jobs at once
GET /jobs/since/{timestamp}    # Get all job updates since timestamp
GET /jobs/missed-webhooks      # Find jobs with failed webhook deliveries

# Batch Operations  
POST /print/batch              # Submit multiple jobs at once
GET /jobs/batch/{batchId}      # Check batch status

# Printer Management
GET /printers/{id}/status      # Real-time printer status
POST /printers/discover        # Scan for new printers
POST /printers/map             # Map logical names to physical printers
GET /printers/health           # Comprehensive diagnostics

# Webhook Management
POST /webhooks/register        # Register POS callback URLs
DELETE /webhooks/{id}          # Unregister webhooks
GET /webhooks                  # List registered webhooks

# System
GET /system/metrics            # Performance and usage metrics
GET /system/logs               # Recent system logs
```

#### Enhanced Response Format
```json
{
  "success": true,
  "jobNumber": "PRT-20250117-123456",
  "status": "queued|printing|completed|failed",
  "estimatedCompletion": "2025-01-17T14:31:00Z",
  "statusUrl": "/jobs/PRT-20250117-123456",
  "printerAssigned": "kitchen-thermal-01",
  "queuePosition": 2,
  "retryCount": 0,
  "error": null
}
```

### 2. Job Queue System

#### Database Schema
```sql
CREATE TABLE print_jobs (
    job_number VARCHAR(20) PRIMARY KEY,
    status VARCHAR(20) NOT NULL DEFAULT 'queued',
    priority INTEGER DEFAULT 5,
    printer_name VARCHAR(100),
    template_data TEXT NOT NULL,
    template_body TEXT NOT NULL,
    cash_drawer BOOLEAN DEFAULT FALSE,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    started_at DATETIME NULL,
    completed_at DATETIME NULL,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    spooler_job_id INTEGER NULL,
    retry_count INTEGER DEFAULT 0,
    error_message TEXT NULL,
    webhook_url VARCHAR(500) NULL
);

CREATE TABLE webhook_deliveries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    job_number VARCHAR(20) NOT NULL,
    webhook_url VARCHAR(500) NOT NULL,
    event_type VARCHAR(50) NOT NULL,
    payload TEXT NOT NULL,
    status VARCHAR(20) DEFAULT 'pending',
    attempt_count INTEGER DEFAULT 0,
    last_attempt DATETIME NULL,
    next_retry DATETIME NULL,
    response_code INTEGER NULL,
    error_message TEXT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (job_number) REFERENCES print_jobs (job_number)
);

CREATE TABLE print_batches (
    batch_id VARCHAR(20) PRIMARY KEY,
    total_jobs INTEGER NOT NULL,
    completed_jobs INTEGER DEFAULT 0,
    failed_jobs INTEGER DEFAULT 0,
    status VARCHAR(20) DEFAULT 'processing',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

#### Queue Processing Logic
- **Priority Queue**: Cash drawer jobs, rush orders first
- **Retry Logic**: Exponential backoff with configurable limits
- **Dead Letter Queue**: Failed jobs for manual intervention
- **Batch Processing**: Optimize for multiple receipts
- **Resource Management**: Prevent printer overload

### 3. Real-time Status Communication

#### Webhook System
```csharp
// POS registers for status updates
POST /webhooks/register
{
  "url": "https://pos.example.com/print-status",
  "events": ["job.completed", "job.failed", "printer.offline", "job.started"],
  "secret": "webhook-secret-key",
  "timeout": 5000
}

// App sends status updates to POS
POST https://pos.example.com/print-status
{
  "event": "job.completed",
  "jobNumber": "PRT-20250117-123456",
  "status": "completed",
  "printerName": "kitchen-thermal-01",
  "timestamp": "2025-01-17T14:30:00Z",
  "spoolerJobId": 42,
  "processingTime": "2.3s",
  "signature": "sha256-hash-of-payload"
}
```

#### Server-Sent Events (Alternative)
```http
GET /events/stream
Accept: text/event-stream

# Response stream
data: {"event":"job.completed","jobNumber":"PRT-20250117-123456"}
data: {"event":"printer.offline","printerName":"kitchen-thermal-01"}
```

### 4. Enhanced Printer Management

#### Printer Discovery
```json
{
  "discoveredPrinters": [
    {
      "logicalName": "kitchen-thermal-01",
      "windowsPrinterName": "EPSON TM-T88V",
      "ipAddress": "192.168.1.100",
      "location": "Kitchen Station 1",
      "capabilities": ["raw", "graphics", "cashdrawer"],
      "status": "online",
      "lastSeen": "2025-01-17T14:30:00Z"
    }
  ],
  "mappingRecommendations": [
    {
      "logicalName": "kitchen",
      "suggestedPrinter": "kitchen-thermal-01",
      "confidence": 0.95,
      "reason": "Location match and capabilities"
    }
  ]
}
```

#### Health Monitoring
```json
{
  "printerName": "kitchen-thermal-01",
  "status": "online|offline|warning|error",
  "paperLevel": "ok|low|empty",
  "temperature": "normal|hot|overheating",
  "jobsInQueue": 3,
  "lastJobTime": "2025-01-17T14:25:00Z",
  "errorHistory": [
    {
      "timestamp": "2025-01-17T14:20:00Z",
      "error": "Paper jam detected",
      "resolved": true
    }
  ]
}
```

## 📋 Implementation Roadmap

### Phase 1: Foundation (Week 1-2)
**Goal**: Improve reliability without breaking existing integration

#### Tasks:
1. **Job Persistence**
   - Implement SQLite database
   - Create job tracking tables
   - Add job status management

2. **Async API**
   - Add `/print/async` endpoint
   - Implement background job processing
   - Create `/jobs/{jobNumber}` status endpoint

3. **Enhanced Error Handling**
   - Detailed error codes and messages
   - Retry configuration
   - Dead letter queue for failed jobs

#### POS Changes Required:
- **Minimal**: Can continue using existing `/print` endpoint
- **Optional**: Implement polling of `/jobs/{jobNumber}` for status

### Phase 2: Real-time Integration (Week 3-4)
**Goal**: Enable real-time status updates and batch processing

#### Tasks:
1. **Webhook System**
   - Webhook registration endpoint
   - Status notification delivery
   - Webhook security (signatures)

2. **Batch Processing**
   - `/print/batch` endpoint
   - Batch status tracking
   - Optimized queue processing

3. **Printer Health Monitoring**
   - Real-time printer status
   - Proactive issue detection
   - Health metrics collection

#### POS Changes Required:
- **Webhook Endpoint**: Implement endpoint to receive status updates
- **Batch Support**: Optional batch submission for high-volume scenarios

### Phase 3: Advanced Features (Week 5-8)
**Goal**: Production-ready enterprise features

#### Tasks:
1. **Print Preview**
   - Generate preview images
   - Template validation
   - Print cost estimation

2. **Analytics & Monitoring**
   - Success/failure rates
   - Performance metrics
   - Usage analytics

3. **Multi-location Support**
   - Store/location routing
   - Printer pools
   - Load balancing

4. **Security & Authentication**
   - API key authentication
   - Role-based access
   - Audit logging

#### POS Changes Required:
- **Authentication**: Add API keys to requests
- **Location Routing**: Include store/location identifiers

## 🔧 Implementation Examples

### Bulk Status Query Implementation

```csharp
[HttpPost("/jobs/bulk-status")]
public async Task<ActionResult> GetBulkJobStatus([FromBody] BulkStatusRequest request)
{
    try
    {
        if (request.JobNumbers?.Length > 100)
            return BadRequest(new { error = "Maximum 100 jobs per request" });
        
        var jobs = await _jobService.GetJobsAsync(request.JobNumbers);
        
        var results = jobs.Select(job => new
        {
            jobNumber = job.JobNumber,
            status = job.Status.ToString().ToLower(),
            updatedAt = job.UpdatedAt,
            completedAt = job.CompletedAt,
            error = job.ErrorMessage,
            webhookDelivered = job.WebhookDelivered
        }).ToArray();
        
        return Ok(new
        {
            jobs = results,
            requestedCount = request.JobNumbers?.Length ?? 0,
            foundCount = results.Length
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting bulk job status");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpGet("/jobs/since/{timestamp}")]
public async Task<ActionResult> GetJobsSince(DateTime timestamp, [FromQuery] int limit = 100)
{
    try
    {
        var jobs = await _jobService.GetJobsUpdatedSinceAsync(timestamp, limit);
        
        var results = jobs.Select(job => new
        {
            jobNumber = job.JobNumber,
            status = job.Status.ToString().ToLower(),
            updatedAt = job.UpdatedAt,
            completedAt = job.CompletedAt,
            webhookDelivered = job.WebhookDelivered,
            changes = GetJobChanges(job, timestamp)
        }).ToArray();
        
        return Ok(new
        {
            jobs = results,
            since = timestamp,
            hasMore = jobs.Count == limit
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting jobs since timestamp");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpGet("/jobs/missed-webhooks")]
public async Task<ActionResult> GetMissedWebhooks([FromQuery] DateTime? since = null)
{
    try
    {
        var cutoff = since ?? DateTime.UtcNow.AddHours(-24);
        var missedJobs = await _jobService.GetJobsWithFailedWebhooksAsync(cutoff);
        
        var results = missedJobs.Select(job => new
        {
            jobNumber = job.JobNumber,
            status = job.Status.ToString().ToLower(),
            completedAt = job.CompletedAt,
            webhookUrl = job.WebhookUrl,
            lastWebhookAttempt = job.LastWebhookAttempt,
            webhookError = job.WebhookError
        }).ToArray();
        
        return Ok(new
        {
            missedWebhooks = results,
            since = cutoff,
            count = results.Length
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting missed webhooks");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

public class BulkStatusRequest
{
    public string[]? JobNumbers { get; set; }
    public DateTime? Since { get; set; }
    public string[]? Statuses { get; set; }
}
```

### Webhook Recovery System

```csharp
public class WebhookDeliveryService : BackgroundService
{
    private readonly IWebhookRepository _webhookRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDeliveryService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingWebhooks();
                await ProcessFailedWebhooks();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook delivery service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
    
    private async Task ProcessPendingWebhooks()
    {
        var pendingWebhooks = await _webhookRepo.GetPendingWebhooksAsync();
        
        foreach (var webhook in pendingWebhooks)
        {
            await DeliverWebhookAsync(webhook);
        }
    }
    
    private async Task ProcessFailedWebhooks()
    {
        var retryableWebhooks = await _webhookRepo.GetRetryableWebhooksAsync();
        
        foreach (var webhook in retryableWebhooks)
        {
            if (DateTime.UtcNow >= webhook.NextRetry)
            {
                await DeliverWebhookAsync(webhook);
            }
        }
    }
    
    private async Task DeliverWebhookAsync(WebhookDelivery webhook)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("webhook");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var request = new HttpRequestMessage(HttpMethod.Post, webhook.WebhookUrl)
            {
                Content = new StringContent(webhook.Payload, Encoding.UTF8, "application/json")
            };
            
            // Add security headers
            request.Headers.Add("X-Webhook-Event", webhook.EventType);
            request.Headers.Add("X-Webhook-Delivery", webhook.Id.ToString());
            request.Headers.Add("X-Webhook-Signature", GenerateSignature(webhook.Payload));
            
            var response = await httpClient.SendAsync(request);
            
            webhook.AttemptCount++;
            webhook.LastAttempt = DateTime.UtcNow;
            webhook.ResponseCode = (int)response.StatusCode;
            
            if (response.IsSuccessStatusCode)
            {
                webhook.Status = "delivered";
                _logger.LogInformation("Webhook delivered successfully: {WebhookId}", webhook.Id);
            }
            else
            {
                webhook.Status = "failed";
                webhook.ErrorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                webhook.NextRetry = CalculateNextRetry(webhook.AttemptCount);
                
                _logger.LogWarning("Webhook delivery failed: {WebhookId}, Status: {StatusCode}", 
                    webhook.Id, response.StatusCode);
            }
            
            await _webhookRepo.UpdateWebhookAsync(webhook);
        }
        catch (Exception ex)
        {
            webhook.AttemptCount++;
            webhook.LastAttempt = DateTime.UtcNow;
            webhook.Status = "failed";
            webhook.ErrorMessage = ex.Message;
            webhook.NextRetry = CalculateNextRetry(webhook.AttemptCount);
            
            await _webhookRepo.UpdateWebhookAsync(webhook);
            
            _logger.LogError(ex, "Exception delivering webhook: {WebhookId}", webhook.Id);
        }
    }
    
    private DateTime? CalculateNextRetry(int attemptCount)
    {
        if (attemptCount >= 5) return null; // Give up after 5 attempts
        
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptCount) * 30); // Exponential backoff
        return DateTime.UtcNow.Add(delay);
    }
}
```

### POS Recovery Pattern

```csharp
// POS-side implementation for handling missed webhooks
public class PrintJobStatusService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PrintJobStatusService> _logger;
    private DateTime _lastSyncTime;
    
    public async Task SyncMissedUpdatesAsync()
    {
        try
        {
            // Get all updates since last sync
            var response = await _httpClient.GetAsync($"/jobs/since/{_lastSyncTime:O}");
            var result = await response.Content.ReadFromJsonAsync<JobUpdateResponse>();
            
            foreach (var job in result.Jobs)
            {
                await ProcessJobUpdateAsync(job);
            }
            
            // Check for specifically missed webhooks
            var missedResponse = await _httpClient.GetAsync($"/jobs/missed-webhooks?since={_lastSyncTime:O}");
            var missedResult = await missedResponse.Content.ReadFromJsonAsync<MissedWebhookResponse>();
            
            foreach (var missedJob in missedResult.MissedWebhooks)
            {
                _logger.LogWarning("Processing missed webhook for job: {JobNumber}", missedJob.JobNumber);
                await ProcessJobUpdateAsync(missedJob);
            }
            
            _lastSyncTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing missed updates");
        }
    }
    
    public async Task<JobStatusResult[]> CheckMultipleJobsAsync(string[] jobNumbers)
    {
        try
        {
            var request = new BulkStatusRequest { JobNumbers = jobNumbers };
            var response = await _httpClient.PostAsJsonAsync("/jobs/bulk-status", request);
            var result = await response.Content.ReadFromJsonAsync<BulkStatusResponse>();
            
            return result.Jobs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking multiple job statuses");
            return Array.Empty<JobStatusResult>();
        }
    }
    
    // Periodic sync to catch any missed webhooks
    public async Task StartPeriodicSyncAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await SyncMissedUpdatesAsync();
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
        }
    }
}
```

### Async Print Implementation

```csharp
[HttpPost("/print/async")]
public async Task<ActionResult> PrintAsync([FromBody] PrinterTask task)
{
    try
    {
        var jobNumber = GenerateJobNumber();
        
        // Validate request
        if (task.template?.body == null)
            return BadRequest(new { error = "Template is required" });
        
        // Create job record
        var job = new PrintJob
        {
            JobNumber = jobNumber,
            Status = JobStatus.Queued,
            TemplateData = task.templateData,
            TemplateBody = task.template.body,
            CashDrawer = task.isOpenCashDrawer,
            Priority = task.isOpenCashDrawer ? 1 : 5, // Higher priority for cash drawer
            CreatedAt = DateTime.UtcNow
        };
        
        await _jobService.CreateJobAsync(job);
        
        // Queue for background processing
        _backgroundQueue.EnqueueJob(jobNumber);
        
        // Return immediate response
        return Ok(new
        {
            success = true,
            jobNumber = jobNumber,
            status = "queued",
            statusUrl = $"/jobs/{jobNumber}",
            estimatedCompletion = DateTime.UtcNow.AddSeconds(30)
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error submitting async print job");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpGet("/jobs/{jobNumber}")]
public async Task<ActionResult> GetJobStatus(string jobNumber)
{
    var job = await _jobService.GetJobAsync(jobNumber);
    
    if (job == null)
        return NotFound(new { error = "Job not found" });
    
    return Ok(new
    {
        jobNumber = job.JobNumber,
        status = job.Status.ToString().ToLower(),
        printerAssigned = job.PrinterName,
        createdAt = job.CreatedAt,
        startedAt = job.StartedAt,
        completedAt = job.CompletedAt,
        spoolerJobId = job.SpoolerJobId,
        retryCount = job.RetryCount,
        error = job.ErrorMessage
    });
}
```

### Background Job Processor

```csharp
public class BackgroundJobProcessor : BackgroundService
{
    private readonly IJobService _jobService;
    private readonly PrinterService _printerService;
    private readonly IWebhookService _webhookService;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobQueue.DequeueAsync(stoppingToken);
                await ProcessJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing background job");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
    
    private async Task ProcessJobAsync(PrintJob job)
    {
        try
        {
            // Update status
            job.Status = JobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await _jobService.UpdateJobAsync(job);
            
            // Send webhook notification
            await _webhookService.NotifyAsync("job.started", job);
            
            // Process the print job
            var result = await ProcessPrintTask(job);
            
            // Update final status
            job.Status = result.Success ? JobStatus.Completed : JobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.SpoolerJobId = result.SpoolerJobId;
            job.ErrorMessage = result.Error;
            
            await _jobService.UpdateJobAsync(job);
            
            // Send completion notification
            var eventName = result.Success ? "job.completed" : "job.failed";
            await _webhookService.NotifyAsync(eventName, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobNumber}", job.JobNumber);
            
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.RetryCount++;
            
            await _jobService.UpdateJobAsync(job);
            
            // Retry logic
            if (job.RetryCount < GetMaxRetries(job))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, job.RetryCount));
                _jobQueue.EnqueueDelayed(job.JobNumber, delay);
            }
            else
            {
                await _webhookService.NotifyAsync("job.failed", job);
            }
        }
    }
}
```

## 🔒 Security Considerations

### API Authentication
```csharp
[ApiKey]
[HttpPost("/print/async")]
public async Task<ActionResult> PrintAsync([FromBody] PrinterTask task)
{
    // Implementation
}

public class ApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var apiKey = context.HttpContext.Request.Headers["X-API-Key"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(apiKey) || !IsValidApiKey(apiKey))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
```

### Webhook Security
```csharp
public class WebhookService
{
    public async Task NotifyAsync(string eventName, PrintJob job)
    {
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            jobNumber = job.JobNumber,
            timestamp = DateTime.UtcNow,
            data = job
        });
        
        var signature = GenerateSignature(payload, webhook.Secret);
        
        var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        
        request.Headers.Add("X-Webhook-Signature", signature);
        request.Headers.Add("X-Webhook-Event", eventName);
        
        await _httpClient.SendAsync(request);
    }
}
```

## 📊 Success Metrics

### Performance Targets
- **Job Submission**: < 100ms response time
- **Print Processing**: < 5 seconds average
- **Webhook Delivery**: < 1 second
- **System Uptime**: > 99.5%

### Reliability Targets
- **Print Success Rate**: > 98%
- **Webhook Delivery Success**: > 95%
- **Recovery Time**: < 30 seconds after printer comes online

### Monitoring Dashboard
- Real-time job queue status
- Printer health indicators
- Success/failure rates
- Performance metrics
- Error trends

## 🚀 Getting Started

## 🔄 Webhook Recovery & Bulk Query Features

### Key Features Added:

#### 1. **Bulk Status Queries**
- `POST /jobs/bulk-status` - Check up to 100 jobs at once
- `GET /jobs/since/{timestamp}` - Get all job updates since a specific time
- `GET /jobs/missed-webhooks` - Find jobs where webhook delivery failed

#### 2. **Webhook Delivery Tracking**
- `webhook_deliveries` table tracks every webhook attempt
- Automatic retry with exponential backoff (up to 5 attempts)
- Background service processes failed webhook deliveries
- POS can query for missed webhooks and sync manually

#### 3. **Recovery Patterns**
```csharp
// POS can periodically sync missed updates
await posService.SyncMissedUpdatesAsync();

// Or check multiple jobs at once
var statuses = await posService.CheckMultipleJobsAsync(jobNumbers);
```

### For POS Developers

1. **Start Simple**: Continue using existing `/print` endpoint
2. **Add Polling**: Implement `/jobs/{jobNumber}` status checking
3. **Implement Webhooks**: Create endpoint to receive status updates
4. **Add Bulk Recovery**: Use bulk endpoints to catch missed webhooks
5. **Enable Batching**: Use `/print/batch` for high-volume scenarios

### For Printer App

1. **Phase 1**: Implement job persistence and async endpoints
2. **Phase 2**: Add webhook system and batch processing
3. **Phase 3**: Build advanced features and monitoring

## 📝 Configuration Example

```json
{
  "PrinterTrayApp": {
    "Api": {
      "Port": 9877,
      "ApiKeys": ["pos-system-key-123", "admin-key-456"],
      "MaxConcurrentJobs": 10,
      "DefaultJobTimeout": "5m"
    },
    "Queue": {
      "MaxRetries": 3,
      "RetryDelay": "2s",
      "BatchSize": 5,
      "ProcessingInterval": "1s"
    },
    "Webhooks": {
      "Timeout": "5s",
      "MaxRetries": 3,
      "RetryInterval": "30s"
    },
    "Database": {
      "ConnectionString": "Data Source=jobs.db",
      "RetentionDays": 30
    }
  }
}
```

---
**Document Version**: 1.0  
**Last Updated**: 2025-01-17  
**Status**: Proposed Implementation Strategy