using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;

namespace PrinterTrayApp.Tests.Unit;

public class HealthEndpointEnhancementsTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public HealthEndpointEnhancementsTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void EnhancedHealthResponse_ShouldIncludeHasPrinterIssuesFlag()
    {
        // Arrange
        var response = new EnhancedHealthResponse
        {
            Ok = true,
            Version = "0.1.0",
            HasPrinterIssues = false,
            UptimeSeconds = 3600
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedHealthResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.HasPrinterIssues.Should().BeFalse();
    }

    [Fact]
    public void EnhancedHealthResponse_ShouldIncludePrinterStatusDetails()
    {
        // Arrange
        var response = new EnhancedHealthResponse
        {
            Ok = true,
            Version = "0.1.0",
            Printers = new List<PrinterHealthStatus>
            {
                new PrinterHealthStatus
                {
                    Name = "TestPrinter",
                    IsOnline = true,
                    Status = "Ready",
                    JobCount = 0
                }
            },
            UptimeSeconds = 3600
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedHealthResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Printers.Should().HaveCount(1);
        var printer = deserialized.Printers!.First();
        printer.Name.Should().Be("TestPrinter");
        printer.IsOnline.Should().BeTrue();
        printer.Status.Should().Be("Ready");
        printer.JobCount.Should().Be(0);
    }

    [Fact]
    public void EnhancedHealthResponse_ShouldIncludeSummaryStatistics()
    {
        // Arrange
        var response = new EnhancedHealthResponse
        {
            Ok = true,
            Version = "0.1.0",
            Summary = new PrinterHealthSummary
            {
                TotalPrinters = 3,
                OnlinePrinters = 2,
                OfflinePrinters = 1,
                PrintersWithJobs = 1
            },
            UptimeSeconds = 3600
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedHealthResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Summary.Should().NotBeNull();
        deserialized.Summary!.TotalPrinters.Should().Be(3);
        deserialized.Summary.OnlinePrinters.Should().Be(2);
        deserialized.Summary.OfflinePrinters.Should().Be(1);
        deserialized.Summary.PrintersWithJobs.Should().Be(1);
    }

    [Fact]
    public void DetermineHasPrinterIssues_ShouldReturnTrue_WhenAnyPrinterIsOffline()
    {
        // Arrange
        var printers = new List<PrinterInfo>
        {
            new PrinterInfo { IsOnline = true, Status = "Ready" },
            new PrinterInfo { IsOnline = false, Status = "Offline" }
        };

        // Act
        var hasIssues = printers.Any(p => !p.IsOnline || 
                                          p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                          p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase));

        // Assert
        hasIssues.Should().BeTrue();
    }

    [Fact]
    public void DetermineHasPrinterIssues_ShouldReturnTrue_WhenAnyPrinterHasError()
    {
        // Arrange
        var printers = new List<PrinterInfo>
        {
            new PrinterInfo { IsOnline = true, Status = "Ready" },
            new PrinterInfo { IsOnline = true, Status = "Paper Out Error" }
        };

        // Act
        var hasIssues = printers.Any(p => !p.IsOnline || 
                                          p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                          p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase));

        // Assert
        hasIssues.Should().BeTrue();
    }

    [Fact]
    public void DetermineHasPrinterIssues_ShouldReturnFalse_WhenAllPrintersAreHealthy()
    {
        // Arrange
        var printers = new List<PrinterInfo>
        {
            new PrinterInfo { IsOnline = true, Status = "Ready" },
            new PrinterInfo { IsOnline = true, Status = "Idle" }
        };

        // Act
        var hasIssues = printers.Any(p => !p.IsOnline || 
                                          p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                          p.Status.Contains("Paper", StringComparison.OrdinalIgnoreCase));

        // Assert
        hasIssues.Should().BeFalse();
    }

    [Fact]
    public void CalculateSummaryStatistics_ShouldReturnCorrectCounts()
    {
        // Arrange
        var printers = new List<PrinterInfo>
        {
            new PrinterInfo { IsOnline = true, Status = "Ready", JobCount = 0 },
            new PrinterInfo { IsOnline = true, Status = "Printing", JobCount = 3 },
            new PrinterInfo { IsOnline = false, Status = "Offline", JobCount = 0 }
        };

        // Act
        var summary = new PrinterHealthSummary
        {
            TotalPrinters = printers.Count,
            OnlinePrinters = printers.Count(p => p.IsOnline),
            OfflinePrinters = printers.Count(p => !p.IsOnline),
            PrintersWithJobs = printers.Count(p => p.JobCount > 0)
        };

        // Assert
        summary.TotalPrinters.Should().Be(3);
        summary.OnlinePrinters.Should().Be(2);
        summary.OfflinePrinters.Should().Be(1);
        summary.PrintersWithJobs.Should().Be(1);
    }
}

// Enhanced models for testing
public class EnhancedHealthResponse
{
    public bool Ok { get; set; }
    public string Version { get; set; } = "0.1.0";
    public bool HasPrinterIssues { get; set; }
    public List<PrinterHealthStatus>? Printers { get; set; }
    public PrinterHealthSummary? Summary { get; set; }
    public long UptimeSeconds { get; set; }
}

public class PrinterHealthStatus
{
    public string Name { get; set; } = "";
    public bool IsOnline { get; set; }
    public string Status { get; set; } = "";
    public int JobCount { get; set; }
}

public class PrinterHealthSummary
{
    public int TotalPrinters { get; set; }
    public int OnlinePrinters { get; set; }
    public int OfflinePrinters { get; set; }
    public int PrintersWithJobs { get; set; }
}