using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace PrinterTrayApp;

/// <summary>
/// HTTP Server that receives print jobs from POS system and sends them to thermal printers
/// 
/// FILE PURPOSE:
/// - Provides REST API endpoints for the POS system to communicate with
/// - Receives PrinterTask JSON objects containing templates and data
/// - Orchestrates the entire print pipeline from JSON to physical output
/// - Handles printer validation, error recovery, and retry logic
/// 
/// MAIN ENDPOINTS:
/// - POST /print: Receives and processes print jobs
/// - GET /health: Returns server and printer status
/// - GET /printers: Returns list of available printers
/// 
/// DATA FLOW:
/// 1. POS sends PrinterTask JSON to /print endpoint
/// 2. Server extracts templateData (contains ALL print data as JSON string)
/// 3. Cleans MongoDB types and applies POS business rules
/// 4. Renders XML template with data (replaces {{tokens}})
/// 5. Converts XML to ESC/POS printer commands
/// 6. Sends commands to physical printer via Windows spooler
/// 
/// IMPORTANT: Printer names from request body fields, template data from templateData for rendering
/// </summary>
public class HttpServer
{
    private WebApplication? _app;
    private readonly DateTime _startTime;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly PrinterService _printerService;

    public HttpServer(PrinterService printerService)
    {
        _printerService = printerService;
        _startTime = DateTime.UtcNow;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Add converters for POS compatibility
        _jsonOptions.Converters.Add(new MongoDateConverter());
        _jsonOptions.Converters.Add(new FlexibleBooleanConverter());
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();

        // Minimize logging overhead
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            // Remove console logging to reduce overhead
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        // Configure Kestrel with minimal resources
        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(Constants.ApiPort);
            // Limit connections for a local-only API
            options.Limits.MaxConcurrentConnections = 10;
            options.Limits.MaxConcurrentUpgradedConnections = 10;
            // Reduce thread usage
            options.Limits.MaxRequestBodySize = 2 * 1024 * 1024; // 2MB max
        });

        // Configure minimal thread pool
        builder.WebHost.ConfigureServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true; // Reduce async overhead for simple API
            });
        });

        _app = builder.Build();

        _app.UseRouting();

        _app.MapGet("/health", GetHealth);

        _app.MapPost("/print", Print);

        _app.MapGet("/self-test", GetSelfTest);

        _app.MapGet("/printers", GetPrinters);

        _app.MapGet("/status", GetStatus);

        _app.MapGet("/status/guid/{guid}", GetStatusByGuid);

        _app.MapGet("/status/spooler/{id}", GetStatusBySpooler);

        _app.MapGet("/check-queue", GetQueue);

        _app.MapPost("/retry-queue", RetryQueue);

        _app.MapGet("/", GetRoot);

        await _app.StartAsync(cancellationToken);

        ConsoleWindow.WriteLine($"HTTP Server started on http://127.0.0.1:{Constants.ApiPort}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }

    // Private methods

    /// <summary>
    /// Main print task processing pipeline - receives PrinterTask from POS and sends to printer
    /// This is the core function that orchestrates the entire printing process
    /// 
    /// PIPELINE STAGES:
    /// 1. Validate template exists
    /// 2. Parse templateData (JSON string containing all print data)
    /// 3. Render XML template (replace {{tokens}} with actual values)
    /// 4. Convert XML to ESC/POS commands
    /// 5. Send commands to printer with retry logic
    /// 
    /// Flow: JSON Data -> Clean MongoDB Types -> Apply POS Rules -> Render Template -> Generate Commands -> Send to Printer
    /// </summary>
    /// <param name="task">PrinterTask containing template and templateData for rendering</param>
    /// <param name="printerName">Name of the printer to use (already validated)</param>
    /// <returns>PrintResult with success status and any error messages</returns>
    private async Task<PrintResult> ProcessPrintTask(PrinterTask task, string printerName)
    {
        var result = new PrintResult();

        try
        {
            // STEP 0: Extract GUID from PrinterTask
            var guid = task._id?.Id ?? $"job-{DateTime.Now:yyyyMMddHHmmss}";
            result.Guid = guid;
            result.DocumentName = guid;

            ConsoleWindow.WriteLine($"Processing PrinterTask with GUID: {guid}");

            // STEP 1: Validate template exists
            if (task.Template == null || string.IsNullOrEmpty(task.Template.Body))
            {
                throw new Exception("Template is missing or empty");
            }

            ConsoleWindow.WriteLine($"Processing PrinterTask with GUID: {guid}...");

            // STEP 2: Process the templateData field
            // IMPORTANT: templateData contains template rendering data - JSON string with:
            // - sites (store info)
            // - orders (order details, totals, items)
            // - staff (employee info)
            // - registers (register/terminal info)
            // - currencies (currency formatting)
            // - dateOfPrinting, appVersion, etc.
            // The template uses {{path.to.value}} tokens to reference this data

            // ============================================================
            // STEP 2a: Parse templateData (JSON string from POS)
            // ============================================================
            // The POS sends templateData as a JSON STRING, not an object
            // It contains template rendering data:
            // - sites: Store information
            // - orders: Order details with products
            // - staff, registers, currencies: Context data
            // - dateOfPrinting, header, footer: Print metadata
            string processedTemplateData = task.TemplateData ?? "{}";

            // Log the type and content we received for debugging
            DebugLogger.Log($"[POS-FIX] templateData is string: {!string.IsNullOrEmpty(processedTemplateData)}");
            DebugLogger.Log($"[POS-FIX] templateData length: {processedTemplateData.Length}");

            // Verify it's valid JSON by attempting to parse it
            try
            {
                var testParse = System.Text.Json.JsonDocument.Parse(processedTemplateData);
                DebugLogger.Log($"[POS-FIX] templateData is valid JSON with root element: {testParse.RootElement.ValueKind}");
                testParse.Dispose();
            }
            catch (Exception parseEx)
            {
                DebugLogger.Log($"[POS-FIX] WARNING: templateData is not valid JSON: {parseEx.Message}");
            }

            // ============================================================
            // STEP 3: Render XML Template with Data
            // ============================================================
            // The template contains XML with {{tokens}} placeholders
            // TemplateHelpers.RenderTemplate does the heavy lifting:
            // - Parses the XML structure
            // - Replaces {{tokens}} with values from templateData
            // - Processes special sections like <docket-section>
            // - Handles product hierarchies with proper formatting
            // Example transformations:
            //   {{sites.name}} -> "Palmerston North"
            //   {{orders.docNumber}} -> "005868"
            //   <docket-section /> -> Full product list with categories
            DebugLogger.Log($"[HttpServer] About to render template: {task.Template.Name}");

            XmlDocument xmlDoc;
            try
            {
                // TODO: Ask John - RenderTemplateSimple exists but wasn't used (useSimpleProcessor=false)
                // Should we keep SimpleTemplateProcessor or remove it entirely?
                DebugLogger.Log($"[HttpServer] Using TemplateHelpers.RenderTemplate");
                xmlDoc = TemplateHelpers.RenderTemplate(
                    task.Template.Body,        // XML template with {{tokens}} and print commands
                    processedTemplateData,     // JSON string with all the data
                    PrinterPaperWidth.Paper_80 // Standard 80mm thermal printer width
                );

                DebugLogger.Log($"[HttpServer] Template rendered successfully");

                // Debug: Log the rendered XML to see what we're sending
                DebugLogger.Log($"[HttpServer] Rendered XML:\n{xmlDoc.OuterXml}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"[HttpServer] RenderTemplate failed", ex);
                throw;
            }

            // ============================================================
            // STEP 4: Convert XML to ESC/POS Commands
            // ============================================================
            // ESC/POS (Epson Standard Code for Point of Sale) is the
            // industry standard protocol for thermal receipt printers
            // CommandBuilder converts each XML element to printer commands:
            // - <text> -> Text with formatting (size, alignment, style)
            // - <separator> -> Line of characters
            // - <blank> -> Empty lines
            // - <command> -> Special commands (cut, drawer, beep)
            ConsoleWindow.WriteLine("Building ESC/POS commands...");

            var commandBuilder = new CommandBuilder(PrinterPaperWidth.Paper_80);

            // Add cash drawer opening command if requested
            // Most thermal printers have a cash drawer port (DK port)
            // This sends an electrical pulse to trigger the drawer solenoid
            if (task.IsOpenCashDrawer)
            {
                ConsoleWindow.WriteLine("Adding cash drawer command");
                commandBuilder.OpenCashDrawer(PrinterPulse.Duration_100);  // 100ms pulse
            }

            // Process the XML document and generate ESC/POS commands
            // Converts <text>, <separator>, <command> etc. to printer control codes
            commandBuilder.ProcessXmlDocument(xmlDoc);

            // Get the final string of commands to send to printer
            var commands = commandBuilder.Build();

            // ============================================================
            // STEP 5: Send Commands to Physical Printer
            // ============================================================
            ConsoleWindow.WriteLine($"Sending {commands.Length} bytes to printer...");

            // Printer name already validated and passed as parameter
            ConsoleWindow.WriteLine($"Using validated printer: {printerName}");
            result.PrinterName = printerName;

            // ============================================================
            // DUPLICATE PREVENTION: Check if GUID already exists in spooler
            // ============================================================
            ConsoleWindow.WriteLine($"Checking for duplicate GUID: {guid}");
            try
            {
                var existingJobs = PrintDirect.GetPrinterJobs(printerName);
                var duplicateJob = existingJobs.FirstOrDefault(j =>
                    j.DocumentName?.Equals(guid, StringComparison.OrdinalIgnoreCase) == true);

                if (duplicateJob != null)
                {
                    ConsoleWindow.WriteLine($"Duplicate GUID detected! Job {duplicateJob.JobId} already exists with GUID: {guid}");
                    result.Success = false;
                    result.Error = $"Duplicate print job detected. GUID '{guid}' already exists in spooler.";
                    result.SpoolerJobId = duplicateJob.JobId;
                    return result;
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Warning: Could not check for duplicates: {ex.Message}");
                // Continue with printing - duplicate check is optional safety feature
            }

            // ============================================================
            // QUEUE MONITORING: Check for errors and printer issues
            // ============================================================
            try
            {
                // Check current printer's queue for errors
                var currentJobs = PrintDirect.GetPrinterJobs(printerName);
                result.HasErrors = currentJobs.Any(j =>
                    j.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                    j.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                    j.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                    j.Status.HasFlag(PrintDirect.JobStatus.UserIntervention));

                // Check all printers for issues
                var allPrinters = _printerService.GetAllPrinters();
                result.HasPrinterIssues = allPrinters.Any(p =>
                    !p.IsOnline ||
                    p.HasError ||
                    p.IsPaused ||
                    p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase) ||
                    p.Status.Contains("Offline", StringComparison.OrdinalIgnoreCase));

                if (result.HasErrors)
                {
                    ConsoleWindow.WriteLine($"Warning: Printer {printerName} has errored jobs in queue");
                }

                if (result.HasPrinterIssues)
                {
                    ConsoleWindow.WriteLine($"Warning: Some printers have issues");
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Warning: Could not check queue status: {ex.Message}");
                // Continue with printing - queue monitoring is optional
            }

            // ============================================================
            // Retry Logic with Cash Drawer Safety
            // ============================================================
            // Implements smart retry logic:
            // - Normal prints: Up to 5 retries
            // - Cash drawer prints: Limited to 3 retries
            // This prevents the cash drawer from opening multiple times
            // if there's a communication issue
            var maxRetries = task.IsOpenCashDrawer ? 3 : 5;
            var retryCount = 0;
            Exception? lastError = null;

            // Retry loop for handling temporary printer issues
            while (retryCount < maxRetries)
            {
                try
                {
                    var spoolJobId = PrintDirect.Print(
                        printerName,
                        guid,  // Use GUID as document name for tracking
                        "RAW",
                        commands
                    );

                    ConsoleWindow.WriteLine($"Print job sent successfully. Spooler ID: {spoolJobId}");

                    result.Success = true;
                    result.SpoolerJobId = spoolJobId;
                    result.RetryCount = retryCount;

                    await Task.Delay(100);

                    var jobs = PrintDirect.GetPrinterJobs(printerName);
                    var job = jobs.FirstOrDefault(j => j.JobId == spoolJobId);

                    if (job != null)
                    {
                        result.Status = job.Status.ToString();
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    retryCount++;
                    ConsoleWindow.WriteError($"Print attempt {retryCount} failed: {ex.Message}");

                    if (retryCount < maxRetries)
                    {
                        var delay = retryCount * 500;
                        ConsoleWindow.WriteLine($"Retrying in {delay}ms...");
                        await Task.Delay(delay);
                    }
                }
            }

            result.Success = false;
            result.Error = lastError?.Message ?? "Unknown error";
            result.RetryCount = retryCount;

            if (task.IsOpenCashDrawer && retryCount >= 3)
            {
                ConsoleWindow.WriteError("Cash drawer job failed after 3 attempts - aborting to prevent drawer issues");
                result.Error += " - Cash drawer safety limit reached";
            }

            return result;
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"ProcessPrintTask error: {ex.Message}");
            ConsoleWindow.WriteError($"Stack trace: {ex.StackTrace}");
            ConsoleWindow.WriteError($"Inner exception: {ex.InnerException?.Message}");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    // HTTP endpoint handlers
    private async Task GetHealth(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /health from {context.Connection.RemoteIpAddress}");

            var allPrinters = _printerService.GetAllPrinters();
            var hasPrinterIssues = HasPrinterIssues(allPrinters);
            var response = CreateHealthResponse(allPrinters.ToList(), hasPrinterIssues);

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /health endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetPrinters(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /printers from {context.Connection.RemoteIpAddress}");

            var printers = _printerService.GetAllPrinters();
            var hasPrinterIssues = HasPrinterIssues(printers);
            var response = CreatePrintersResponse(printers, hasPrinterIssues);

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /printers endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task Print(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"POST /print from {context.Connection.RemoteIpAddress}");

            // Read the JSON body from the HTTP request
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();

            ConsoleWindow.WriteLine($"Request body length: {body.Length} characters");
            if (body.Length < 100)
            {
                ConsoleWindow.WriteLine($"Request body preview: {body}");
            }
            else
            {
                ConsoleWindow.WriteLine($"Request body preview: {body.Substring(0, 100)}...");
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                ConsoleWindow.WriteError("Request body is empty");
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Request body cannot be empty" }, _jsonOptions));
                return;
            }

            PrinterTask? printerTask;
            try
            {
                printerTask = JsonSerializer.Deserialize<PrinterTask>(body, _jsonOptions);
            }
            catch (JsonException ex)
            {
                ConsoleWindow.WriteError($"JSON deserialization failed: {ex.Message}");
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = $"Invalid JSON format: {ex.Message}" }, _jsonOptions));
                return;
            }

            if (printerTask == null)
            {
                ConsoleWindow.WriteError("Deserialized PrinterTask is null");
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Failed to parse request body as PrinterTask" }, _jsonOptions));
                return;
            }

            ConsoleWindow.WriteLine($"Successfully deserialized PrinterTask with ID: {printerTask._id?.Id}");

            // Determine the target printer name from the request
            var targetPrinter = GetTargetPrinterName(printerTask);
            if (string.IsNullOrWhiteSpace(targetPrinter))
            {
                ConsoleWindow.WriteError("No target printer specified");
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Either printerDeviceName or printerName must be specified" }, _jsonOptions));
                return;
            }

            ConsoleWindow.WriteLine($"Target printer: {targetPrinter}");

            // Process the print task using the extracted printer name
            var result = await ProcessPrintTask(printerTask, targetPrinter);

            // Check if we should look for errors or printer issues in other jobs
            var hasErrors = false;
            var hasPrinterIssues = false;
            var errorDetails = new List<string>();

            try
            {
                // Get current status of all printers to check for issues
                var allPrinters = _printerService.GetAllPrinters();

                // Check if any printer has issues
                hasPrinterIssues = HasPrinterIssues(allPrinters);

                if (hasPrinterIssues)
                {
                    var problemPrinters = allPrinters
                        .Where(p => !p.IsOnline || p.HasError || p.IsPaused ||
                                   p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                   p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase) ||
                                   p.Status.Contains("Offline", StringComparison.OrdinalIgnoreCase))
                        .Select(p => $"{p.WindowsPrinterName}: {p.Status}")
                        .ToList();

                    errorDetails.AddRange(problemPrinters);
                    ConsoleWindow.WriteLine($"Found printer issues: {string.Join(", ", problemPrinters)}");
                }

                // Check for errors in job queues (only for other jobs, not the current one)
                var allPrinterNames = _printerService.GetPrinterNames();
                var jobsWithErrors = new List<string>();

                foreach (var printerName in allPrinterNames)
                {
                    try
                    {
                        var printerJobs = PrintDirect.GetPrinterJobs(printerName);
                        var errorJobs = printerJobs.Where(job =>
                            job.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                            job.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                            job.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                            job.Status.HasFlag(PrintDirect.JobStatus.UserIntervention)
                        ).ToList();

                        if (errorJobs.Any())
                        {
                            hasErrors = true;
                            jobsWithErrors.AddRange(errorJobs.Select(j => $"{printerName}:{j.JobId}({j.Status})"));
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleWindow.WriteError($"Failed to check jobs for printer {printerName}: {ex.Message}");
                    }
                }

                if (hasErrors)
                {
                    ConsoleWindow.WriteLine($"Found jobs with errors: {string.Join(", ", jobsWithErrors)}");
                }
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error checking printer/queue status: {ex.Message}");
                // Don't fail the main request, just log the error
            }

            // Prepare response
            var response = new PrintResult
            {
                Success = result.Success,
                SpoolerJobId = result.SpoolerJobId,
                Guid = printerTask._id?.Id,
                HasErrors = hasErrors,
                HasPrinterIssues = hasPrinterIssues,
                Error = result.Error
            };

            context.Response.ContentType = "application/json";
            if (result.Success)
            {
                ConsoleWindow.WriteLine($"Print task completed successfully. Spooler ID: {result.SpoolerJobId}");
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
            }
            else
            {
                ConsoleWindow.WriteError($"Print task failed: {result.Error}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
            }
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /print endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetSelfTest(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /self-test from {context.Connection.RemoteIpAddress}");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 501;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Not implemented yet" }, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /self-test endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetStatus(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /status from {context.Connection.RemoteIpAddress}");

            // Get all printers
            var allPrinters = _printerService.GetPrinterNames();
            var allJobs = new List<JobStatusInfo>();

            // Collect jobs from all printers
            foreach (var printerName in allPrinters)
            {
                try
                {
                    var printerJobs = PrintDirect.GetPrinterJobs(printerName);
                    foreach (var job in printerJobs)
                    {
                        allJobs.Add(new JobStatusInfo
                        {
                            Guid = job.DocumentName,
                            SpoolerId = job.JobId,
                            PrinterName = printerName,
                            Status = job.Status.ToString().ToLowerInvariant(),
                            IsError = job.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                                     job.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                                     job.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                                     job.Status.HasFlag(PrintDirect.JobStatus.UserIntervention),
                            IsPrinting = job.Status.HasFlag(PrintDirect.JobStatus.Printing),
                            IsComplete = job.Status.HasFlag(PrintDirect.JobStatus.Complete) ||
                                        job.Status.HasFlag(PrintDirect.JobStatus.Printed) ||
                                        job.Status.HasFlag(PrintDirect.JobStatus.Deleted)
                        });
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to get jobs for printer {printerName}: {ex.Message}");
                }
            }

            var response = new StatusResponse
            {
                TotalJobs = allJobs.Count,
                Jobs = allJobs
            };

            ConsoleWindow.WriteLine($"Status check completed: {allJobs.Count} total jobs across {allPrinters.Count} printers");

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /status endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetStatusByGuid(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /status/guid/{{guid}} from {context.Connection.RemoteIpAddress}");

            // Extract GUID from route parameter
            var guid = context.Request.RouteValues["guid"]?.ToString();

            if (string.IsNullOrWhiteSpace(guid))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "GUID parameter is required" }, _jsonOptions));
                return;
            }

            ConsoleWindow.WriteLine($"Searching for GUID: {guid}");

            // Search all printers for the GUID
            var allPrinters = _printerService.GetPrinterNames();
            GuidStatusResponse? result = null;

            foreach (var printerName in allPrinters)
            {
                try
                {
                    var printerJobs = PrintDirect.GetPrinterJobs(printerName);

                    // Search for job with matching GUID (case-insensitive)
                    var foundJob = printerJobs.FirstOrDefault(j =>
                        j.DocumentName?.Equals(guid, StringComparison.OrdinalIgnoreCase) == true);

                    if (foundJob != null)
                    {
                        // Create response for found job
                        result = new GuidStatusResponse
                        {
                            Guid = guid,
                            SpoolerId = foundJob.JobId,
                            Found = true,
                            PrinterName = printerName,
                            Status = foundJob.Status.ToString().ToLowerInvariant(),
                            IsError = foundJob.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.UserIntervention),
                            IsPrinting = foundJob.Status.HasFlag(PrintDirect.JobStatus.Printing),
                            IsPaused = foundJob.Status.HasFlag(PrintDirect.JobStatus.Paused),
                            IsComplete = foundJob.Status.HasFlag(PrintDirect.JobStatus.Complete) ||
                                        foundJob.Status.HasFlag(PrintDirect.JobStatus.Printed) ||
                                        foundJob.Status.HasFlag(PrintDirect.JobStatus.Deleted)
                        };

                        ConsoleWindow.WriteLine($"Found GUID {guid} on printer {printerName}, spooler ID: {foundJob.JobId}, status: {foundJob.Status}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to get jobs for printer {printerName}: {ex.Message}");
                    // Continue searching other printers
                }
            }

            // If not found, create not found response
            if (result == null)
            {
                result = new GuidStatusResponse
                {
                    Guid = guid,
                    Found = false
                };

                ConsoleWindow.WriteLine($"GUID {guid} not found in any printer queue");
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(result, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /status/guid/{{guid}} endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetStatusBySpooler(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /status/spooler/{{id}} from {context.Connection.RemoteIpAddress}");

            // Extract spooler ID from route parameter
            var spoolerIdString = context.Request.RouteValues["id"]?.ToString();

            if (string.IsNullOrWhiteSpace(spoolerIdString))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Spooler ID parameter is required" }, _jsonOptions));
                return;
            }

            // Parse spooler ID as integer
            if (!int.TryParse(spoolerIdString, out var spoolerId) || spoolerId <= 0)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Spooler ID must be a positive integer" }, _jsonOptions));
                return;
            }

            ConsoleWindow.WriteLine($"Searching for spooler ID: {spoolerId}");

            // Search all printers for the spooler ID
            var allPrinters = _printerService.GetPrinterNames();
            SpoolerStatusResponse? result = null;

            foreach (var printerName in allPrinters)
            {
                try
                {
                    var printerJobs = PrintDirect.GetPrinterJobs(printerName);

                    // Search for job with matching spooler ID
                    var foundJob = printerJobs.FirstOrDefault(j => j.JobId == spoolerId);

                    if (foundJob != null)
                    {
                        // Create response for found job
                        result = new SpoolerStatusResponse
                        {
                            SpoolerId = spoolerId,
                            Guid = foundJob.DocumentName,
                            Found = true,
                            PrinterName = printerName,
                            Status = foundJob.Status.ToString().ToLowerInvariant(),
                            IsError = foundJob.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                                     foundJob.Status.HasFlag(PrintDirect.JobStatus.UserIntervention),
                            IsPrinting = foundJob.Status.HasFlag(PrintDirect.JobStatus.Printing),
                            IsPaused = foundJob.Status.HasFlag(PrintDirect.JobStatus.Paused),
                            IsComplete = foundJob.Status.HasFlag(PrintDirect.JobStatus.Complete) ||
                                        foundJob.Status.HasFlag(PrintDirect.JobStatus.Printed) ||
                                        foundJob.Status.HasFlag(PrintDirect.JobStatus.Deleted)
                        };

                        ConsoleWindow.WriteLine($"Found spooler ID {spoolerId} on printer {printerName}, GUID: {foundJob.DocumentName}, status: {foundJob.Status}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to get jobs for printer {printerName}: {ex.Message}");
                    // Continue searching other printers
                }
            }

            // If not found, create not found response
            if (result == null)
            {
                result = new SpoolerStatusResponse
                {
                    SpoolerId = spoolerId,
                    Found = false
                };

                ConsoleWindow.WriteLine($"Spooler ID {spoolerId} not found in any printer queue");
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(result, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /status/spooler/{{id}} endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task GetQueue(HttpContext context)
    {
        try
        {
            ConsoleWindow.WriteLine($"GET /check-queue from {context.Connection.RemoteIpAddress}");

            // Get all printers
            var allPrinters = _printerService.GetPrinterNames();
            var allJobs = new List<QueueJobInfo>();

            // Aggregate jobs from all printers with detailed analysis
            foreach (var printerName in allPrinters)
            {
                try
                {
                    var printerJobs = PrintDirect.GetPrinterJobs(printerName);
                    var position = 1;

                    foreach (var job in printerJobs)
                    {
                        var currentTime = DateTime.UtcNow;
                        var submittedAt = currentTime.AddSeconds(-60); // Approximate submission time (Windows doesn't track this)
                        var ageSeconds = (long)(currentTime - submittedAt).TotalSeconds;

                        var queueJob = new QueueJobInfo
                        {
                            Guid = job.DocumentName,
                            SpoolerId = job.JobId,
                            PrinterName = printerName,
                            Status = job.Status.ToString().ToLowerInvariant(),
                            ErrorMessage = GetErrorMessage(job.Status),
                            DocumentName = job.DocumentName,
                            Position = position++,
                            PagesPrinted = 0, // Windows doesn't easily provide this info
                            TotalPages = 1, // Default assumption
                            SubmittedAt = submittedAt,
                            AgeSeconds = ageSeconds,
                            AgeFormatted = FormatAge(ageSeconds),
                            IsStuck = IsJobStuck(job.Status, ageSeconds),
                            IsError = IsErrorState(job.Status)
                        };

                        allJobs.Add(queueJob);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Failed to get jobs for printer {printerName}: {ex.Message}");
                    // Continue with other printers
                }
            }

            // Sort jobs by printer name and position
            allJobs = allJobs.OrderBy(j => j.PrinterName).ThenBy(j => j.Position).ToList();

            // Calculate summary statistics
            var summary = new QueueSummary
            {
                Total = allJobs.Count,
                Printing = allJobs.Count(j => j.Status.Equals("printing", StringComparison.OrdinalIgnoreCase)),
                Queued = allJobs.Count(j => j.Status.Equals("none", StringComparison.OrdinalIgnoreCase)),
                Spooling = allJobs.Count(j => j.Status.Equals("spooling", StringComparison.OrdinalIgnoreCase)),
                Paused = allJobs.Count(j => j.Status.Equals("paused", StringComparison.OrdinalIgnoreCase)),
                Error = allJobs.Count(j => j.IsError),
                StuckCount = allJobs.Count(j => j.IsStuck)
            };

            if (allJobs.Any())
            {
                summary.OldestJobAge = allJobs.Max(j => j.AgeSeconds);
                summary.OldestJobAgeFormatted = FormatAge(summary.OldestJobAge);
            }

            var response = new QueueCheckResponse
            {
                TotalJobs = allJobs.Count,
                HasErrors = allJobs.Any(j => j.IsError),
                HasStuckJobs = allJobs.Any(j => j.IsStuck),
                Jobs = allJobs,
                Summary = summary
            };

            ConsoleWindow.WriteLine($"Queue check completed: {allJobs.Count} total jobs, {summary.Error} errors, {summary.StuckCount} stuck");

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in /check-queue endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
        }
    }

    private async Task RetryQueue(HttpContext context)
    {
        try
        {
            // Read and parse request
            var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<RetryQueueRequest>(requestBody, _jsonOptions);

            if (request == null || string.IsNullOrWhiteSpace(request.PrinterName))
            {
                context.Response.StatusCode = 400;
                var errorResponse = new RetryQueueResponse
                {
                    Success = false,
                    RetriedCount = 0,
                    RetriedJobs = new List<RetriedJobInfo>(),
                    ErrorMessage = "Printer name is required"
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, _jsonOptions));
                return;
            }

            // Get jobs for the specified printer
            var printerJobs = PrintDirect.GetPrinterJobs(request.PrinterName);

            if (printerJobs.Length == 0)
            {
                // Check if printer exists by trying to get all printers
                var availablePrinters = _printerService.GetAllPrinters();
                if (!availablePrinters.Any(p => p.WindowsPrinterName.Equals(request.PrinterName, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.StatusCode = 404;
                    var notFoundResponse = new RetryQueueResponse
                    {
                        Success = false,
                        RetriedCount = 0,
                        RetriedJobs = new List<RetriedJobInfo>(),
                        ErrorMessage = $"Printer '{request.PrinterName}' not found"
                    };
                    await context.Response.WriteAsync(JsonSerializer.Serialize(notFoundResponse, _jsonOptions));
                    return;
                }
            }

            // Find retriable jobs (paused, error, blocked, user intervention)
            var retriableJobs = printerJobs.Where(job =>
                job.Status.HasFlag(PrintDirect.JobStatus.Paused) ||
                job.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                job.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                job.Status.HasFlag(PrintDirect.JobStatus.UserIntervention)
            ).ToList();

            var retriedJobs = new List<RetriedJobInfo>();
            var failureCount = 0;

            foreach (var job in retriableJobs)
            {
                try
                {
                    // Try to resume the job
                    PrintDirect.SendJobCommand(request.PrinterName, job.JobId, PrintDirect.JobCommand.JOB_CONTROL_RESUME);

                    // Get updated job status after retry
                    var updatedJobs = PrintDirect.GetPrinterJobs(request.PrinterName);
                    var updatedJob = updatedJobs.FirstOrDefault(j => j.JobId == job.JobId);

                    retriedJobs.Add(new RetriedJobInfo
                    {
                        Guid = ExtractGuidFromDocumentName(job.DocumentName),
                        SpoolerId = job.JobId,
                        Status = updatedJob?.Status.ToString().ToLowerInvariant() ?? "unknown",
                        ErrorMessage = null
                    });
                }
                catch (Exception ex)
                {
                    failureCount++;
                    retriedJobs.Add(new RetriedJobInfo
                    {
                        Guid = ExtractGuidFromDocumentName(job.DocumentName),
                        SpoolerId = job.JobId,
                        Status = "failed",
                        ErrorMessage = $"Failed to retry: {ex.Message}"
                    });
                }
            }

            var response = new RetryQueueResponse
            {
                Success = true,
                RetriedCount = retriableJobs.Count,
                RetriedJobs = retriedJobs,
                ErrorMessage = failureCount > 0 ? $"{failureCount} job(s) failed to retry" : null
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in retry queue endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            var errorResponse = new RetryQueueResponse
            {
                Success = false,
                RetriedCount = 0,
                RetriedJobs = new List<RetriedJobInfo>(),
                ErrorMessage = $"Internal server error: {ex.Message}"
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, _jsonOptions));
        }
    }

    private async Task GetRoot(HttpContext context)
    {
        try
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Printer Tray App API v0.1.0\n\nAvailable endpoints:\n- GET /health\n- GET /printers\n- POST /print\n- GET /status\n- GET /status/guid/{guid}\n- GET /status/spooler/{id}\n- GET /check-queue\n- POST /retry-queue\n- GET /self-test");
        }
        catch (Exception ex)
        {
            ConsoleWindow.WriteError($"Error in root endpoint: {ex.Message}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    // Helper methods for business logic
    private bool HasPrinterIssues(IList<PrinterInfo> printers)
    {
        return printers.Any(p => !p.IsOnline || p.HasError ||
                                p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase) ||
                                p.Status.Contains("Offline", StringComparison.OrdinalIgnoreCase));
    }

    private EnhancedHealthResponse CreateHealthResponse(List<PrinterInfo> printers, bool hasPrinterIssues)
    {
        return new EnhancedHealthResponse
        {
            Ok = true,
            Version = Constants.ApiVersion,
            HasPrinterIssues = hasPrinterIssues,
            Printers = MapToPrinterHealthStatus(printers),
            Summary = CreatePrinterSummary(printers),
            UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds
        };
    }

    private static List<PrinterHealthStatus> MapToPrinterHealthStatus(List<PrinterInfo> printers)
    {
        return printers.Select(p => new PrinterHealthStatus
        {
            Name = p.WindowsPrinterName,
            IsOnline = p.IsOnline,
            Status = p.Status,
            JobCount = (int)p.JobCount
        }).ToList();
    }

    private static PrinterHealthSummary CreatePrinterSummary(List<PrinterInfo> printers)
    {
        return new PrinterHealthSummary
        {
            TotalPrinters = printers.Count,
            OnlinePrinters = printers.Count(p => p.IsOnline),
            OfflinePrinters = printers.Count(p => !p.IsOnline),
            PrintersWithJobs = printers.Count(p => p.JobCount > 0)
        };
    }

    private PrintersResponse CreatePrintersResponse(IList<PrinterInfo> printers, bool hasPrinterIssues)
    {
        return new PrintersResponse
        {
            TotalPrinters = printers.Count,
            HasPrinterIssues = hasPrinterIssues,
            Printers = printers.Select(p => new PrinterDetailDto
            {
                Name = p.WindowsPrinterName,
                DisplayName = p.LogicalName,
                IsDefault = p.IsDefault,
                IsOnline = p.IsOnline,
                Status = p.Status,
                Port = p.PortName,
                PortType = p.PortType.ToString(),
                Driver = p.DriverName,
                JobCount = p.JobCount,
                SupportsRaw = p.SupportsRawPrinting,
                SupportedPaperSizes = new[] { "80mm", "58mm" },
                IsShared = false,
                ShareName = null
            }).ToList(),
            Summary = new PrintersSummary
            {
                Total = printers.Count,
                Online = printers.Count(p => p.IsOnline),
                Offline = printers.Count(p => !p.IsOnline),
                WithJobs = printers.Count(p => p.JobCount > 0),
                RawCapable = printers.Count(p => p.SupportsRawPrinting)
            }
        };
    }

    // Static helper methods
    private static string FormatAge(long ageSeconds)
    {
        if (ageSeconds < 60)
        {
            return ageSeconds == 1 ? "1 second" : $"{ageSeconds} seconds";
        }
        else if (ageSeconds < 3600)
        {
            var minutes = ageSeconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        else if (ageSeconds < 86400)
        {
            var hours = ageSeconds / 3600;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        else
        {
            var days = ageSeconds / 86400;
            return days == 1 ? "1 day" : $"{days} days";
        }
    }

    private static bool IsJobStuck(PrintDirect.JobStatus status, long ageSeconds)
    {
        // Job is stuck if it's been printing/spooling for more than 30 seconds
        return (status == PrintDirect.JobStatus.Printing || status == PrintDirect.JobStatus.Spooling)
               && ageSeconds > 30;
    }

    private static bool IsErrorState(PrintDirect.JobStatus status)
    {
        return status.HasFlag(PrintDirect.JobStatus.Error) ||
               status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
               status.HasFlag(PrintDirect.JobStatus.Blocked) ||
               status.HasFlag(PrintDirect.JobStatus.UserIntervention);
    }

    private static string? GetErrorMessage(PrintDirect.JobStatus status)
    {
        if (status.HasFlag(PrintDirect.JobStatus.PaperOut))
            return "Paper out";
        else if (status.HasFlag(PrintDirect.JobStatus.Error))
            return "Printer error";
        else if (status.HasFlag(PrintDirect.JobStatus.Blocked))
            return "Print job blocked";
        else if (status.HasFlag(PrintDirect.JobStatus.UserIntervention))
            return "User intervention required";
        else if (status.HasFlag(PrintDirect.JobStatus.Offline))
            return "Printer offline";

        return null;
    }

    private static string? GetTargetPrinterName(PrinterTask printerTask)
    {
        if (!string.IsNullOrWhiteSpace(printerTask.PrinterDeviceName))
        {
            ConsoleWindow.WriteLine($"Using printerDeviceName: {printerTask.PrinterDeviceName}");
            return printerTask.PrinterDeviceName;
        }

        if (!string.IsNullOrWhiteSpace(printerTask.PrinterName))
        {
            ConsoleWindow.WriteLine($"Using printerName: {printerTask.PrinterName}");
            return printerTask.PrinterName;
        }

        return null; // No printer specified
    }

    private static string? ExtractGuidFromDocumentName(string? documentName)
    {
        // Document name should be the GUID from PrinterTask._id.Id
        return string.IsNullOrWhiteSpace(documentName) ? null : documentName;
    }
}
