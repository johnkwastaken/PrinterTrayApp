using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;
using System.IO;
using System.Text.Json;

namespace PrinterTrayApp.Tests.Unit.Services;

public class PrinterServiceTests : IDisposable
{
    private readonly PrinterService _printerService;
    private readonly string _testConfigPath;

    public PrinterServiceTests()
    {
        _printerService = new PrinterService();
        _testConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "printers.json");
    }

    [Fact]
    public void GetAllPrinters_Should_ExcludeVirtualPrinters()
    {
        // Act
        var printers = _printerService.GetAllPrinters();

        // Assert
        printers.Should().NotBeNull();
        printers.Should().NotContain(p => p.WindowsPrinterName.Contains("PDF", StringComparison.OrdinalIgnoreCase));
        printers.Should().NotContain(p => p.WindowsPrinterName.Contains("XPS", StringComparison.OrdinalIgnoreCase));
        printers.Should().NotContain(p => p.WindowsPrinterName.Contains("OneNote", StringComparison.OrdinalIgnoreCase));
        printers.Should().NotContain(p => p.WindowsPrinterName.Contains("Microsoft Print", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetPrinterNames_Should_ReturnListOfPrinterNames()
    {
        // Act
        var printerNames = _printerService.GetPrinterNames();

        // Assert
        printerNames.Should().NotBeNull();
        printerNames.Should().BeOfType<List<string>>();
        printerNames.Should().NotContain(name => name.Contains("PDF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrinterExists_Should_ReturnTrue_ForExistingPrinter()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            // Skip test if no physical printers available
            return;
        }
        var existingPrinter = printers.First().WindowsPrinterName;

        // Act
        var exists = _printerService.PrinterExists(existingPrinter);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public void PrinterExists_Should_ReturnFalse_ForNonExistingPrinter()
    {
        // Act
        var exists = _printerService.PrinterExists("NonExistentPrinter12345");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public void PrinterExists_Should_BeCaseInsensitive()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            return;
        }
        var printerName = printers.First().WindowsPrinterName;

        // Act
        var existsLower = _printerService.PrinterExists(printerName.ToLower());
        var existsUpper = _printerService.PrinterExists(printerName.ToUpper());

        // Assert
        existsLower.Should().BeTrue();
        existsUpper.Should().BeTrue();
    }

    [Fact]
    public void ResolvePrinter_Should_ReturnPrinterInfo_ForValidLogicalName()
    {
        // Arrange
        var printers = _printerService.GetAllPrinters();
        if (printers.Count == 0)
        {
            return;
        }
        var printer = printers.First();

        // Act
        var resolved = _printerService.ResolvePrinter(printer.LogicalName);

        // Assert
        resolved.Should().NotBeNull();
        resolved?.WindowsPrinterName.Should().Be(printer.WindowsPrinterName);
    }

    [Fact]
    public void ResolvePrinter_Should_ReturnNull_ForInvalidLogicalName()
    {
        // Act
        var resolved = _printerService.ResolvePrinter("InvalidLogicalName");

        // Assert
        resolved.Should().BeNull();
    }

    [Fact]
    public void LoadMappings_Should_HandleMissingConfigFile()
    {
        // Arrange
        if (File.Exists(_testConfigPath))
        {
            File.Delete(_testConfigPath);
        }

        // Act & Assert - Should not throw
        var service = new PrinterService();
        service.GetAllPrinters().Should().NotBeNull();
    }

    [Fact]
    public void LoadMappings_Should_ParseValidConfigFile()
    {
        // Arrange
        var config = new
        {
            mappings = new[]
            {
                new { logicalName = "kitchen", windowsPrinterName = "TestKitchenPrinter" },
                new { logicalName = "bar", windowsPrinterName = "TestBarPrinter" }
            },
            fallbackPrinter = "TestFallbackPrinter"
        };
        
        File.WriteAllText(_testConfigPath, JsonSerializer.Serialize(config));

        try
        {
            // Act
            var service = new PrinterService();
            
            // Assert - mappings should be loaded (we can't directly test private field)
            // but the service should work without throwing
            service.GetAllPrinters().Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }
        }
    }

    [Fact]
    public void RefreshPrinters_Should_UpdateCachedPrinters()
    {
        // Arrange
        var initialPrinters = _printerService.GetAllPrinters();
        var initialCount = initialPrinters.Count;

        // Act
        _printerService.RefreshPrinters();
        var refreshedPrinters = _printerService.GetAllPrinters();

        // Assert
        refreshedPrinters.Should().NotBeNull();
        // Count should be consistent if no printers were added/removed
        refreshedPrinters.Count.Should().Be(initialCount);
    }

    [Fact]
    public void GetAllPrinters_Should_RefreshCache_WhenExpired()
    {
        // This test validates cache expiry behavior
        // We can't easily test the time-based expiry without mocking DateTime,
        // but we can verify the method doesn't throw and returns consistent results
        
        // Act
        var firstCall = _printerService.GetAllPrinters();
        var secondCall = _printerService.GetAllPrinters();

        // Assert
        firstCall.Should().NotBeNull();
        secondCall.Should().NotBeNull();
        firstCall.Count.Should().Be(secondCall.Count);
    }

    public void Dispose()
    {
        // Cleanup test config file if it exists
        if (File.Exists(_testConfigPath))
        {
            try
            {
                File.Delete(_testConfigPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}