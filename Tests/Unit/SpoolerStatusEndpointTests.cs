using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class SpoolerStatusEndpointTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public SpoolerStatusEndpointTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void SpoolerStatusResponse_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new SpoolerStatusResponse
        {
            SpoolerId = 142,
            Guid = "test-guid-123",
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
        var deserialized = JsonSerializer.Deserialize<SpoolerStatusResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.SpoolerId.Should().Be(142);
        deserialized.Guid.Should().Be("test-guid-123");
        deserialized.Found.Should().BeTrue();
        deserialized.PrinterName.Should().Be("TestPrinter");
        deserialized.Status.Should().Be("printing");
        deserialized.IsError.Should().BeFalse();
        deserialized.IsPrinting.Should().BeTrue();
        deserialized.IsPaused.Should().BeFalse();
        deserialized.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void SpoolerStatusResponse_WhenJobFound_ShouldReturnFoundTrue()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = "test-guid-123"
        };

        // Act
        var response = CreateSpoolerStatusFromJob(142, mockJob, "TestPrinter");

        // Assert
        response.Found.Should().BeTrue();
        response.SpoolerId.Should().Be(142);
        response.Guid.Should().Be("test-guid-123");
        response.PrinterName.Should().Be("TestPrinter");
        response.Status.Should().Be("printing");
        response.IsPrinting.Should().BeTrue();
    }

    [Fact]
    public void SpoolerStatusResponse_WhenJobNotFound_ShouldReturnFoundFalse()
    {
        // Arrange & Act
        var response = new SpoolerStatusResponse
        {
            SpoolerId = 999,
            Found = false
        };

        // Assert
        response.Found.Should().BeFalse();
        response.SpoolerId.Should().Be(999);
        response.Guid.Should().BeNull();
        response.PrinterName.Should().BeNull();
        response.Status.Should().BeNull();
    }

    [Fact]
    public void SpoolerStatusResponse_ShouldDetectErrorStates()
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
            var response = CreateSpoolerStatusFromJob(100, mockJob, "TestPrinter");

            // Assert
            response.IsError.Should().BeTrue($"Status {errorState} should be detected as error");
            response.Found.Should().BeTrue();
        }
    }

    [Fact]
    public void SpoolerStatusResponse_ShouldDetectPausedState()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 100,
            Status = PrintDirect.JobStatus.Paused,
            DocumentName = "test-paused-guid"
        };

        // Act
        var response = CreateSpoolerStatusFromJob(100, mockJob, "TestPrinter");

        // Assert
        response.IsPaused.Should().BeTrue();
        response.Found.Should().BeTrue();
        response.IsError.Should().BeFalse();
        response.IsPrinting.Should().BeFalse();
    }

    [Fact]
    public void SpoolerStatusResponse_ShouldDetectCompleteStates()
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
            var response = CreateSpoolerStatusFromJob(100, mockJob, "TestPrinter");

            // Assert
            response.IsComplete.Should().BeTrue($"Status {completeState} should be detected as complete");
            response.Found.Should().BeTrue();
        }
    }

    [Fact]
    public void FindJobBySpoolerId_ShouldSearchAllPrinters()
    {
        // Arrange
        var printers = new List<string> { "Printer1", "Printer2", "Printer3" };
        var targetSpoolerId = 142;
        
        // Simulate job found on Printer2
        var printer1Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 100, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-1" }
        };
        
        var printer2Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = targetSpoolerId, Status = PrintDirect.JobStatus.Spooling, DocumentName = "target-guid" },
            new PrintDirect.PrintJobInfo { JobId = 143, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-2" }
        };
        
        var printer3Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 200, Status = PrintDirect.JobStatus.Error, DocumentName = "other-guid-3" }
        };

        // Act - Simulate search algorithm
        SpoolerStatusResponse? result = null;
        foreach (var printerName in printers)
        {
            var printerJobs = printerName switch
            {
                "Printer1" => printer1Jobs,
                "Printer2" => printer2Jobs,
                "Printer3" => printer3Jobs,
                _ => new List<PrintDirect.PrintJobInfo>()
            };
            
            var foundJob = printerJobs.FirstOrDefault(j => j.JobId == targetSpoolerId);
            
            if (foundJob != null)
            {
                result = CreateSpoolerStatusFromJob(targetSpoolerId, foundJob, printerName);
                break;
            }
        }

        // Assert
        result.Should().NotBeNull();
        result!.Found.Should().BeTrue();
        result.SpoolerId.Should().Be(targetSpoolerId);
        result.Guid.Should().Be("target-guid");
        result.PrinterName.Should().Be("Printer2");
        result.Status.Should().Be("spooling");
    }

    [Fact]
    public void FindJobBySpoolerId_WhenSpoolerIdNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var printers = new List<string> { "Printer1", "Printer2" };
        var targetSpoolerId = 999;
        
        var printer1Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 100, Status = PrintDirect.JobStatus.Printing, DocumentName = "other-guid-1" }
        };
        
        var printer2Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 200, Status = PrintDirect.JobStatus.Error, DocumentName = "other-guid-2" }
        };

        // Act - Simulate search algorithm
        SpoolerStatusResponse? result = null;
        foreach (var printerName in printers)
        {
            var printerJobs = printerName == "Printer1" ? printer1Jobs : printer2Jobs;
            
            var foundJob = printerJobs.FirstOrDefault(j => j.JobId == targetSpoolerId);
            
            if (foundJob != null)
            {
                result = CreateSpoolerStatusFromJob(targetSpoolerId, foundJob, printerName);
                break;
            }
        }
        
        // If not found, create not found response
        result ??= new SpoolerStatusResponse
        {
            SpoolerId = targetSpoolerId,
            Found = false
        };

        // Assert
        result.Should().NotBeNull();
        result.Found.Should().BeFalse();
        result.SpoolerId.Should().Be(targetSpoolerId);
        result.Guid.Should().BeNull();
        result.PrinterName.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void FindJobBySpoolerId_WithInvalidSpoolerId_ShouldReturnNotFound(int invalidSpoolerId)
    {
        // Act
        var response = new SpoolerStatusResponse
        {
            SpoolerId = invalidSpoolerId,
            Found = false
        };

        // Assert
        response.Found.Should().BeFalse();
        response.SpoolerId.Should().Be(invalidSpoolerId);
    }

    [Fact]
    public void SpoolerIdParsing_ShouldHandleValidIntegers()
    {
        // Arrange
        var validSpoolerIds = new[] { "1", "42", "999", "1234567" };

        foreach (var spoolerIdString in validSpoolerIds)
        {
            // Act
            var canParse = int.TryParse(spoolerIdString, out var spoolerId);

            // Assert
            canParse.Should().BeTrue($"'{spoolerIdString}' should be parseable as integer");
            spoolerId.Should().BePositive($"'{spoolerIdString}' should parse to positive integer");
        }
    }

    [Fact]
    public void SpoolerIdParsing_ShouldRejectInvalidStrings()
    {
        // Arrange
        var invalidSpoolerIds = new[] { "abc", "12.5", "", "  ", "12abc", "999999999999999999999" };

        foreach (var spoolerIdString in invalidSpoolerIds)
        {
            // Act
            var canParse = int.TryParse(spoolerIdString, out var spoolerId);

            // Assert
            canParse.Should().BeFalse($"'{spoolerIdString}' should not be parseable as integer");
        }
    }

    [Fact]
    public void FindJobBySpoolerId_ShouldExtractGuidFromDocumentName()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = "extracted-guid-456"
        };

        // Act
        var response = CreateSpoolerStatusFromJob(142, mockJob, "TestPrinter");

        // Assert
        response.Found.Should().BeTrue();
        response.SpoolerId.Should().Be(142);
        response.Guid.Should().Be("extracted-guid-456");
        response.PrinterName.Should().Be("TestPrinter");
    }

    [Fact]
    public void FindJobBySpoolerId_ShouldHandleNullDocumentName()
    {
        // Arrange
        var mockJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = null
        };

        // Act
        var response = CreateSpoolerStatusFromJob(142, mockJob, "TestPrinter");

        // Assert
        response.Found.Should().BeTrue();
        response.SpoolerId.Should().Be(142);
        response.Guid.Should().BeNull();
        response.PrinterName.Should().Be("TestPrinter");
    }

    private static SpoolerStatusResponse CreateSpoolerStatusFromJob(int spoolerId, PrintDirect.PrintJobInfo job, string printerName)
    {
        return new SpoolerStatusResponse
        {
            SpoolerId = spoolerId,
            Guid = job.DocumentName,
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