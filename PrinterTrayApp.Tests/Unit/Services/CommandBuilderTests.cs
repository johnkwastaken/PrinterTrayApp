using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;
using System.Xml;
using System.Text;

namespace PrinterTrayApp.Tests.Unit.Services;

public class CommandBuilderTests
{
    private CommandBuilder CreateBuilder(PrinterPaperWidth width = PrinterPaperWidth.Paper_80)
    {
        return new CommandBuilder(width);
    }

    [Fact]
    public void Build_Should_ReturnStringCommands()
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddText("Test");

        // Act
        var result = builder.Build();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<string>();
        result.Should().Contain("Test");
    }

    [Fact]
    public void AddText_Should_AppendTextToCommands()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddText("Hello World");
        var result = builder.Build();

        // Assert
        result.Should().Contain("Hello World");
    }

    [Fact]
    public void AddText_WithAlignment_Should_ApplyAlignmentCommands()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddText("Center Text", PrinterAlign.Center);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.align[PrinterAlign.Center]);
        result.Should().Contain("Center Text");
    }

    [Fact]
    public void AddText_WithSize_Should_ApplySizeCommands()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddText("Large Text", size: PrinterScale.WideHigh);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.size[PrinterScale.WideHigh]);
        result.Should().Contain("Large Text");
    }

    [Fact]
    public void AddText_WithBold_Should_ApplyBoldCommands()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddText("Bold Text", bold: true);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.bold[true]);
        result.Should().Contain("Bold Text");
        result.Should().Contain(POS80Commands.bold[false]); // Reset bold
    }

    [Fact]
    public void AddNewLine_Should_AddLineBreak()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddText("Line 1");
        builder.AddNewLine();
        builder.AddText("Line 2");
        var result = builder.Build();

        // Assert
        result.Should().Contain("Line 1\n");
        result.Should().Contain("Line 2");
    }

    [Fact]
    public void AddSeparator_Should_AddSeparatorLine()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddSeparator();
        var result = builder.Build();

        // Assert
        result.Should().Contain("----------------------------------------");
    }

    [Fact]
    public void AddSeparator_WithCustomChar_Should_UseCustomCharacter()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddSeparator('=');
        var result = builder.Build();

        // Assert
        result.Should().Contain("========================================");
    }

    [Fact]
    public void AddBlankLines_Should_AddMultipleLineBreaks()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddBlankLines(3);
        var result = builder.Build();

        // Assert
        result.Should().Contain("\n\n\n");
    }

    [Fact]
    public void CutPaper_Should_AddCutCommand()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.CutPaper();
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.cut);
    }

    [Fact]
    public void OpenCashDrawer_Should_AddCashDrawerCommand()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.OpenCashDrawer();
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.openCashDrawer[PrinterPulse.Duration_100]);
    }

    [Fact]
    public void ProcessXmlDocument_Should_ProcessTextElements()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text>Simple Text</text>
            <text align=""center"">Centered Text</text>
            <text size=""wide"">Wide Text</text>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("Simple Text");
        result.Should().Contain("Centered Text");
        result.Should().Contain("Wide Text");
    }

    [Fact]
    public void ProcessXmlDocument_Should_ProcessCommandElements()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <command cmd=""cut"" />
            <command cmd=""opencashdrawer"" />
            <command cmd=""beep"" />
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.cut);
        result.Should().Contain(POS80Commands.openCashDrawer[PrinterPulse.Duration_100]);
        result.Should().Contain(POS80Commands.beep);
    }

    [Fact]
    public void ProcessXmlDocument_Should_ProcessBlankElements()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <blank />
            <blank lines=""3"" />
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("\n");
    }

    [Fact]
    public void ProcessXmlDocument_Should_ProcessSeparatorElements()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <separator />
            <separator char=""="" />
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("----------------------------------------");
        result.Should().Contain("========================================");
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleFontFamily()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text font-family=""a"">Font A</text>
            <text font-family=""b"">Font B</text>
            <text font-family=""c"">Font C</text>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.A]);
        result.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.B]);
        result.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.C]);
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleTextStyles()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text font-weight=""bold"">Bold Text</text>
            <text font-style=""underline"">Underlined Text</text>
            <text font-style=""inverse"">Inverted Text</text>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain(POS80Commands.bold[true]);
        result.Should().Contain(POS80Commands.underline[true]);
        result.Should().Contain(POS80Commands.inverse[true]);
    }

    [Fact]
    public void ProcessXmlDocument_Should_NotApplyFontColor_WhenNotSpecified()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text>Text without color</text>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        // Should not contain any color commands
        result.Should().NotContain("\x1B\x72");
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleBarcode()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <barcode type=""code39"">123456</barcode>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("123456");
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleQRCode()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <qrcode>https://example.com</qrcode>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("https://example.com");
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleTableWithColumns()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <table>
                <column width=""50"">Item</column>
                <column width=""25"" align=""right"">Qty</column>
                <column width=""25"" align=""right"">Price</column>
            </table>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("Item");
        result.Should().Contain("Qty");
        result.Should().Contain("Price");
    }

    [Fact]
    public void Build_Should_ResetStateAfterEachText()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text font-weight=""bold"" size=""wide"">Bold Wide Text</text>
            <text>Normal Text</text>
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        // Should contain reset commands after bold/wide text
        result.Should().Contain(POS80Commands.bold[false]);
        result.Should().Contain(POS80Commands.size[PrinterScale.Normal]);
    }

    [Fact]
    public void ProcessXmlDocument_Should_HandleComplexDocument()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <text align=""center"" size=""wide-high"">RECEIPT</text>
            <separator />
            <text>Date: 2024-01-01</text>
            <blank lines=""2"" />
            <table>
                <column width=""60"">Burger</column>
                <column width=""20"" align=""right"">2</column>
                <column width=""20"" align=""right"">$20.00</column>
            </table>
            <separator char=""="" />
            <text align=""right"" font-weight=""bold"">Total: $20.00</text>
            <blank lines=""3"" />
            <command cmd=""cut"" />
        </root>");

        // Act
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        // Assert
        result.Should().Contain("RECEIPT");
        result.Should().Contain("Date: 2024-01-01");
        result.Should().Contain("Burger");
        result.Should().Contain("Total: $20.00");
        result.Should().Contain(POS80Commands.cut);
    }

    [Fact]
    public void ProcessXmlDocument_Should_IgnoreUnknownElements()
    {
        // Arrange
        var builder = CreateBuilder();
        var xml = new XmlDocument();
        xml.LoadXml(@"<root>
            <unknown>This should be ignored</unknown>
            <text>This should be processed</text>
            <anothertag />
        </root>");

        // Act & Assert - Should not throw
        builder.ProcessXmlDocument(xml);
        var result = builder.Build();

        result.Should().Contain("This should be processed");
        result.Should().NotContain("This should be ignored");
    }
}