using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PrinterTrayApp.Tests.Integration;

public class HealthEndpointTests : IClassFixture<PrinterTrayAppFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public HealthEndpointTests(PrinterTrayAppFactory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    [Fact]
    public async Task GetHealth_ReturnsOkStatus()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsOkTrue()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealth_ReturnsVersion()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.Version.Should().NotBeNullOrEmpty();
        response.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public async Task GetHealth_ReturnsPrintersList()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.Printers.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHealth_ReturnsHasPrinterIssuesFlag()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.HasPrinterIssues.Should().NotBeNull();
    }

    [Fact]
    public async Task GetHealth_ReturnsPrintersWithStatusDetails()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        if (response!.Printers?.Any() == true)
        {
            var firstPrinter = response.Printers.First();
            firstPrinter.Should().NotBeNull();
            firstPrinter.Name.Should().NotBeNullOrEmpty();
            firstPrinter.IsOnline.Should().NotBeNull();
            firstPrinter.Status.Should().NotBeNullOrEmpty();
            firstPrinter.JobCount.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task GetHealth_ReturnsSummaryObject()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.Summary.Should().NotBeNull();
        response.Summary!.TotalPrinters.Should().BeGreaterThanOrEqualTo(0);
        response.Summary.OnlinePrinters.Should().BeGreaterThanOrEqualTo(0);
        response.Summary.OfflinePrinters.Should().BeGreaterThanOrEqualTo(0);
        response.Summary.PrintersWithJobs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_ReturnsUptimeSeconds()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_SummaryMatchesPrintersList()
    {
        // Act
        var response = await _client.GetFromJsonAsync<HealthResponseDto>("/health", _jsonOptions);

        // Assert
        response.Should().NotBeNull();
        if (response!.Summary != null && response.Printers != null)
        {
            response.Summary.TotalPrinters.Should().Be(response.Printers.Count());
            
            var onlineCount = response.Printers.Count(p => p.IsOnline == true);
            response.Summary.OnlinePrinters.Should().Be(onlineCount);
            
            var offlineCount = response.Printers.Count(p => p.IsOnline == false);
            response.Summary.OfflinePrinters.Should().Be(offlineCount);
        }
    }
}

public class HealthResponseDto
{
    public bool Ok { get; set; }
    public string? Version { get; set; }
    public bool? HasPrinterIssues { get; set; }
    public IEnumerable<PrinterStatusDto>? Printers { get; set; }
    public PrinterSummaryDto? Summary { get; set; }
    public long UptimeSeconds { get; set; }
}

public class PrinterStatusDto
{
    public string Name { get; set; } = "";
    public bool? IsOnline { get; set; }
    public string? Status { get; set; }
    public int JobCount { get; set; }
}

public class PrinterSummaryDto
{
    public int TotalPrinters { get; set; }
    public int OnlinePrinters { get; set; }
    public int OfflinePrinters { get; set; }
    public int PrintersWithJobs { get; set; }
}