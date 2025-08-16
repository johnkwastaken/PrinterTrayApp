using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;

namespace PrinterTrayApp;

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
            WriteIndented = true
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        builder.WebHost.UseKestrel(options =>
        {
            options.ListenLocalhost(9877);
        });

        _app = builder.Build();

        _app.UseRouting();

        _app.MapGet("/health", async (HttpContext context) =>
        {
            ConsoleWindow.WriteLine($"GET /health from {context.Connection.RemoteIpAddress}");
            
            var response = new HealthResponse
            {
                Ok = true,
                Version = "0.1.0",
                Printers = _printerService.GetPrinterNames(), 
                UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds
            };

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        });

        _app.MapPost("/print", async (HttpContext context) =>
        {
            ConsoleWindow.WriteLine($"POST /print from {context.Connection.RemoteIpAddress}");
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 501;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Not implemented yet" }, _jsonOptions));
        });

        _app.MapGet("/self-test", async (HttpContext context) =>
        {
            ConsoleWindow.WriteLine($"GET /self-test from {context.Connection.RemoteIpAddress}");
            
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 501;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Not implemented yet" }, _jsonOptions));
        });

        _app.MapGet("/printers", async (HttpContext context) =>
        {
            ConsoleWindow.WriteLine($"GET /printers from {context.Connection.RemoteIpAddress}");
            
            var printers = _printerService.GetAllPrinters();
            var response = new
            {
                printers = printers.Select(p => new
                {
                    logicalName = p.LogicalName,
                    windowsPrinterName = p.WindowsPrinterName,
                    status = p.Status,
                    isOnline = p.IsOnline,
                    isDefault = p.IsDefault,
                    port = p.PortName,
                    jobCount = p.JobCount
                })
            };
            
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
        });

        _app.MapGet("/", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Printer Tray App API v0.1.0\n\nAvailable endpoints:\n- GET /health\n- GET /printers\n- POST /print\n- GET /self-test");
        });

        await _app.StartAsync(cancellationToken);
        
        ConsoleWindow.WriteLine("HTTP Server started on http://127.0.0.1:9877");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
    }
}