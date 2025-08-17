using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;
using System.Text.Json;

namespace PrinterTrayApp.Tests.Integration;

public class PrintWorkflowIntegrationTests
{
    private readonly PrinterService _printerService;

    public PrintWorkflowIntegrationTests()
    {
        _printerService = new PrinterService();
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Process_Simple_Template()
    {
        // Arrange
        var template = @"<root>
            <text align=""center"" size=""wide-high"">TEST RECEIPT</text>
            <separator />
            <text>Date: {{dateOfPrinting}}</text>
            <text>Order: {{orderId}}</text>
            <blank lines=""2""/>
            <command cmd=""cut""/>
        </root>";
        
        var templateData = @"{""orderId"": ""12345""}";

        // Act - Process template
        var xmlDoc = TemplateHelpers.RenderTemplate(template, templateData, PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().NotBeNullOrEmpty();
        commands.Should().Contain("TEST RECEIPT");
        commands.Should().Contain("12345");
        commands.Should().Contain(POS80Commands.cut);
        commands.Should().Contain(POS80Commands.align[PrinterAlign.Center]);
        commands.Should().Contain(POS80Commands.size[PrinterScale.WideHigh]);
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Process_Docket_Section()
    {
        // Arrange
        var template = @"<root>
            <docket-section>
                <text scale=""2"">{{categoryName}}</text>
                <text iterate=""products"">{{productName}}</text>
                <text iterate=""products.modifiers"">  + {{modifierName}}</text>
            </docket-section>
        </root>";
        
        var templateData = @"{
            ""mainGroupProduct"": [{
                ""categoryName"": ""Burgers"",
                ""products"": [
                    {
                        ""productName"": ""Cheeseburger"",
                        ""modifiers"": [
                            {""modifierName"": ""Extra Cheese""},
                            {""modifierName"": ""No Pickles""}
                        ]
                    }
                ]
            }]
        }";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, templateData, PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Burgers");
        commands.Should().Contain("Cheeseburger");
        commands.Should().Contain("Extra Cheese");
        commands.Should().Contain("No Pickles");
        commands.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.B]); // Scale 2 maps to font-family b
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Handle_Receipt_Section()
    {
        // Arrange
        var template = @"<root>
            <receipt-section>
                <text align=""center"" size=""wide"">{{storeName}}</text>
                <separator />
                <table iterate=""items"">
                    <column width=""60"">{{name}}</column>
                    <column width=""20"" align=""right"">{{qty}}</column>
                    <column width=""20"" align=""right"">{{price}}</column>
                </table>
                <separator char=""=""/>
                <text align=""right"" font-weight=""bold"">Total: {{total}}</text>
            </receipt-section>
        </root>";
        
        var templateData = @"{
            ""storeName"": ""Test Restaurant"",
            ""items"": [
                {""name"": ""Burger"", ""qty"": ""2"", ""price"": ""$20.00""},
                {""name"": ""Fries"", ""qty"": ""1"", ""price"": ""$5.00""}
            ],
            ""total"": ""$25.00""
        }";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, templateData, PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Test Restaurant");
        commands.Should().Contain("Burger");
        commands.Should().Contain("$20.00");
        commands.Should().Contain("Total: $25.00");
        commands.Should().Contain(POS80Commands.bold[true]);
        commands.Should().Contain("========================================");
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Remove_Empty_Token_Elements()
    {
        // Arrange
        var template = @"<root>
            <text>Name: {{customer.name}}</text>
            <text>Phone: {{customer.phone}}</text>
            <text>Email: {{customer.email}}</text>
        </root>";
        
        var templateData = @"{
            ""customer"": {
                ""name"": ""John Doe"",
                ""email"": ""john@example.com""
            }
        }"; // Phone is missing

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, templateData, PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Name: John Doe");
        commands.Should().Contain("Email: john@example.com");
        commands.Should().NotContain("Phone:"); // Should be removed entirely
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Handle_Cash_Drawer()
    {
        // Arrange
        var template = @"<root>
            <text>Sale Complete</text>
            <command cmd=""opencashdrawer""/>
        </root>";
        
        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Sale Complete");
        commands.Should().Contain(POS80Commands.openCashDrawer[PrinterPulse.Duration_100]);
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Handle_Complex_POS_Task()
    {
        // Arrange - Real-world POS task structure
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = @"<root>
                    <text align=""center"" size=""wide-high"">Kitchen Order</text>
                    <separator />
                    <docket-section>
                        <text scale=""2"">{{categoryName}}</text>
                        <text iterate=""printerTaskProduct"">{{quantity}}x {{productName}}</text>
                        <text iterate=""printerTaskProduct.modifiers"">  + {{modifierName}}</text>
                    </docket-section>
                    <blank lines=""3""/>
                    <command cmd=""cut""/>
                </root>",
                name = "Kitchen Docket",
                templateType = "Docket" // Docket type
            },
            templateData = JsonSerializer.Serialize(new
            {
                mainGroupProduct = new object[]
                {
                    new
                    {
                        categoryName = "Main Course",
                        printerTaskProduct = new[]
                        {
                            new
                            {
                                quantity = 2,
                                productName = "Grilled Salmon",
                                modifiers = new[]
                                {
                                    new { modifierName = "Extra Lemon" },
                                    new { modifierName = "No Salt" }
                                }
                            },
                            new
                            {
                                quantity = 1,
                                productName = "Caesar Salad",
                                modifiers = new[]
                                {
                                    new { modifierName = "Extra Dressing" }
                                }
                            }
                        }
                    },
                    new
                    {
                        categoryName = "Beverages",
                        printerTaskProduct = new[]
                        {
                            new
                            {
                                quantity = 3,
                                productName = "Coke",
                                modifiers = new object[] { }
                            }
                        }
                    }
                }
            }),
            printerDeviceName = "TestPrinter",
            isOpenCashDrawer = false
        };

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(
            printerTask.template.body, 
            printerTask.templateData, 
            PrinterPaperWidth.Paper_80
        );
        
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        if (printerTask.isOpenCashDrawer)
        {
            builder.OpenCashDrawer();
        }
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Kitchen Order");
        commands.Should().Contain("Main Course");
        commands.Should().Contain("2x Grilled Salmon");
        commands.Should().Contain("Extra Lemon");
        commands.Should().Contain("No Salt");
        commands.Should().Contain("1x Caesar Salad");
        commands.Should().Contain("Beverages");
        commands.Should().Contain("3x Coke");
        commands.Should().Contain(POS80Commands.cut);
        commands.Should().NotContain(POS80Commands.openCashDrawer[PrinterPulse.Duration_100]); // Cash drawer is false
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Handle_Scale_To_Font_Mapping()
    {
        // Arrange
        var template = @"<root>
            <text scale=""1"">Scale 1 Text</text>
            <text scale=""2"">Scale 2 Text</text>
            <text scale=""3"">Scale 3 Text</text>
            <text scale=""4"">Scale 4 Text</text>
            <text scale=""5"">Scale 5 Text</text>
            <text scale=""6"">Scale 6 Text</text>
        </root>";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        // Scale 1-3 use normal size, 4-6 use wide-high
        commands.Should().Contain(POS80Commands.size[PrinterScale.Normal]);
        commands.Should().Contain(POS80Commands.size[PrinterScale.WideHigh]);
        
        // Font families are mapped correctly
        commands.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.A]);
        commands.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.B]);
        commands.Should().Contain(POS80Commands.fontFamily[PrinterFontFamily.C]);
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Not_Include_Font_Color_Unless_Specified()
    {
        // Arrange
        var template = @"<root>
            <text>Normal text without color</text>
            <text font-color=""red"">Red text</text>
        </root>";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Normal text without color");
        commands.Should().Contain("Red text");
        
        // Should only have one color command (for red text)
        var colorCommandCount = commands.Split(POS80Commands.fontColor[PrinterFontColor.Red]).Length - 1;
        colorCommandCount.Should().Be(1);
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Handle_Barcode_And_QRCode()
    {
        // Arrange
        var template = @"<root>
            <text>Product Code:</text>
            <barcode type=""code128"">ABC123456</barcode>
            <blank lines=""2""/>
            <text>Scan for menu:</text>
            <qrcode>https://restaurant.com/menu</qrcode>
        </root>";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, "{}", PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Product Code:");
        commands.Should().Contain("ABC123456");
        commands.Should().Contain("Scan for menu:");
        commands.Should().Contain("https://restaurant.com/menu");
    }

    [Fact]
    public void Complete_Print_Workflow_Should_Process_Real_POS_Json()
    {
        // Arrange - Using actual JSON structure from POS
        var template = @"<root>
            <docket-section>
                <text scale=""2"">{{categoryName}}</text>
                <text iterate=""printerTaskProduct"">{{productName}}</text>
            </docket-section>
        </root>";
        
        var templateData = @"{
            ""_id"": {""id"": ""670cf948cf079ea3872c1e37"", ""siteId"": ""65e5e19dfa0e95ca09e4d953""},
            ""evicted"": true,
            ""active"": true,
            ""name"": ""Dell Infinity POS station 3"",
            ""deviceId"": ""9e4e3b4e-46e2-46fe-948f-e088797e8a80"",
            ""mainGroupProduct"": [{
                ""categoryName"": ""Burgers"",
                ""printerTaskProduct"": [
                    {""productName"": ""Cheeseburger""},
                    {""productName"": ""Hamburger""}
                ]
            }]
        }";

        // Act
        var xmlDoc = TemplateHelpers.RenderTemplate(template, templateData, PrinterPaperWidth.Paper_80);
        var builder = new CommandBuilder(PrinterPaperWidth.Paper_80);
        builder.ProcessXmlDocument(xmlDoc);
        var commands = builder.Build();

        // Assert
        commands.Should().Contain("Burgers");
        commands.Should().NotContain("!0Burger"); // Should not have ESC/POS commands in text
        commands.Should().Contain("Cheeseburger");
        commands.Should().Contain("Hamburger");
    }
}