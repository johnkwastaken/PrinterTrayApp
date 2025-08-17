using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using System.Xml;
using PrinterTrayApp.Models;

namespace PrinterTrayApp.Tests.Unit.Services;

public class TemplateHelpersTests
{
    [Fact]
    public void RenderTemplate_Should_ReplaceSimpleTokens()
    {
        // Arrange
        var template = @"<root><text>Order ID: {{orderId}}</text></root>";
        var data = @"{""orderId"": ""12345""}";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        result.Should().NotBeNull();
        var textNode = result.SelectSingleNode("//text");
        textNode?.InnerText.Should().Be("Order ID: 12345");
    }

    [Fact]
    public void RenderTemplate_Should_ReplaceNestedTokens()
    {
        // Arrange
        var template = @"<root><text>Customer: {{customer.name}}</text></root>";
        var data = @"{""customer"": {""name"": ""John Doe""}}";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var textNode = result.SelectSingleNode("//text");
        textNode?.InnerText.Should().Be("Customer: John Doe");
    }

    [Fact]
    public void RenderTemplate_Should_RemoveElementsWithEmptyTokens()
    {
        // Arrange
        var template = @"<root>
            <text>Name: {{name}}</text>
            <text>Phone: {{phone}}</text>
        </root>";
        var data = @"{""name"": ""John""}"; // phone is missing

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var textNodes = result.SelectNodes("//text");
        textNodes?.Count.Should().Be(1);
        textNodes?[0]?.InnerText.Should().Be("Name: John");
    }

