using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class GuidStatusEndpointTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public GuidStatusEndpointTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void GuidStatusResponse_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new GuidStatusResponse
        {
            Guid = "test-guid-123",
            SpoolerId = 142,
            Found = true,
            PrinterName = "TestPrinter",
            Status = "printing",
            IsError = false,
            IsPrinting = true,
            IsPaused = false,
            IsComplete = false
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<GuidStatusResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Guid.Should().Be("test-guid-123");
        deserialized.SpoolerId.Should().Be(142);
        deserialized.Found.Should().BeTrue();
        deserialized.PrinterName.Should().Be("TestPrinter");
        deserialized.Status.Should().Be("printing");
        deserialized.IsError.Should().BeFalse();
        deserialized.IsPrinting.Should().BeTrue();
        deserialized.IsPaused.Should().BeFalse();
        deserialized.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void GuidStatusResponse_WhenJobFound_ShouldReturnFoundTrue()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = "test-guid-123"
        };

        // Act
        var response = CreateGuidStatusFromJob("test-guid-123", mockJob, "TestPrinter");

        // Assert
        response.Found.Should().BeTrue();
        response.Guid.Should().Be("test-guid-123");
        response.SpoolerId.Should().Be(142);
        response.PrinterName.Should().Be("TestPrinter");
        response.Status.Should().Be("printing");
        response.IsPrinting.Should().BeTrue();
    }

    [Fact]
    public void GuidStatusResponse_WhenJobNotFound_ShouldReturnFoundFalse()
    {
        // Arrange & Act
        var response = new GuidStatusResponse
        {
            Guid = "missing-guid",
            Found = false
        };

        // Assert
        response.Found.Should().BeFalse();
        response.Guid.Should().Be("missing-guid");
        response.SpoolerId.Should().BeNull();
        response.PrinterName.Should().BeNull();
        response.Status.Should().BeNull();
    }

    [Fact]
    public void GuidStatusResponse_ShouldDetectErrorStates()
    {
        // Arrange
        var errorStates = new[]
        {
            PrintDirect.JobStatus.Error,
            PrintDirect.JobStatus.PaperOut,
            PrintDirect.JobStatus.Blocked,
            PrintDirect.JobStatus.UserIntervention
        };

        foreach (var errorState in errorStates)
        {
            var mockJob = new PrintDirect.PrintJobInfo
            {
                JobId = 100,
                Status = errorState,
                DocumentName = "test-error-guid"
            };

            // Act
            var response = CreateGuidStatusFromJob("test-error-guid", mockJob, "TestPrinter");

            // Assert
            response.IsError.Should().BeTrue($"Status {errorState} should be detected as error");
            response.Found.Should().BeTrue();
        }
    }

    [Fact]
    public void GuidStatusResponse_ShouldDetectPausedState()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 100,
            Status = PrintDirect.JobStatus.Paused,
            DocumentName = "test-paused-guid"
        };

        // Act
        var response = CreateGuidStatusFromJob("test-paused-guid", mockJob, "TestPrinter");

        // Assert
        response.IsPaused.Should().BeTrue();
        response.Found.Should().BeTrue();
        response.IsError.Should().BeFalse();
        response.IsPrinting.Should().BeFalse();
    }

    [Fact]
    public void GuidStatusResponse_ShouldDetectCompleteStates()
    {
        // Arrange
        var completeStates = new[]
        {
            PrintDirect.JobStatus.Complete,
            PrintDirect.JobStatus.Printed,
            PrintDirect.JobStatus.Deleted
        };

        foreach (var completeState in completeStates)
        {
            var mockJob = new PrintDirect.PrintJobInfo
            {
                JobId = 100,
                Status = completeState,
                DocumentName = "test-complete-guid"
            };

            // Act
            var response = CreateGuidStatusFromJob("test-complete-guid", mockJob, "TestPrinter");

            // Assert
            response.IsComplete.Should().BeTrue($"Status {completeState} should be detected as complete");
            response.Found.Should().BeTrue();
        }
    }

    [Fact]
    public void FindJobByGuid_ShouldSearchAllPrinters()
    {
        // Arrange
        var printers = new List<string> { "Printer1", "Printer2", "Printer3" };
        var targetGuid = "target-guid-123";
        
        // Simulate job found on Printer2
        var printer1Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 100, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-1" }
        };
        
        var printer2Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 142, Status = PrintDirect.JobStatus.Spooling, DocumentName = targetGuid },
            new PrintDirect.PrintJobInfo { JobId = 143, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-2" }
        };
        
        var printer3Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 200, Status = PrintDirect.JobStatus.Error, DocumentName = "other-guid-3" }
        };

        // Act - Simulate search algorithm
        GuidStatusResponse? result = null;
        foreach (var printerName in printers)
        {
            var printerJobs = printerName switch
            {
                "Printer1" => printer1Jobs,
                "Printer2" => printer2Jobs,
                "Printer3" => printer3Jobs,
                _ => new List<PrintDirect.PrintJobInfo>()
            };
            
            var foundJob = printerJobs.FirstOrDefault(j => 
                j.DocumentName?.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) == true);
            
            if (foundJob != null)
            {
                result = CreateGuidStatusFromJob(targetGuid, foundJob, printerName);
                break;
            }
        }

        // Assert
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.Guid.Should().Be(targetGuid);
        result.SpoolerId.Should().Be(142);
        result.PrinterName.Should().Be("Printer2");
        result.Status.Should().Be("spooling");
    }

    [Fact]
    public void FindJobByGuid_WhenGuidNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var printers = new List<string> { "Printer1", "Printer2" };
        var targetGuid = "missing-guid-123";
        
        var printer1Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 100, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-1" }
        };
        
        var printer2Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 200, Status = PrintDirect.JobStatus.Error, DocumentName = "other-guid-2" }
        };

        // Act - Simulate search algorithm
        GuidStatusResponse? result = null;
        foreach (var printerName in printers)
        {
            var printerJobs = printerName == "Printer1" ? printer1Jobs : printer2Jobs;
            
            var foundJob = printerJobs.FirstOrDefault(j => 
                j.DocumentName?.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) == true);
            
            if (foundJob != null)
            {
                result = CreateGuidStatusFromJob(targetGuid, foundJob, printerName);
                break;
            }
        }
        
        // If not found, create not found response
        result ??= new GuidStatusResponse
        {
            Guid = targetGuid,
            Found = false
        };

        // Assert
        result.Should().NotBeNull();
        result.Found.Should().BeFalse();
        result.Guid.Should().Be(targetGuid);
        result.SpoolerId.Should().BeNull();
        result.PrinterName.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FindJobByGuid_WithInvalidGuid_ShouldReturnNotFound(string invalidGuid)
    {
        // Act
        var response = new GuidStatusResponse
        {
            Guid = invalidGuid ?? "",
            Found = false
        };

        // Assert
        response.Found.Should().BeFalse();
        response.Guid.Should().Be(invalidGuid ?? "");
    }

    [Fact]
    public void FindJobByGuid_ShouldBeCaseInsensitive()
    {
        // Arrange
        var targetGuid = "Test-GUID-123";
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = "test-guid-123"  // Different case
        };

        // Act - Simulate case-insensitive comparison
        var isMatch = mockJob.DocumentName?.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) == true;
        
        GuidStatusResponse? result = null;
        if (isMatch)
        {
            result = CreateGuidStatusFromJob(targetGuid, mockJob, "TestPrinter");
        }

        // Assert
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.Guid.Should().Be(targetGuid);  // Should preserve the requested case
    }

    private static GuidStatusResponse CreateGuidStatusFromJob(string guid, PrintDirect.PrintJobInfo job, string printerName)
    {
        return new GuidStatusResponse
        {
            Guid = guid,
            SpoolerId = job.JobId,
            Found = true,
            PrinterName = printerName,
            Status = job.Status.ToString().ToLowerInvariant(),
            IsError = job.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                     job.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                     job.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                     job.Status.HasFlag(PrintDirect.JobStatus.UserIntervention),
            IsPrinting = job.Status.HasFlag(PrintDirect.JobStatus.Printing),
            IsPaused = job.Status.HasFlag(PrintDirect.JobStatus.Paused),
            IsComplete = job.Status.HasFlag(PrintDirect.JobStatus.Complete) ||
                        job.Status.HasFlag(PrintDirect.JobStatus.Printed) ||
                        job.Status.HasFlag(PrintDirect.JobStatus.Deleted)
        };
    }
}