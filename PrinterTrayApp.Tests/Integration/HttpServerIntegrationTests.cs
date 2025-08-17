using Xunit;
using FluentAssertions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PrinterTrayApp.Tests.Integration;

public class HttpServerIntegrationTests : IAsyncLifetime
{
    private HttpServer _httpServer = null!;
    private HttpClient _httpClient = null!;
    private PrinterService _printerService = null!;
    private CancellationTokenSource _cancellationTokenSource = null!;

    public async Task InitializeAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _printerService = new PrinterService();
        _httpServer = new HttpServer(_printerService);
        
        // Start the server
        await _httpServer.StartAsync(_cancellationTokenSource.Token);
        
        // Create HTTP client
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Constants.ApiPort}")
        };
        
        // Give server time to fully start
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _httpServer.StopAsync();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    [Fact]
    public async Task Health_Endpoint_Should_Return_OK()
    {
        // Act
        var response = await _httpClient.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        var healthResponse = JsonSerializer.Deserialize<HealthResponse>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        healthResponse.Should().NotBeNull();
        healthResponse!.Ok.Should().BeTrue();
        healthResponse.Version.Should().Be(Constants.ApiVersion);
        healthResponse.Printers.Should().NotBeNull();
        healthResponse.UptimeSeconds.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Printers_Endpoint_Should_Return_PrinterList()
    {
        // Act
        var response = await _httpClient.GetAsync("/printers");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        var printersResponse = JsonDocument.Parse(content);
        printersResponse.RootElement.TryGetProperty("printers", out var printers).Should().BeTrue();
        printers.ValueKind.Should().Be(JsonValueKind.Array);
        
        // Should not contain virtual printers
        foreach (var printer in printers.EnumerateArray())
        {
            var name = printer.GetProperty("windowsPrinterName").GetString();
            name.Should().NotContain("PDF");
            name.Should().NotContain("XPS");
        }
    }

    [Fact]
    public async Task Print_Endpoint_Should_Reject_Empty_Body()
    {
        // Arrange
        var content = new StringContent("", Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseContent.Should().Contain("error");
        responseContent.Should().Contain("empty");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Reject_Invalid_Json()
    {
        // Arrange
        var content = new StringContent("not valid json", Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseContent.Should().Contain("error");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Reject_Missing_Printer()
    {
        // Arrange
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Test</text></root>",
                name = "Test Template"
            },
            templateData = "{}",
            printerDeviceName = "", // Empty printer name
            printerName = "" // Also empty
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        responseContent.Should().Contain("error");
        responseContent.Should().Contain("printer");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Reject_NonExistent_Printer()
    {
        // Arrange
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Test</text></root>",
                name = "Test Template"
            },
            templateData = "{}",
            printerDeviceName = "NonExistentPrinter12345"
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        responseContent.Should().Contain("error");
        responseContent.Should().Contain("not found");
        responseContent.Should().Contain("NonExistentPrinter12345");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Accept_Valid_PrinterTask()
    {
        // Arrange - get a real printer if available
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            // Skip test if no printers available
            return;
        }
        
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Integration Test</text><command cmd=\"cut\"/></root>",
                name = "Test Template",
                templateType = "Receipt"
            },
            templateData = "{\"test\": \"data\"}",
            printerDeviceName = printers.First().WindowsPrinterName,
            isOpenCashDrawer = false
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        responseContent.Should().Contain("jobNumber");
        responseContent.Should().Contain("success");
        
        var result = JsonDocument.Parse(responseContent);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("jobNumber").GetString().Should().StartWith("PRT-");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Use_PrinterName_When_DeviceName_Empty()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            return;
        }
        
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-002", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Test</text></root>",
                name = "Test"
            },
            templateData = "{}",
            printerDeviceName = "", // Empty
            printerName = printers.First().WindowsPrinterName // Use printerName instead
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = JsonDocument.Parse(responseContent);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Root_Endpoint_Should_Return_API_Info()
    {
        // Act
        var response = await _httpClient.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Contain("Printer Tray App API");
        content.Should().Contain("/health");
        content.Should().Contain("/printers");
        content.Should().Contain("/print");
    }

    [Fact]
    public async Task SelfTest_Endpoint_Should_Return_NotImplemented()
    {
        // Act
        var response = await _httpClient.GetAsync("/self-test");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        content.Should().Contain("Not implemented");
    }

    [Fact]
    public async Task Print_Endpoint_Should_Handle_Cash_Drawer_Command()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            return;
        }
        
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-003", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Cash Drawer Test</text></root>",
                name = "Drawer Test"
            },
            templateData = "{}",
            printerDeviceName = printers.First().WindowsPrinterName,
            isOpenCashDrawer = true // Enable cash drawer
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/print", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = JsonDocument.Parse(responseContent);
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        // Cash drawer jobs have max 3 retries
        result.RootElement.GetProperty("retryCount").GetInt32().Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task Print_Endpoint_Should_Generate_Unique_Job_Numbers()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            return;
        }
        
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-004", siteId = "site-001" },
            template = new ReceiptTemplate 
            { 
                body = "<root><text>Job Number Test</text></root>",
                name = "Test"
            },
            templateData = "{}",
            printerDeviceName = printers.First().WindowsPrinterName
        };
        
        var json = JsonSerializer.Serialize(printerTask);
        var content1 = new StringContent(json, Encoding.UTF8, "application/json");
        var content2 = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response1 = await _httpClient.PostAsync("/print", content1);
        var response2 = await _httpClient.PostAsync("/print", content2);
        
        var content1Result = await response1.Content.ReadAsStringAsync();
        var content2Result = await response2.Content.ReadAsStringAsync();

        // Assert
        var result1 = JsonDocument.Parse(content1Result);
        var result2 = JsonDocument.Parse(content2Result);
        
        var jobNumber1 = result1.RootElement.GetProperty("jobNumber").GetString();
        var jobNumber2 = result2.RootElement.GetProperty("jobNumber").GetString();
        
        jobNumber1.Should().NotBe(jobNumber2);
        jobNumber1.Should().MatchRegex(@"^PRT-\d{8}-\d{6}$");
        jobNumber2.Should().MatchRegex(@"^PRT-\d{8}-\d{6}$");
    }
}