    [Fact]
    public void RenderTemplate_Should_HandleArrayIterations()
    {
        // Arrange
        var template = @"<root>
            <text iterate=""items"">{{name}}: {{price}}</text>
        </root>";
        var data = @"{
            ""items"": [
                {""name"": ""Item1"", ""price"": ""$10""},
                {""name"": ""Item2"", ""price"": ""$20""}
            ]
        }";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var textNodes = result.SelectNodes("//text");
        textNodes?.Count.Should().Be(2);
        textNodes?[0]?.InnerText.Should().Be("Item1: $10");
        textNodes?[1]?.InnerText.Should().Be("Item2: $20");
    }

    [Fact]
    public void RenderTemplate_Should_ProcessDocketSection()
    {
        // Arrange
        var template = @"<root>
            <docket-section>
                <text scale=""2"">{{categoryName}}</text>
                <text iterate=""products"">{{productName}}</text>
            </docket-section>
        </root>";
        var data = @"{
            ""mainGroupProduct"": [{
                ""categoryName"": ""Burgers"",
                ""products"": [
                    {""productName"": ""Cheeseburger""},
                    {""productName"": ""Hamburger""}
                ]
            }]
        }";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var categoryNode = result.SelectSingleNode("//text[@font-family='b']");
        categoryNode?.InnerText.Should().Be("Burgers");
        
        var productNodes = result.SelectNodes("//text[not(@font-family)]");
        productNodes?.Count.Should().Be(2);
    }

    [Fact]
    public void RenderTemplate_Should_MapScaleToFontAttributes()
    {
        // Arrange
        var template = @"<root>
            <text scale=""1"">Scale 1</text>
            <text scale=""2"">Scale 2</text>
            <text scale=""3"">Scale 3</text>
            <text scale=""4"">Scale 4</text>
            <text scale=""5"">Scale 5</text>
            <text scale=""6"">Scale 6</text>
        </root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var scale1 = result.SelectSingleNode("//text[text()='Scale 1']");
        scale1?.Attributes?["font-family"]?.Value.Should().Be("c");
        scale1?.Attributes?["size"]?.Value.Should().Be("normal");

        var scale4 = result.SelectSingleNode("//text[text()='Scale 4']");
        scale4?.Attributes?["font-family"]?.Value.Should().Be("c");
        scale4?.Attributes?["size"]?.Value.Should().Be("wide-high");

        var scale6 = result.SelectSingleNode("//text[text()='Scale 6']");
        scale6?.Attributes?["font-family"]?.Value.Should().Be("a");
        scale6?.Attributes?["size"]?.Value.Should().Be("wide-high");
    }

    [Fact]
    public void RenderTemplate_Should_HandleSpecialTokenDateOfPrinting()
    {
        // Arrange
        var template = @"<root><text>Date: {{dateOfPrinting}}</text></root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var textNode = result.SelectSingleNode("//text");
        textNode?.InnerText.Should().StartWith("Date: ");
        textNode?.InnerText.Should().Contain(DateTime.Now.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void RenderTemplate_Should_HandleReceiptSection()
    {
        // Arrange
        var template = @"<root>
            <receipt-section>
                <text>Total: {{total}}</text>
            </receipt-section>
        </root>";
        var data = @"{""total"": ""$25.00""}";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        // receipt-section should be processed and removed
        result.SelectSingleNode("//receipt-section").Should().BeNull();
        var textNode = result.SelectSingleNode("//text");
        textNode?.InnerText.Should().Be("Total: $25.00");
    }

    [Fact]
    public void RenderTemplate_Should_HandleTableStructure()
    {
        // Arrange
        var template = @"<root>
            <table>
                <column width=""60"">{{item}}</column>
                <column width=""20"" align=""right"">{{qty}}</column>
                <column width=""20"" align=""right"">{{price}}</column>
            </table>
        </root>";
        var data = @"{""item"": ""Burger"", ""qty"": ""2"", ""price"": ""$20""}";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var textNode = result.SelectSingleNode("//text");
        textNode?.InnerText.Should().Contain("Burger");
        textNode?.InnerText.Should().Contain("2");
        textNode?.InnerText.Should().Contain("$20");
    }

    [Fact]
    public void RenderTemplate_Should_HandleMissingTemplateData()
    {
        // Arrange
        var template = @"<root><text>Test</text></root>";

        // Act & Assert - Should not throw
        var result = TemplateHelpers.RenderTemplate(template, null, PrinterPaperWidth.Paper_80);
        result.Should().NotBeNull();

        result = TemplateHelpers.RenderTemplate(template, "", PrinterPaperWidth.Paper_80);
        result.Should().NotBeNull();
    }

    [Fact]
    public void RenderTemplate_Should_HandleInvalidJson()
    {
        // Arrange
        var template = @"<root><text>Test: {{value}}</text></root>";
        var invalidJson = "not valid json";

        // Act & Assert - Should handle gracefully
        var result = TemplateHelpers.RenderTemplate(template, invalidJson, PrinterPaperWidth.Paper_80);
        result.Should().NotBeNull();
    }

    [Fact]
    public void RenderTemplate_Should_ProcessCommandElements()
    {
        // Arrange
        var template = @"<root>
            <command cmd=""cut"" />
            <command cmd=""opencashdrawer"" />
            <command cmd=""beep"" />
        </root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var commands = result.SelectNodes("//command");
        commands?.Count.Should().Be(3);
        commands?[0]?.Attributes?["cmd"]?.Value.Should().Be("cut");
        commands?[1]?.Attributes?["cmd"]?.Value.Should().Be("opencashdrawer");
        commands?[2]?.Attributes?["cmd"]?.Value.Should().Be("beep");
    }

    [Fact]
    public void RenderTemplate_Should_HandleBlankLines()
    {
        // Arrange
        var template = @"<root>
            <blank lines=""3"" />
            <blank />
        </root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var blanks = result.SelectNodes("//blank");
        blanks?.Count.Should().Be(2);
        blanks?[0]?.Attributes?["lines"]?.Value.Should().Be("3");
        blanks?[1]?.Attributes?["lines"].Should().BeNull();
    }

    [Fact]
    public void RenderTemplate_Should_HandleSeparator()
    {
        // Arrange
        var template = @"<root>
            <separator />
            <separator char=""=""/>
            <separator char=""*"" length=""20""/>
        </root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var separators = result.SelectNodes("//separator");
        separators?.Count.Should().Be(3);
        separators?[1]?.Attributes?["char"]?.Value.Should().Be("=");
        separators?[2]?.Attributes?["char"]?.Value.Should().Be("*");
        separators?[2]?.Attributes?["length"]?.Value.Should().Be("20");
    }

    [Fact]
    public void RenderTemplate_Should_HandleDocketSectionWithMainGroupProduct()
    {
        // Arrange
        var template = @"<root>
            <docket-section>
                <text scale=""2"">{{categoryName}}</text>
                <text iterate=""printerTaskProduct"">{{productName}}</text>
            </docket-section>
        </root>";
        var data = @"{
            ""mainGroupProduct"": [{
                ""categoryName"": ""Drinks"",
                ""printerTaskProduct"": [
                    {""productName"": ""Coke""},
                    {""productName"": ""Sprite""}
                ]
            }]
        }";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var categoryNode = result.SelectSingleNode("//text[@font-family]");
        categoryNode?.InnerText.Should().Be("Drinks");
        
        var productNodes = result.SelectNodes("//text[not(@font-family)]");
        productNodes?.Count.Should().Be(2);
        productNodes?[0]?.InnerText.Should().Be("Coke");
        productNodes?[1]?.InnerText.Should().Be("Sprite");
    }

    [Fact]
    public void RenderTemplate_Should_RemoveEmptyTextElements()
    {
        // Arrange
        var template = @"<root>
            <text>{{missing}}</text>
            <text>Static Text</text>
            <text>{{alsoMissing}}</text>
        </root>";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);

        // Assert
        var textNodes = result.SelectNodes("//text");
        textNodes?.Count.Should().Be(1);
        textNodes?[0]?.InnerText.Should().Be("Static Text");
    }

    [Fact]
    public void RenderTemplate_Should_HandleComplexNestedData()
    {
        // Arrange
        var template = @"<root>
            <text>Store: {{store.name}}</text>
            <text>Address: {{store.address.street}}, {{store.address.city}}</text>
        </root>";
        var data = @"{
            ""store"": {
                ""name"": ""Test Store"",
                ""address"": {
                    ""street"": ""123 Main St"",
                    ""city"": ""Test City""
                }
            }
        }";

        // Act
        var result = TemplateHelpers.RenderTemplate(template, data, PrinterPaperWidth.Paper_80);

        // Assert
        var textNodes = result.SelectNodes("//text");
        textNodes?.Count.Should().Be(2);
        textNodes?[0]?.InnerText.Should().Be("Store: Test Store");
        textNodes?[1]?.InnerText.Should().Be("Address: 123 Main St, Test City");
    }
}