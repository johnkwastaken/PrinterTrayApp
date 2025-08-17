using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Tests.Unit.Services;

public class POS80CommandsTests
{
    [Fact]
    public void Initialize_Should_ReturnCorrectCommand()
    {
        // Assert
        POS80Commands.init.Should().Be("\x1B\x40");
    }

    [Fact]
    public void Cut_Should_ReturnCorrectCommand()
    {
        // Assert
        POS80Commands.cut.Should().Be("\n\n\n\x1D\x56\x01");
    }

    [Fact]
    public void Beep_Should_ReturnCorrectCommand()
    {
        // Assert
        POS80Commands.beep.Should().Be("\x1B\x70\x00\x32\x32");
    }

    [Fact]
    public void Align_Should_ContainAllAlignmentModes()
    {
        // Assert
        POS80Commands.align.Should().ContainKey(PrinterAlign.Left);
        POS80Commands.align.Should().ContainKey(PrinterAlign.Center);
        POS80Commands.align.Should().ContainKey(PrinterAlign.Right);
        
        POS80Commands.align[PrinterAlign.Left].Should().Be("\x1B\x61\x00");
        POS80Commands.align[PrinterAlign.Center].Should().Be("\x1B\x61\x01");
        POS80Commands.align[PrinterAlign.Right].Should().Be("\x1B\x61\x02");
    }

    [Fact]
    public void Size_Should_ContainAllSizeModes()
    {
        // Assert
        POS80Commands.size.Should().ContainKey(PrinterScale.Normal);
        POS80Commands.size.Should().ContainKey(PrinterScale.High);
        POS80Commands.size.Should().ContainKey(PrinterScale.Wide);
        POS80Commands.size.Should().ContainKey(PrinterScale.WideHigh);
        
        // Using ESC ! commands (matching POS system)
        POS80Commands.size[PrinterScale.Normal].Should().Be("\x1B\x21\x00");
        POS80Commands.size[PrinterScale.High].Should().Be("\x1B\x21\x10");
        POS80Commands.size[PrinterScale.Wide].Should().Be("\x1B\x21\x20");
        POS80Commands.size[PrinterScale.WideHigh].Should().Be("\x1B\x21\x30");
    }

    [Fact]
    public void FontFamily_Should_ContainAllFontFamilies()
    {
        // Assert
        POS80Commands.fontFamily.Should().ContainKey(PrinterFontFamily.A);
        POS80Commands.fontFamily.Should().ContainKey(PrinterFontFamily.B);
        POS80Commands.fontFamily.Should().ContainKey(PrinterFontFamily.C);
        
        POS80Commands.fontFamily[PrinterFontFamily.A].Should().Be("\x1B\x4D\x00");
        POS80Commands.fontFamily[PrinterFontFamily.B].Should().Be("\x1B\x4D\x01");
        POS80Commands.fontFamily[PrinterFontFamily.C].Should().Be("\x1B\x4D\x02");
    }

    [Fact]
    public void Bold_Should_ContainOnOffStates()
    {
        // Assert
        POS80Commands.bold.Should().ContainKey(true);
        POS80Commands.bold.Should().ContainKey(false);
        
        POS80Commands.bold[true].Should().Be("\x1B\x45\x01");
        POS80Commands.bold[false].Should().Be("\x1B\x45\x00");
    }

    [Fact]
    public void Underline_Should_ContainOnOffStates()
    {
        // Assert
        POS80Commands.underline.Should().ContainKey(true);
        POS80Commands.underline.Should().ContainKey(false);
        
        POS80Commands.underline[true].Should().Be("\x1B\x2D\x01");
        POS80Commands.underline[false].Should().Be("\x1B\x2D\x00");
    }

    [Fact]
    public void Inverse_Should_ContainOnOffStates()
    {
        // Assert
        POS80Commands.inverse.Should().ContainKey(true);
        POS80Commands.inverse.Should().ContainKey(false);
        
        POS80Commands.inverse[true].Should().Be("\x1D\x42\x01");
        POS80Commands.inverse[false].Should().Be("\x1D\x42\x00");
    }

