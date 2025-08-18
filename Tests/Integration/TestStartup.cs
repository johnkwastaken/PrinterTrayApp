using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrinterTrayApp;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;

namespace PrinterTrayApp.Tests.Integration;

public class TestStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Add JSON options
        services.AddSingleton(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // Add mock PrinterService
        var mockPrinterService = new Mock<PrinterService>();
        
        // Setup mock printers for testing
        mockPrinterService.Setup(x => x.GetPrinterNames())
            .Returns(new List<string> { "TestPrinter1", "TestPrinter2" });

        mockPrinterService.Setup(x => x.GetAllPrinters())
            .Returns(new List<PrinterInfo>
            {
                new PrinterInfo
                {
                    LogicalName = "TestPrinter1",
                    WindowsPrinterName = "TestPrinter1",
                    Status = "Ready",
                    IsOnline = true,
                    IsDefault = false,
                    PortName = "USB001",
                    JobCount = 0
                },
                new PrinterInfo
                {
                    LogicalName = "TestPrinter2",
                    WindowsPrinterName = "TestPrinter2",
                    Status = "Offline",
                    IsOnline = false,
                    IsDefault = false,
                    PortName = "192.168.1.100",
                    JobCount = 2
                }
            });

        services.AddSingleton(mockPrinterService.Object);
        services.AddSingleton<HttpServer>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        // Register endpoints similar to HttpServer
        app.UseEndpoints(endpoints =>
        {
            var printerService = app.ApplicationServices.GetRequiredService<PrinterService>();
            var jsonOptions = app.ApplicationServices.GetRequiredService<JsonSerializerOptions>();
            var startTime = DateTime.UtcNow;

            endpoints.MapGet("/health", async context =>
            {
                var allPrinters = printerService.GetAllPrinters();
                
                // Create enhanced response with all required fields
                var response = new
                {
                    ok = true,
                    version = Constants.ApiVersion,
                    hasPrinterIssues = allPrinters.Any(p => !p.IsOnline || 
                                                           p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                                           p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase)),
                    printers = allPrinters.Select(p => new
                    {
                        name = p.WindowsPrinterName,
                        isOnline = p.IsOnline,
                        status = p.Status,
                        jobCount = p.JobCount
                    }),
                    summary = new
                    {
                        totalPrinters = allPrinters.Count,
                        onlinePrinters = allPrinters.Count(p => p.IsOnline),
                        offlinePrinters = allPrinters.Count(p => !p.IsOnline),
                        printersWithJobs = allPrinters.Count(p => p.JobCount > 0)
                    },
                    uptimeSeconds = (long)(DateTime.UtcNow - startTime).TotalSeconds
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
            });

            endpoints.MapGet("/printers", async context =>
            {
                var printers = printerService.GetAllPrinters();
                var response = new
                {
                    totalPrinters = printers.Count,
                    hasPrinterIssues = printers.Any(p => !p.IsOnline),
                    printers = printers.Select(p => new
                    {
                        name = p.WindowsPrinterName,
                        displayName = p.LogicalName,
                        isDefault = p.IsDefault,
                        isOnline = p.IsOnline,
                        status = p.Status,
                        statusFlags = 0,
                        port = p.PortName,
                        portType = p.PortType.ToString(),
                        driver = "Generic / Text Only",
                        location = "",
                        comment = "",
                        jobCount = p.JobCount,
                        supportsRaw = p.SupportsRawPrinting,
                        supportedPaperSizes = new[] { "80mm", "58mm" },
                        isShared = false,
                        shareName = (string?)null
                    }),
                    summary = new
                    {
                        total = printers.Count,
                        online = printers.Count(p => p.IsOnline),
                        offline = printers.Count(p => !p.IsOnline),
                        withJobs = printers.Count(p => p.JobCount > 0),
                        rawCapable = printers.Count(p => p.SupportsRawPrinting)
                    }
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
            });

            // Add other endpoints as needed for testing
        });
    }
}