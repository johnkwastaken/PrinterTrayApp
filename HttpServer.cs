using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// IMPORTANT: ALL data comes from templateData field - nothing from root level is used
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

        _app.MapGet("/health", async (HttpContext context) =>
        {
            try
            {
                ConsoleWindow.WriteLine($"GET /health from {context.Connection.RemoteIpAddress}");
                
                var response = new HealthResponse
                {
                    Ok = true,
                    Version = Constants.ApiVersion,
                    Printers = _printerService.GetPrinterNames(), 
                    UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error in /health endpoint: {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
            }
        });

        _app.MapPost("/print", async (HttpContext context) =>
        {
            // ============================================================
            // MAIN PRINT ENDPOINT - Receives PrinterTask JSON from POS
            // ============================================================
            try
            {
                ConsoleWindow.WriteLine($"POST /print from {context.Connection.RemoteIpAddress}");
                
                // Read the JSON body from the HTTP request
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                
                if (string.IsNullOrEmpty(body))
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Request body is empty" }, _jsonOptions));
                    return;
                }
                
                ConsoleWindow.WriteLine($"Print request body length: {body.Length}");
                
                // Clean MongoDB types from the entire JSON before deserializing
                // POS sends MongoDB-specific types like {"$date": 123456} that need conversion
                string cleanedBody = body;
                try
                {
                    cleanedBody = JsonCleaner.CleanFullJson(body);
                    ConsoleWindow.WriteLine("Cleaned MongoDB types from request");
                }
                catch (Exception ex)
                {
                    ConsoleWindow.WriteError($"Warning: Could not clean MongoDB types: {ex.Message}");
                    // Continue with original body - not fatal
                }
                
                PrinterTask? printerTask;
                try
                {
                    printerTask = JsonSerializer.Deserialize<PrinterTask>(cleanedBody, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                        error = "Invalid JSON format", 
                        details = ex.Message 
                    }, _jsonOptions));
                    return;
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                        error = "Failed to parse JSON", 
                        details = ex.Message 
                    }, _jsonOptions));
                    return;
                }
                
                if (printerTask == null)
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Invalid print task format" }, _jsonOptions));
                    return;
                }
                
                // Generate unique job number for tracking (format: PRT-YYYYMMDD-######)
                var jobNumber = GenerateJobNumber();
                ConsoleWindow.WriteLine($"Processing print job: {jobNumber}");
                
                // Get available printers from Windows
                // Currently using first available printer - routing logic can be added later
                var availablePrinters = _printerService.GetPrinterNames();
                if (availablePrinters.Count == 0)
                {
                    ConsoleWindow.WriteError("No printers available");
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { 
                        error = "No printers available",
                        jobNumber = jobNumber
                    }, _jsonOptions));
                    return;
                }
                
                // Select printer - currently using first available
                // TODO: Future enhancement - use printer mapping from templateData
                string targetPrinter = availablePrinters[0];
                ConsoleWindow.WriteLine($"Using first available printer: {targetPrinter}");
                ConsoleWindow.WriteLine($"Open cash drawer: {printerTask.isOpenCashDrawer}");
                
                // Process the print task through the full pipeline
                // This is where the magic happens - template rendering, command generation, printing
                var result = await ProcessPrintTask(printerTask, jobNumber, targetPrinter);
                
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = result.Success ? 200 : 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(result, _jsonOptions));
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error in /print endpoint: {ex.Message}");
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
            }
        });

        _app.MapGet("/self-test", async (HttpContext context) =>
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
        });

        _app.MapGet("/printers", async (HttpContext context) =>
        {
            try
            {
                ConsoleWindow.WriteLine($"GET /printers from {context.Connection.RemoteIpAddress}");
                
                var printers = _printerService.GetAllPrinters();
                var response = new
                {
                    printers = printers.Select(p => new
                    {
                        logicalName = p.LogicalName,
                        windowsPrinterName = p.WindowsPrinterName,
                        compositeId = p.UniqueId,  // Name@Port composite ID
                        status = p.Status,
                        isOnline = p.IsOnline,
                        isDefault = p.IsDefault,
                        port = p.PortName,
                        portType = p.PortType.ToString(),
                        supportsRaw = p.SupportsRawPrinting,
                        jobCount = p.JobCount
                    })
                };
                
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error in /printers endpoint: {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, _jsonOptions));
            }
        });

        _app.MapGet("/", async (HttpContext context) =>
        {
            try
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Printer Tray App API v0.1.0\n\nAvailable endpoints:\n- GET /health\n- GET /printers\n- POST /print\n- GET /self-test");
            }
            catch (Exception ex)
            {
                ConsoleWindow.WriteError($"Error in root endpoint: {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error: {ex.Message}");
            }
        });

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
    
    private string GenerateJobNumber()
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var random = new Random();
        var sequence = random.Next(100000, 999999);
        return $"PRT-{date}-{sequence:D6}";
    }
    
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
    /// <param name="task">PrinterTask containing template and ALL data in templateData field</param>
    /// <param name="jobNumber">Unique job number for tracking (PRT-YYYYMMDD-######)</param>
    /// <param name="overridePrinter">Optional printer override for testing (ignores printer in data)</param>
    /// <returns>PrintResult with success status and any error messages</returns>
    private async Task<PrintResult> ProcessPrintTask(PrinterTask task, string jobNumber, string? overridePrinter = null)
    {
        var result = new PrintResult { JobNumber = jobNumber };
        
        try
        {
            // STEP 1: Validate template exists
            if (task.template == null || string.IsNullOrEmpty(task.template.body))
            {
                throw new Exception("Template is missing or empty");
            }
            
            ConsoleWindow.WriteLine($"Processing PrinterTask for job {jobNumber}...");
            
            // STEP 2: Process the templateData field
            // IMPORTANT: ALL data is in templateData - it's a JSON string containing:
            // - sites (store info)
            // - orders (order details, totals, items)
            // - staff (employee info)
            // - registers (register/terminal info)
            // - currencies (currency formatting)
            // - dateOfPrinting, printerName, appVersion, etc.
            // The template uses {{path.to.value}} tokens to reference this data
            
            // ============================================================
            // STEP 1: Parse templateData (JSON string from POS)
            // ============================================================
            // The POS sends templateData as a JSON STRING, not an object
            // It contains ALL the data needed for printing:
            // - printerName/printerDeviceName: Which printer to use
            // - sites: Store information
            // - orders: Order details with products
            // - staff, registers, currencies: Context data
            // - dateOfPrinting, header, footer: Print metadata
            string processedTemplateData = task.templateData ?? "{}";
            
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
            // STEP 2: Data Structure Validation
            // ============================================================
            // IMPORTANT: We do NOT use JsonCleaner on templateData anymore
            // The POS sends pre-flattened products with level properties:
            // - level 0: Main products
            // - level 1: Modifiers (indented 2 spaces)
            // - level 2+: Sub-modifiers (indented 2 spaces per level)
            // Circular references exist but are handled via the level system
            DebugLogger.Log($"[POS-FIX] Keeping POS data structure intact - not using JsonCleaner");
            
            // processedTemplateData is already the string we need
            DebugLogger.Log($"[POS-FIX] Template data ready for processing");
            
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
            DebugLogger.Log($"[HttpServer] About to render template: {task.template.name}");
            
            XmlDocument xmlDoc;
            try
            {
                DebugLogger.Log($"[HttpServer] Calling SimpleTemplateProcessor...");
                
                // Use simple processor for now to debug
                bool useSimpleProcessor = false;
                
                if (useSimpleProcessor && task.template.body.Contains("docket-section"))
                {
                    DebugLogger.Log($"[HttpServer] Using SimpleTemplateProcessor for docket");
                    xmlDoc = SimpleTemplateProcessor.RenderTemplateSimple(
                        task.template.body,
                        processedTemplateData
                    );
                }
                else
                {
                    DebugLogger.Log($"[HttpServer] Using TemplateHelpers.RenderTemplate");
                    xmlDoc = TemplateHelpers.RenderTemplate(
                        task.template.body,        // XML template with {{tokens}} and print commands
                        processedTemplateData,     // JSON string with all the data
                        PrinterPaperWidth.Paper_80 // Standard 80mm thermal printer width
                    );
                }
                
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
            if (task.isOpenCashDrawer)
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
            
            // Determine which printer to use
            // Priority: 1) Override (for testing), 2) templateData, 3) Root field
            string printerName = "";
            if (!string.IsNullOrWhiteSpace(overridePrinter))
            {
                printerName = overridePrinter;
                ConsoleWindow.WriteLine($"Using test override printer: {printerName}");
            }
            else
            {
                // Get printer name from templateData (POS standard)
                try
                {
                    var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(processedTemplateData);
                    if (dataDict != null)
                    {
                        if (dataDict.TryGetValue("printerDeviceName", out var device) && device != null)
                            printerName = device.ToString() ?? "";
                        else if (dataDict.TryGetValue("printerName", out var name) && name != null)
                            printerName = name.ToString() ?? "";
                    }
                }
                catch { }
                
                // Fallback to root field if needed (backward compatibility)
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    printerName = task.printerDeviceName;
                }
            }
            
            // ============================================================
            // Retry Logic with Cash Drawer Safety
            // ============================================================
            // Implements smart retry logic:
            // - Normal prints: Up to 5 retries
            // - Cash drawer prints: Limited to 3 retries
            // This prevents the cash drawer from opening multiple times
            // if there's a communication issue
            var maxRetries = task.isOpenCashDrawer ? 3 : 5;
            var retryCount = 0;
            Exception? lastError = null;
            
            // Retry loop for handling temporary printer issues
            while (retryCount < maxRetries)
            {
                try
                {
                    var spoolJobId = PrintDirect.Print(
                        printerName,
                        $"PrintJob-{jobNumber}",
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
            
            if (task.isOpenCashDrawer && retryCount >= 3)
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
    
    private class PrintResult
    {
        public bool Success { get; set; }
        public string JobNumber { get; set; } = "";
        public int? SpoolerJobId { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
    }
}