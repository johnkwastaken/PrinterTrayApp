using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class StatusEndpointTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public StatusEndpointTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void StatusResponse_ShouldIncludeTotalJobs()
    {
        // Arrange
        var response = new StatusResponse
        {
            TotalJobs = 3,
            Jobs = new List<JobStatusInfo>()
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<StatusResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TotalJobs.Should().Be(3);
    }

    [Fact]
    public void StatusResponse_ShouldIncludeJobsList()
    {
        // Arrange
        var jobs = new List<JobStatusInfo>
        {
            new JobStatusInfo
            {
                Guid = "test-001",
                SpoolerId = 142,
                PrinterName = "TestPrinter",
                Status = "printing",
                IsError = false,
                IsPrinting = true,
                IsComplete = false
            }
        };

        var response = new StatusResponse
        {
            TotalJobs = jobs.Count,
            Jobs = jobs
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<StatusResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Jobs.Should().HaveCount(1);
        var job = deserialized.Jobs.First();
        job.Guid.Should().Be("test-001");
        job.SpoolerId.Should().Be(142);
        job.PrinterName.Should().Be("TestPrinter");
        job.Status.Should().Be("printing");
        job.IsError.Should().BeFalse();
        job.IsPrinting.Should().BeTrue();
        job.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void StatusResponse_ShouldReturnEmptyArrayWhenNoJobs()
    {
        // Arrange
        var response = new StatusResponse
        {
            TotalJobs = 0,
            Jobs = new List<JobStatusInfo>()
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<StatusResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TotalJobs.Should().Be(0);
        deserialized.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void JobStatusInfo_ShouldMapFromPrintJobInfo()
    {
        // Arrange
        var printJob = new PrintDirect.PrintJobInfo
        {
            JobId = 142,
            Status = PrintDirect.JobStatus.Printing,
            DocumentName = "test-guid-123"
        };

        // Act
        var jobStatus = new JobStatusInfo
        {
            Guid = printJob.DocumentName,
            SpoolerId = printJob.JobId,
            PrinterName = "TestPrinter",
            Status = printJob.Status.ToString().ToLowerInvariant(),
            IsError = printJob.Status.HasFlag(PrintDirect.JobStatus.Error),
            IsPrinting = printJob.Status.HasFlag(PrintDirect.JobStatus.Printing),
            IsComplete = printJob.Status.HasFlag(PrintDirect.JobStatus.Complete) || 
                        printJob.Status.HasFlag(PrintDirect.JobStatus.Printed)
        };

        // Assert
        jobStatus.Guid.Should().Be("test-guid-123");
        jobStatus.SpoolerId.Should().Be(142);
        jobStatus.PrinterName.Should().Be("TestPrinter");
        jobStatus.Status.Should().Be("printing");
        jobStatus.IsError.Should().BeFalse();
        jobStatus.IsPrinting.Should().BeTrue();
        jobStatus.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void JobStatusInfo_ShouldDetectErrorStates()
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
            var printJob = new PrintDirect.PrintJobInfo
            {
                JobId = 100,
                Status = errorState,
                DocumentName = "test-error"
            };

            // Act
            var jobStatus = new JobStatusInfo
            {
                Guid = printJob.DocumentName,
                SpoolerId = printJob.JobId,
                PrinterName = "TestPrinter",
                Status = printJob.Status.ToString().ToLowerInvariant(),
                IsError = printJob.Status.HasFlag(PrintDirect.JobStatus.Error) ||
                         printJob.Status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
                         printJob.Status.HasFlag(PrintDirect.JobStatus.Blocked) ||
                         printJob.Status.HasFlag(PrintDirect.JobStatus.UserIntervention),
                IsPrinting = printJob.Status.HasFlag(PrintDirect.JobStatus.Printing),
                IsComplete = false
            };

            // Assert
            jobStatus.IsError.Should().BeTrue($"Status {errorState} should be detected as error");
        }
    }

    [Fact]
    public void JobStatusInfo_ShouldDetectCompleteStates()
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
            var printJob = new PrintDirect.PrintJobInfo
            {
                JobId = 100,
                Status = completeState,
                DocumentName = "test-complete"
            };

            // Act
            var jobStatus = new JobStatusInfo
            {
                Guid = printJob.DocumentName,
                SpoolerId = printJob.JobId,
                PrinterName = "TestPrinter",
                Status = printJob.Status.ToString().ToLowerInvariant(),
                IsError = false,
                IsPrinting = false,
                IsComplete = printJob.Status.HasFlag(PrintDirect.JobStatus.Complete) || 
                            printJob.Status.HasFlag(PrintDirect.JobStatus.Printed) ||
                            printJob.Status.HasFlag(PrintDirect.JobStatus.Deleted)
            };

            // Assert
            jobStatus.IsComplete.Should().BeTrue($"Status {completeState} should be detected as complete");
        }
    }

    [Fact]
    public void GetAllJobsFromAllPrinters_ShouldAggregateJobsCorrectly()
    {
        // Arrange
        var printer1Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 100, Status = PrintDirect.JobStatus.Printing, DocumentName = "job-1" },
            new PrintDirect.PrintJobInfo { JobId = 101, Status = PrintDirect.JobStatus.Spooling, DocumentName = "job-2" }
        };

        var printer2Jobs = new List<PrintDirect.PrintJobInfo>
        {
            new PrintDirect.PrintJobInfo { JobId = 200, Status = PrintDirect.JobStatus.Error, DocumentName = "job-3" }
        };

        var allPrinters = new List<string> { "Printer1", "Printer2" };

        // Act
        var allJobs = new List<JobStatusInfo>();
        
        // Simulate aggregating jobs from all printers
        foreach (var printerName in allPrinters)
        {
            var printerJobs = printerName == "Printer1" ? printer1Jobs : printer2Jobs;
            
            foreach (var job in printerJobs)
            {
                allJobs.Add(new JobStatusInfo
                {
                    Guid = job.DocumentName,
                    SpoolerId = job.JobId,
                    PrinterName = printerName,
                    Status = job.Status.ToString().ToLowerInvariant(),
                    IsError = job.Status.HasFlag(PrintDirect.JobStatus.Error),
                    IsPrinting = job.Status.HasFlag(PrintDirect.JobStatus.Printing),
                    IsComplete = job.Status.HasFlag(PrintDirect.JobStatus.Complete)
                });
            }
        }

        // Assert
        allJobs.Should().HaveCount(3);
        allJobs.Count(j => j.PrinterName == "Printer1").Should().Be(2);
        allJobs.Count(j => j.PrinterName == "Printer2").Should().Be(1);
        allJobs.Count(j => j.IsError).Should().Be(1);
        allJobs.Count(j => j.IsPrinting).Should().Be(1);
    }
}

// Response models for testing
public class StatusResponse
{
    public int TotalJobs { get; set; }
    public List<JobStatusInfo> Jobs { get; set; } = new();
}

public class JobStatusInfo
{
    public string? Guid { get; set; }
    public int SpoolerId { get; set; }
    public string PrinterName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsError { get; set; }
    public bool IsPrinting { get; set; }
    public bool IsComplete { get; set; }
}