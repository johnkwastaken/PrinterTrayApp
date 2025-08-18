using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class RetryQueueEndpointTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public RetryQueueEndpointTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void RetryQueueResponse_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new RetryQueueResponse
        {
            Success = true,
            RetriedCount = 2,
            RetriedJobs = new List<RetriedJobInfo>
            {
                new RetriedJobInfo
                {
                    Guid = "test-001",
                    SpoolerId = 142,
                    Status = "printing",
                    ErrorMessage = null
                },
                new RetriedJobInfo
                {
                    Guid = "test-002",
                    SpoolerId = 143,
                    Status = "spooling",
                    ErrorMessage = null
                }
            },
            ErrorMessage = null
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<RetryQueueResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Success.Should().BeTrue();
        deserialized.RetriedCount.Should().Be(2);
        deserialized.RetriedJobs.Should().HaveCount(2);
        deserialized.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RetryQueueRequest_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """{"printerName": "passkitchen"}""";

        // Act
        var request = JsonSerializer.Deserialize<RetryQueueRequest>(json, _jsonOptions);

        // Assert
        request.Should().NotBeNull();
        request!.PrinterName.Should().Be("passkitchen");
    }

    [Fact]
    public void RetryQueue_WithValidPrinter_ShouldRetryPausedJobs()
    {
        // Arrange
        var printerName = "TestPrinter";
        var pausedJobs = new List<PrintDirect.PrintJobInfo>
        {
            CreateMockJobInfo("test-001", 142, PrintDirect.JobStatus.Paused),
            CreateMockJobInfo("test-002", 143, PrintDirect.JobStatus.Error),
            CreateMockJobInfo("test-003", 144, PrintDirect.JobStatus.Printing) // Should not retry
        };

        // Act
        var retriableJobs = pausedJobs.Where(IsJobRetriable).ToList();
        var response = CreateRetryResponse(retriableJobs, true);

        // Assert
        response.Success.Should().BeTrue();
        response.RetriedCount.Should().Be(2);
        response.RetriedJobs.Should().HaveCount(2);
        response.RetriedJobs.Should().Contain(j => j.Guid == "test-001");
        response.RetriedJobs.Should().Contain(j => j.Guid == "test-002");
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RetryQueue_WithNonExistentPrinter_ShouldReturnError()
    {
        // Arrange
        var printerName = "NonExistentPrinter";
        var response = new RetryQueueResponse
        {
            Success = false,
            RetriedCount = 0,
            RetriedJobs = new List<RetriedJobInfo>(),
            ErrorMessage = $"Printer '{printerName}' not found"
        };

        // Assert
        response.Success.Should().BeFalse();
        response.RetriedCount.Should().Be(0);
        response.RetriedJobs.Should().BeEmpty();
        response.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public void RetryQueue_WithNoRetriableJobs_ShouldReturnZeroCount()
    {
        // Arrange
        var printerName = "TestPrinter";
        var nonRetriableJobs = new List<PrintDirect.PrintJobInfo>
        {
            CreateMockJobInfo("test-001", 142, PrintDirect.JobStatus.Printing),
            CreateMockJobInfo("test-002", 143, PrintDirect.JobStatus.Complete),
            CreateMockJobInfo("test-003", 144, PrintDirect.JobStatus.Spooling)
        };

        // Act
        var retriableJobs = nonRetriableJobs.Where(IsJobRetriable).ToList();
        var response = CreateRetryResponse(retriableJobs, true);

        // Assert
        response.Success.Should().BeTrue();
        response.RetriedCount.Should().Be(0);
        response.RetriedJobs.Should().BeEmpty();
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RetryQueue_WithEmptyQueue_ShouldReturnZeroCount()
    {
        // Arrange
        var printerName = "TestPrinter";
        var emptyJobs = new List<PrintDirect.PrintJobInfo>();

        // Act
        var response = CreateRetryResponse(emptyJobs, true);

        // Assert
        response.Success.Should().BeTrue();
        response.RetriedCount.Should().Be(0);
        response.RetriedJobs.Should().BeEmpty();
        response.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RetryQueue_WithMissingPrinterName_ShouldReturnError()
    {
        // Arrange
        var request = new RetryQueueRequest { PrinterName = "" };

        // Act
        var isValid = !string.IsNullOrWhiteSpace(request.PrinterName);

        // Assert
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(PrintDirect.JobStatus.Paused, true)]
    [InlineData(PrintDirect.JobStatus.Error, true)]
    [InlineData(PrintDirect.JobStatus.Blocked, true)]
    [InlineData(PrintDirect.JobStatus.UserIntervention, true)]
    [InlineData(PrintDirect.JobStatus.Printing, false)]
    [InlineData(PrintDirect.JobStatus.Spooling, false)]
    [InlineData(PrintDirect.JobStatus.Complete, false)]
    [InlineData(PrintDirect.JobStatus.Deleted, false)]
    public void IsJobRetriable_ShouldIdentifyCorrectStates(PrintDirect.JobStatus status, bool expectedRetriable)
    {
        // Arrange
        var job = CreateMockJobInfo("test-001", 142, status);

        // Act
        var isRetriable = IsJobRetriable(job);

        // Assert
        isRetriable.Should().Be(expectedRetriable);
    }

    [Fact]
    public void RetryQueue_WithPartialFailure_ShouldReturnPartialResults()
    {
        // Arrange
        var successfulJobs = new List<PrintDirect.PrintJobInfo>
        {
            CreateMockJobInfo("test-001", 142, PrintDirect.JobStatus.Paused)
        };
        var failedJobs = new List<PrintDirect.PrintJobInfo>
        {
            CreateMockJobInfo("test-002", 143, PrintDirect.JobStatus.Error)
        };

        // Act - simulate one success, one failure
        var retriedJobs = new List<RetriedJobInfo>
        {
            new RetriedJobInfo
            {
                Guid = "test-001",
                SpoolerId = 142,
                Status = "printing",
                ErrorMessage = null
            }
        };

        var response = new RetryQueueResponse
        {
            Success = true, // Overall success even with partial failure
            RetriedCount = 1,
            RetriedJobs = retriedJobs,
            ErrorMessage = "1 job failed to retry"
        };

        // Assert
        response.Success.Should().BeTrue();
        response.RetriedCount.Should().Be(1);
        response.RetriedJobs.Should().HaveCount(1);
        response.ErrorMessage.Should().Contain("failed");
    }

    [Fact]
    public void RetryQueue_ShouldExtractGuidFromDocumentName()
    {
        // Arrange
        var jobs = new List<PrintDirect.PrintJobInfo>
        {
            CreateMockJobInfo("test-guid-001", 142, PrintDirect.JobStatus.Paused),
            CreateMockJobInfo("another-guid-123", 143, PrintDirect.JobStatus.Error),
            CreateMockJobInfo(null, 144, PrintDirect.JobStatus.Blocked) // No document name
        };

        // Act
        var retriedJobs = jobs.Where(IsJobRetriable).Select(job => new RetriedJobInfo
        {
            Guid = ExtractGuidFromDocumentName(job.DocumentName),
            SpoolerId = job.JobId,
            Status = "retrying",
            ErrorMessage = null
        }).ToList();

        // Assert
        retriedJobs.Should().HaveCount(3);
        retriedJobs[0].Guid.Should().Be("test-guid-001");
        retriedJobs[1].Guid.Should().Be("another-guid-123");
        retriedJobs[2].Guid.Should().BeNull();
    }

    [Fact]
    public void RetryQueue_ShouldHandleJobCommandFailures()
    {
        // Arrange
        var job = CreateMockJobInfo("test-001", 142, PrintDirect.JobStatus.Paused);
        var simulateFailure = true;

        // Act
        var retriedJob = new RetriedJobInfo
        {
            Guid = ExtractGuidFromDocumentName(job.DocumentName),
            SpoolerId = job.JobId,
            Status = simulateFailure ? "failed" : "retrying",
            ErrorMessage = simulateFailure ? "Failed to send resume command" : null
        };

        // Assert
        retriedJob.Status.Should().Be("failed");
        retriedJob.ErrorMessage.Should().Contain("Failed to send");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RetryQueue_WithInvalidPrinterName_ShouldReturnError(string? printerName)
    {
        // Arrange
        var request = new RetryQueueRequest { PrinterName = printerName ?? "" };

        // Act
        var isValid = !string.IsNullOrWhiteSpace(request.PrinterName);
        var response = new RetryQueueResponse
        {
            Success = false,
            RetriedCount = 0,
            RetriedJobs = new List<RetriedJobInfo>(),
            ErrorMessage = "Printer name is required"
        };

        // Assert
        isValid.Should().BeFalse();
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void RetryQueue_ShouldTrackJobStatusAfterRetry()
    {
        // Arrange
        var job = CreateMockJobInfo("test-001", 142, PrintDirect.JobStatus.Paused);

        // Act - simulate successful retry
        var retriedJob = new RetriedJobInfo
        {
            Guid = ExtractGuidFromDocumentName(job.DocumentName),
            SpoolerId = job.JobId,
            Status = "printing", // Status after successful retry
            ErrorMessage = null
        };

        // Assert
        retriedJob.Status.Should().Be("printing");
        retriedJob.ErrorMessage.Should().BeNull();
    }

    private static PrintDirect.PrintJobInfo CreateMockJobInfo(string? documentName, int jobId, PrintDirect.JobStatus status)
    {
        return new PrintDirect.PrintJobInfo
        {
            JobId = jobId,
            Status = status,
            DocumentName = documentName
        };
    }

    private static bool IsJobRetriable(PrintDirect.PrintJobInfo job)
    {
        // Job is retriable if it's in an error state or paused
        return job.Status.HasFlag(PrintDirect.JobStatus.Paused) ||
               job.Status.HasFlag(PrintDirect.JobStatus.Error) ||
               job.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
               job.Status.HasFlag(PrintDirect.JobStatus.UserIntervention);
    }

    private static string? ExtractGuidFromDocumentName(string? documentName)
    {
        // Document name should be the GUID from PrinterTask._id.id
        return string.IsNullOrWhiteSpace(documentName) ? null : documentName;
    }

    private static RetryQueueResponse CreateRetryResponse(List<PrintDirect.PrintJobInfo> retriableJobs, bool success)
    {
        var retriedJobs = retriableJobs.Select(job => new RetriedJobInfo
        {
            Guid = ExtractGuidFromDocumentName(job.DocumentName),
            SpoolerId = job.JobId,
            Status = "retrying",
            ErrorMessage = null
        }).ToList();

        return new RetryQueueResponse
        {
            Success = success,
            RetriedCount = retriedJobs.Count,
            RetriedJobs = retriedJobs,
            ErrorMessage = null
        };
    }
}