    [Fact]
    public void FontColor_Should_ContainBlackAndRed()
    {
        // Assert
        POS80Commands.fontColor.Should().ContainKey(PrinterFontColor.Black);
        POS80Commands.fontColor.Should().ContainKey(PrinterFontColor.Red);
        
        POS80Commands.fontColor[PrinterFontColor.Black].Should().Be("\x1B\x72\x00");
        POS80Commands.fontColor[PrinterFontColor.Red].Should().Be("\x1B\x72\x01");
    }

    [Fact]
    public void OpenCashDrawer_Should_ContainAllPulseDurations()
    {
        // Assert
        POS80Commands.openCashDrawer.Should().ContainKey(PrinterPulse.Duration_100);
        POS80Commands.openCashDrawer.Should().ContainKey(PrinterPulse.Duration_200);
        POS80Commands.openCashDrawer.Should().ContainKey(PrinterPulse.Duration_300);
        POS80Commands.openCashDrawer.Should().ContainKey(PrinterPulse.Duration_400);
        POS80Commands.openCashDrawer.Should().ContainKey(PrinterPulse.Duration_500);
        
        POS80Commands.openCashDrawer[PrinterPulse.Duration_100].Should().Be("\x1B\x70\x00\x32\x32");
        POS80Commands.openCashDrawer[PrinterPulse.Duration_500].Should().Be("\x1B\x70\x00\xFA\xFA");
    }

    [Fact]
    public void BarcodeType_Should_ContainCommonTypes()
    {
        // Assert
        POS80Commands.barcodeType.Should().ContainKey(PrinterBarcodeType.Code39);
        POS80Commands.barcodeType.Should().ContainKey(PrinterBarcodeType.Code128);
        POS80Commands.barcodeType.Should().ContainKey(PrinterBarcodeType.EAN13);
        POS80Commands.barcodeType.Should().ContainKey(PrinterBarcodeType.QRCode);
        
        POS80Commands.barcodeType[PrinterBarcodeType.Code39].Should().Be("\x04");
        POS80Commands.barcodeType[PrinterBarcodeType.Code128].Should().Be("\x49");
        POS80Commands.barcodeType[PrinterBarcodeType.EAN13].Should().Be("\x02");
    }

    [Fact]
    public void Feed_Should_ReturnLineFeedCommand()
    {
        // Assert
        POS80Commands.feed.Should().Be("\x0A");
    }

    [Fact]
    public void Commands_Should_UseStringFormat()
    {
        // All commands should be strings (not byte arrays) to match POS system
        
        // Assert
        POS80Commands.init.Should().BeOfType<string>();
        POS80Commands.cut.Should().BeOfType<string>();
        POS80Commands.beep.Should().BeOfType<string>();
        POS80Commands.feed.Should().BeOfType<string>();
        
        foreach (var cmd in POS80Commands.align.Values)
        {
            cmd.Should().BeOfType<string>();
        }
        
        foreach (var cmd in POS80Commands.size.Values)
        {
            cmd.Should().BeOfType<string>();
        }
    }

    [Fact]
    public void Commands_Should_NotContainNullValues()
    {
        // Assert
        POS80Commands.init.Should().NotBeNull();
        POS80Commands.cut.Should().NotBeNull();
        POS80Commands.beep.Should().NotBeNull();
        POS80Commands.feed.Should().NotBeNull();
        
        POS80Commands.align.Values.Should().NotContainNulls();
        POS80Commands.size.Values.Should().NotContainNulls();
        POS80Commands.fontFamily.Values.Should().NotContainNulls();
        POS80Commands.bold.Values.Should().NotContainNulls();
        POS80Commands.underline.Values.Should().NotContainNulls();
        POS80Commands.inverse.Values.Should().NotContainNulls();
        POS80Commands.fontColor.Values.Should().NotContainNulls();
        POS80Commands.openCashDrawer.Values.Should().NotContainNulls();
        POS80Commands.barcodeType.Values.Should().NotContainNulls();
    }

    [Fact]
    public void Size_Commands_Should_Use_ESC_Exclamation_Format()
    {
        // Verify we're using ESC ! (0x1B 0x21) not GS ! (0x1D 0x21)
        // This matches the POS system implementation
        
        foreach (var sizeCmd in POS80Commands.size.Values)
        {
            sizeCmd.Should().StartWith("\x1B\x21", "should use ESC ! format, not GS !");
        }
    }
}