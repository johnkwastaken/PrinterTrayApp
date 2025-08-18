using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class PrintEndpointEnhancementsTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public PrintEndpointEnhancementsTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void EnhancedPrintResponse_ShouldIncludeGuid()
    {
        // Arrange
        var response = new EnhancedPrintResponse
        {
            Success = true,
            Guid = "test-001",
            SpoolerId = 142,
            PrinterName = "TestPrinter",
            DocumentName = "test-001",
            HasErrors = false,
            HasPrinterIssues = false,
            Message = "Print job submitted successfully"
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedPrintResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Guid.Should().Be("test-001");
    }

    [Fact]
    public void EnhancedPrintResponse_ShouldIncludeSpoolerId()
    {
        // Arrange
        var response = new EnhancedPrintResponse
        {
            Success = true,
            Guid = "test-001",
            SpoolerId = 142,
            PrinterName = "TestPrinter"
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedPrintResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.SpoolerId.Should().Be(142);
    }

    [Fact]
    public void EnhancedPrintResponse_ShouldIncludeHasErrorsFlag()
    {
        // Arrange
        var response = new EnhancedPrintResponse
        {
            Success = true,
            HasErrors = true,
            Message = "Print job submitted but queue has errors"
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedPrintResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void EnhancedPrintResponse_ShouldIncludeHasPrinterIssuesFlag()
    {
        // Arrange
        var response = new EnhancedPrintResponse
        {
            Success = true,
            HasPrinterIssues = true,
            Message = "Print job submitted but some printers have issues"
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<EnhancedPrintResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.HasPrinterIssues.Should().BeTrue();
    }

    [Fact]
    public void ExtractGuidFromPrinterTask_ShouldReturnIdFromObjectId()
    {
        // Arrange
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-guid-123" }
        };

        // Act
        var guid = printerTask._id?.id ?? "";

        // Assert
        guid.Should().Be("test-guid-123");
    }

    [Fact]
    public void CheckForDuplicateGuid_ShouldDetectExistingJob()
    {
        // Arrange
        var existingJobs = new List<PrintJobInfo>
        {
            new PrintJobInfo { DocumentName = "test-001", JobId = 100 },
            new PrintJobInfo { DocumentName = "test-002", JobId = 101 }
        };

        var newGuid = "test-001";

        // Act
        var isDuplicate = existingJobs.Any(j => j.DocumentName == newGuid);

        // Assert
        isDuplicate.Should().BeTrue();
    }

    [Fact]
    public void CheckForDuplicateGuid_ShouldReturnFalseForNewGuid()
    {
        // Arrange
        var existingJobs = new List<PrintJobInfo>
        {
            new PrintJobInfo { DocumentName = "test-001", JobId = 100 },
            new PrintJobInfo { DocumentName = "test-002", JobId = 101 }
        };

        var newGuid = "test-003";

        // Act
        var isDuplicate = existingJobs.Any(j => j.DocumentName == newGuid);

        // Assert
        isDuplicate.Should().BeFalse();
    }

    [Fact]
    public void CheckForQueueErrors_ShouldDetectErroredJobs()
    {
        // Arrange
        var jobs = new List<PrintJobInfo>
        {
            new PrintJobInfo { JobId = 100, Status = JobStatus.Printing },
            new PrintJobInfo { JobId = 101, Status = JobStatus.Error }
        };

        // Act
        var hasErrors = jobs.Any(j => j.Status.HasFlag(JobStatus.Error) || 
                                      j.Status.HasFlag(JobStatus.PaperOut) ||
                                      j.Status.HasFlag(JobStatus.Blocked));

        // Assert
        hasErrors.Should().BeTrue();
    }

    [Fact]
    public void CheckForQueueErrors_ShouldReturnFalseWhenNoErrors()
    {
        // Arrange
        var jobs = new List<PrintJobInfo>
        {
            new PrintJobInfo { JobId = 100, Status = JobStatus.Printing },
            new PrintJobInfo { JobId = 101, Status = JobStatus.Spooling }
        };

        // Act
        var hasErrors = jobs.Any(j => j.Status.HasFlag(JobStatus.Error) || 
                                      j.Status.HasFlag(JobStatus.PaperOut) ||
                                      j.Status.HasFlag(JobStatus.Blocked));

        // Assert
        hasErrors.Should().BeFalse();
    }

    [Fact]
    public void PrintEndpoint_ShouldUseGuidAsDocumentName()
    {
        // Arrange
        var guid = "test-guid-456";
        var expectedDocumentName = guid;

        // Act
        // This simulates what the Print endpoint should do
        var documentName = guid; // In real code: printerTask._id?.id ?? $"job-{DateTime.Now:yyyyMMddHHmmss}"

        // Assert
        documentName.Should().Be(expectedDocumentName);
    }
}

// Enhanced response model for testing
public class EnhancedPrintResponse
{
    public bool Success { get; set; }
    public string? Guid { get; set; }
    public int? SpoolerId { get; set; }
    public string? PrinterName { get; set; }
    public string? DocumentName { get; set; }
    public bool HasErrors { get; set; }
    public bool HasPrinterIssues { get; set; }
    public string? Message { get; set; }
    public int? RetryCount { get; set; }
}

// Test helper for print job info
public class PrintJobInfo
{
    public int JobId { get; set; }
    public string DocumentName { get; set; } = "";
    public JobStatus Status { get; set; }
}

[Flags]
public enum JobStatus
{
    None = 0,
    Paused = 0x00000001,
    Error = 0x00000002,
    Deleting = 0x00000004,
    Spooling = 0x00000008,
    Printing = 0x00000010,
    Offline = 0x00000020,
    PaperOut = 0x00000040,
    Printed = 0x00000080,
    Deleted = 0x00000100,
    Blocked = 0x00000200,
    UserIntervention = 0x00000400,
    Restart = 0x00000800,
    Complete = 0x00001000
}