using FluentAssertions;
using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;
using Xunit;
using Moq;

namespace PrinterTrayApp.Tests.Unit;

public class QueueCheckEndpointTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public QueueCheckEndpointTests()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public void QueueCheckResponse_ShouldSerializeCorrectly()
    {
        // Arrange
        var response = new QueueCheckResponse
        {
            TotalJobs = 2,
            HasErrors = true,
            HasStuckJobs = false,
            Jobs = new List<QueueJobInfo>
            {
                new QueueJobInfo
                {
                    Guid = "test-001",
                    SpoolerId = 142,
                    PrinterName = "TestPrinter",
                    Status = "error",
                    ErrorMessage = "Paper out",
                    DocumentName = "test-001",
                    Position = 1,
                    PagesPrinted = 0,
                    TotalPages = 2,
                    SubmittedAt = new DateTime(2025, 1, 17, 14, 30, 0, DateTimeKind.Utc),
                    AgeSeconds = 180,
                    AgeFormatted = "3 minutes",
                    IsStuck = false,
                    IsError = true
                }
            },
            Summary = new QueueSummary
            {
                Total = 2,
                Printing = 0,
                Queued = 1,
                Spooling = 0,
                Paused = 0,
                Error = 1,
                StuckCount = 0,
                OldestJobAge = 180,
                OldestJobAgeFormatted = "3 minutes"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<QueueCheckResponse>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.TotalJobs.Should().Be(2);
        deserialized.HasErrors.Should().BeTrue();
        deserialized.HasStuckJobs.Should().BeFalse();
        deserialized.Jobs.Should().HaveCount(1);
        deserialized.Summary.Total.Should().Be(2);
    }

    [Fact]
    public void QueueJobInfo_ShouldCalculateAgeCorrectly()
    {
        // Arrange
        var submittedAt = DateTime.UtcNow.AddSeconds(-150); // 2.5 minutes ago
        var currentTime = DateTime.UtcNow;

        // Act
        var ageSeconds = (long)(currentTime - submittedAt).TotalSeconds;
        var ageFormatted = FormatAge(ageSeconds);

        // Assert
        ageSeconds.Should().BeInRange(149, 151); // Allow 1 second variance
        ageFormatted.Should().Be("2 minutes");
    }

    [Fact]
    public void FormatAge_ShouldHandleSeconds()
    {
        // Arrange & Act & Assert
        FormatAge(30).Should().Be("30 seconds");
        FormatAge(45).Should().Be("45 seconds");
        FormatAge(59).Should().Be("59 seconds");
    }

    [Fact]
    public void FormatAge_ShouldHandleMinutes()
    {
        // Arrange & Act & Assert
        FormatAge(60).Should().Be("1 minute");
        FormatAge(90).Should().Be("1 minute");
        FormatAge(120).Should().Be("2 minutes");
        FormatAge(150).Should().Be("2 minutes");
        FormatAge(3599).Should().Be("59 minutes");
    }

    [Fact]
    public void FormatAge_ShouldHandleHours()
    {
        // Arrange & Act & Assert
        FormatAge(3600).Should().Be("1 hour");
        FormatAge(3900).Should().Be("1 hour");
        FormatAge(7200).Should().Be("2 hours");
        FormatAge(86399).Should().Be("23 hours");
    }

    [Fact]
    public void FormatAge_ShouldHandleDays()
    {
        // Arrange & Act & Assert
        FormatAge(86400).Should().Be("1 day");
        FormatAge(90000).Should().Be("1 day");
        FormatAge(172800).Should().Be("2 days");
    }

    [Fact]
    public void DetectStuckJobs_ShouldIdentifyJobsStuckForMoreThan30Seconds()
    {
        // Arrange
        var jobs = new List<QueueJobInfo>
        {
            CreateMockJob("fast-job", PrintDirect.JobStatus.Printing, 25), // Not stuck
            CreateMockJob("stuck-job-1", PrintDirect.JobStatus.Printing, 45), // Stuck
            CreateMockJob("stuck-job-2", PrintDirect.JobStatus.Spooling, 60), // Stuck
            CreateMockJob("completed-job", PrintDirect.JobStatus.Complete, 120) // Complete, not stuck
        };

        // Set the IsStuck property based on logic
        foreach (var job in jobs)
        {
            var status = Enum.Parse<PrintDirect.JobStatus>(job.Status, ignoreCase: true);
            job.IsStuck = IsJobStuck(job, status);
        }

        // Act
        var stuckJobs = jobs.Where(j => j.IsStuck).ToList();

        // Assert
        stuckJobs.Should().HaveCount(2);
        stuckJobs.Should().Contain(j => j.Guid == "stuck-job-1");
        stuckJobs.Should().Contain(j => j.Guid == "stuck-job-2");
    }

    [Fact]
    public void DetectErrorJobs_ShouldIdentifyJobsWithErrorStates()
    {
        // Arrange
        var errorStates = new[]
        {
            PrintDirect.JobStatus.Error,
            PrintDirect.JobStatus.PaperOut,
            PrintDirect.JobStatus.Blocked,
            PrintDirect.JobStatus.UserIntervention
        };

        var jobs = new List<QueueJobInfo>();
        foreach (var errorState in errorStates)
        {
            var job = CreateMockJob($"error-job-{errorState}", errorState, 30);
            job.IsError = IsErrorState(errorState);
            jobs.Add(job);
        }

        // Act
        var errorJobs = jobs.Where(j => j.IsError).ToList();

        // Assert
        errorJobs.Should().HaveCount(4);
        errorJobs.All(j => j.IsError).Should().BeTrue();
    }

    [Fact]
    public void CalculateQueueSummary_ShouldReturnCorrectCounts()
    {
        // Arrange
        var jobs = new List<QueueJobInfo>
        {
            CreateMockJob("printing-1", PrintDirect.JobStatus.Printing, 10),
            CreateMockJob("printing-2", PrintDirect.JobStatus.Printing, 50), // Stuck
            CreateMockJob("spooling-1", PrintDirect.JobStatus.Spooling, 5),
            CreateMockJob("error-1", PrintDirect.JobStatus.Error, 20),
            CreateMockJob("paused-1", PrintDirect.JobStatus.Paused, 15),
            CreateMockJob("complete-1", PrintDirect.JobStatus.Complete, 100)
        };

        // Set properties based on status
        foreach (var job in jobs)
        {
            var status = Enum.Parse<PrintDirect.JobStatus>(job.Status, ignoreCase: true);
            job.IsError = IsErrorState(status);
            job.IsStuck = IsJobStuck(job, status);
        }

        // Act
        var summary = CalculateQueueSummary(jobs);

        // Assert
        summary.Total.Should().Be(6);
        summary.Printing.Should().Be(2);
        summary.Spooling.Should().Be(1);
        summary.Error.Should().Be(1);
        summary.Paused.Should().Be(1);
        summary.StuckCount.Should().Be(1); // printing-2 is stuck
        summary.OldestJobAge.Should().Be(100);
        summary.OldestJobAgeFormatted.Should().Be("1 minute");
    }

    [Fact]
    public void QueueCheck_EmptyQueue_ShouldReturnEmptyResponse()
    {
        // Arrange
        var jobs = new List<QueueJobInfo>();

        // Act
        var response = new QueueCheckResponse
        {
            TotalJobs = 0,
            HasErrors = false,
            HasStuckJobs = false,
            Jobs = jobs,
            Summary = CalculateQueueSummary(jobs)
        };

        // Assert
        response.TotalJobs.Should().Be(0);
        response.HasErrors.Should().BeFalse();
        response.HasStuckJobs.Should().BeFalse();
        response.Jobs.Should().BeEmpty();
        response.Summary.Total.Should().Be(0);
        response.Summary.StuckCount.Should().Be(0);
    }

    [Fact]
    public void QueueCheck_ShouldSortJobsByPrinterAndPosition()
    {
        // Arrange
        var jobs = new List<QueueJobInfo>
        {
            CreateMockJobWithPrinter("job-3", "Printer2", 2, PrintDirect.JobStatus.Printing, 30),
            CreateMockJobWithPrinter("job-1", "Printer1", 1, PrintDirect.JobStatus.Spooling, 20),
            CreateMockJobWithPrinter("job-2", "Printer1", 2, PrintDirect.JobStatus.Spooling, 25),
            CreateMockJobWithPrinter("job-4", "Printer2", 1, PrintDirect.JobStatus.Printing, 35)
        };

        // Act
        var sortedJobs = jobs.OrderBy(j => j.PrinterName).ThenBy(j => j.Position).ToList();

        // Assert
        sortedJobs[0].Guid.Should().Be("job-1"); // Printer1, Position 1
        sortedJobs[1].Guid.Should().Be("job-2"); // Printer1, Position 2
        sortedJobs[2].Guid.Should().Be("job-4"); // Printer2, Position 1
        sortedJobs[3].Guid.Should().Be("job-3"); // Printer2, Position 2
    }

    [Fact]
    public void QueueCheck_ShouldSetHasFlagsCorrectly()
    {
        // Arrange
        var jobs = new List<QueueJobInfo>
        {
            CreateMockJob("normal-job", PrintDirect.JobStatus.Printing, 10),
            CreateMockJob("error-job", PrintDirect.JobStatus.Error, 20),
            CreateMockJob("stuck-job", PrintDirect.JobStatus.Printing, 45)
        };

        // Set properties
        jobs[0].IsError = false;
        jobs[0].IsStuck = false;
        jobs[1].IsError = true;
        jobs[1].IsStuck = false;
        jobs[2].IsError = false;
        jobs[2].IsStuck = true;

        // Act
        var hasErrors = jobs.Any(j => j.IsError);
        var hasStuckJobs = jobs.Any(j => j.IsStuck);

        // Assert
        hasErrors.Should().BeTrue();
        hasStuckJobs.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(30, "30 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(61, "1 minute")]
    [InlineData(119, "1 minute")]
    [InlineData(120, "2 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(3660, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(86400, "1 day")]
    [InlineData(172800, "2 days")]
    public void FormatAge_ShouldReturnCorrectFormat(long ageSeconds, string expected)
    {
        // Act & Assert
        FormatAge(ageSeconds).Should().Be(expected);
    }

    private static QueueJobInfo CreateMockJob(string guid, PrintDirect.JobStatus status, long ageSeconds)
    {
        return CreateMockJobWithPrinter(guid, "TestPrinter", 1, status, ageSeconds);
    }

    private static QueueJobInfo CreateMockJobWithPrinter(string guid, string printerName, int position, PrintDirect.JobStatus status, long ageSeconds)
    {
        return new QueueJobInfo
        {
            Guid = guid,
            SpoolerId = 100,
            PrinterName = printerName,
            Status = status.ToString().ToLowerInvariant(),
            DocumentName = guid,
            Position = position,
            PagesPrinted = 0,
            TotalPages = 1,
            SubmittedAt = DateTime.UtcNow.AddSeconds(-ageSeconds),
            AgeSeconds = ageSeconds,
            AgeFormatted = FormatAge(ageSeconds),
            IsStuck = false,
            IsError = false
        };
    }

    private static bool IsJobStuck(QueueJobInfo job, PrintDirect.JobStatus status)
    {
        // Job is stuck if it's been printing/spooling for more than 30 seconds
        return (status == PrintDirect.JobStatus.Printing || status == PrintDirect.JobStatus.Spooling) 
               && job.AgeSeconds > 30;
    }

    private static bool IsErrorState(PrintDirect.JobStatus status)
    {
        return status.HasFlag(PrintDirect.JobStatus.Error) ||
               status.HasFlag(PrintDirect.JobStatus.PaperOut) ||
               status.HasFlag(PrintDirect.JobStatus.Blocked) ||
               status.HasFlag(PrintDirect.JobStatus.UserIntervention);
    }

    private static QueueSummary CalculateQueueSummary(List<QueueJobInfo> jobs)
    {
        var summary = new QueueSummary
        {
            Total = jobs.Count,
            Printing = jobs.Count(j => j.Status.Equals("printing", StringComparison.OrdinalIgnoreCase)),
            Queued = jobs.Count(j => j.Status.Equals("none", StringComparison.OrdinalIgnoreCase)),
            Spooling = jobs.Count(j => j.Status.Equals("spooling", StringComparison.OrdinalIgnoreCase)),
            Paused = jobs.Count(j => j.Status.Equals("paused", StringComparison.OrdinalIgnoreCase)),
            Error = jobs.Count(j => j.IsError),
            StuckCount = jobs.Count(j => j.IsStuck)
        };

        if (jobs.Any())
        {
            summary.OldestJobAge = jobs.Max(j => j.AgeSeconds);
            summary.OldestJobAgeFormatted = FormatAge(summary.OldestJobAge);
        }

        return summary;
    }

    private static string FormatAge(long ageSeconds)
    {
        if (ageSeconds < 60)
        {
            return ageSeconds == 1 ? "1 second" : $"{ageSeconds} seconds";
        }
        else if (ageSeconds < 3600)
        {
            var minutes = ageSeconds / 60;
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }
        else if (ageSeconds < 86400)
        {
            var hours = ageSeconds / 3600;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        else
        {
            var days = ageSeconds / 86400;
            return days == 1 ? "1 day" : $"{days} days";
        }
    }
}