using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using PrinterTrayApp.Models;
using Newtonsoft.Json;
using System.Xml;

namespace PrinterTrayApp.Tests.Integration;

public class ProcessorTemplateIntegrationTests
{
    [Fact]
    public void ProcessorAndTemplateHelpers_Should_WorkTogether_WithRealPOSData()
    {
        // Arrange - Real POS data that was causing the error
        var templateData = @"{
            ""sites"": {""businessName"": ""Test Restaurant""},
            ""orders"": {
                ""orderTime"": ""2024-10-14 10:22"",
                ""orderNumber"": ""12345"",
                ""customerName"": ""John Doe"",
                ""total"": ""25.50"",
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""order"": 1,
                            ""products"": [
                                {""qty"": 2, ""name"": ""Burger"", ""price"": ""10.00""},
                                {""qty"": 1, ""name"": ""Fries"", ""price"": ""5.50""}
                            ]
                        }
                    }]
                }]
            }
        }";

        var templateBody = @"<root>
            <text>{{sites.businessName}}</text>
            <text>Order: {{orders.orderNumber}}</text>
            <text>Total: {{orders.total}}</text>
        </root>";

        // Act
        var processedData = PrinterTaskProcessor.ProcessTemplateData(templateData, enableCompression: true);
        
        // This should not throw "Can not add JObject to JObject" error
        var xmlDoc = TemplateHelpers.RenderTemplate(templateBody, processedData, PrinterPaperWidth.Paper_80);

        // Assert
        processedData.Should().NotBeNullOrEmpty();
        xmlDoc.Should().NotBeNull();
        xmlDoc.DocumentElement.Should().NotBeNull();
        
        // Verify the template was rendered with tokens replaced
        var xmlContent = xmlDoc.OuterXml;
        xmlContent.Should().Contain("Test Restaurant");
        xmlContent.Should().Contain("12345");
        xmlContent.Should().Contain("25.50");
        xmlContent.Should().NotContain("{{"); // No unprocessed tokens
    }

    [Fact]
    public void ProcessorAndTemplateHelpers_Should_HandleNestedProductStructures()
    {
        // Arrange - Complex nested structure that caused circular reference issues
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Mains"",
                            ""order"": 1,
                            ""products"": [
                                {
                                    ""productId"": ""p1"",
                                    ""name"": ""Burger"",
                                    ""qty"": 2,
                                    ""category"": {
                                        ""name"": ""Mains"",
                                        ""order"": 1
                                    }
                                }
                            ]
                        }
                    }],
                    ""courses"": [{
                        ""course"": {
                            ""name"": ""First Course"",
                            ""order"": 1,
                            ""products"": []
                        }
                    }]
                }]
            }
        }";

        var templateBody = @"<root><text>Test</text></root>";

        // Act & Assert - Should not throw circular reference error
        var processedData = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var xmlDoc = TemplateHelpers.RenderTemplate(templateBody, processedData);
        
        xmlDoc.Should().NotBeNull();
    }

    [Fact]
    public void ProcessorAndTemplateHelpers_Should_HandleEmptyTemplateData()
    {
        // Arrange
        var templateData = "{}";
        var templateBody = @"<root><text>Simple Test</text></root>";

        // Act
        var processedData = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var xmlDoc = TemplateHelpers.RenderTemplate(templateBody, processedData);

        // Assert
        xmlDoc.Should().NotBeNull();
        xmlDoc.OuterXml.Should().Contain("Simple Test");
    }

    [Fact]
    public void FullPrintFlow_Should_WorkWithDictionaryProcessor()
    {
        // Arrange - Complete PrinterTask as it would come from POS
        var printerTask = new PrinterTask
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = @"<root>
                    <text align=""center"">{{sites.businessName}}</text>
                    <separator char=""-"" />
                    <text>Order #{{orders.orderNumber}}</text>
                    <text>Total: ${{orders.total}}</text>
                    <blank lines=""2"" />
                    <command cmd=""cut"" />
                </root>",
                name = "Test Receipt",
                templateType = "Receipt"
            },
            templateData = @"{
                ""sites"": {""businessName"": ""Test Restaurant""},
                ""orders"": {
                    ""orderNumber"": ""12345"",
                    ""total"": ""25.50"",
                    ""mainProducts"": [{
                        ""categories"": [{
                            ""category"": {""name"": ""Food"", ""order"": 1, ""products"": []}
                        }]
                    }]
                }
            }",
            printerDeviceName = "TestPrinter",
            isOpenCashDrawer = false
        };

        // Act - Simulate the full print flow
        string processedTemplateData;
        
        // Clean the data (as HttpServer does)
        var cleanedData = JsonCleaner.CleanTemplateData(printerTask.templateData);
        
        // Process with POS rules (as HttpServer does)
        processedTemplateData = PrinterTaskProcessor.ProcessTemplateData(cleanedData, enableCompression: true);
        
        // Render template (as HttpServer does)
        var xmlDoc = TemplateHelpers.RenderTemplate(
            printerTask.template.body,
            processedTemplateData,
            PrinterPaperWidth.Paper_80
        );

        // Assert
        xmlDoc.Should().NotBeNull();
        var xml = xmlDoc.OuterXml;
        xml.Should().Contain("Test Restaurant");
        xml.Should().Contain("12345");
        xml.Should().Contain("25.50");
        
        // Verify command nodes are preserved
        xml.Should().Contain("cmd=\"cut\"");
    }

    [Fact]
    public void ProcessorAndTemplateHelpers_Should_HandleMongoDateFormat()
    {
        // Arrange - Data with MongoDB date format
        var templateData = @"{
            ""createdTime"": {""$date"": 1728883016312},
            ""orders"": {
                ""orderTime"": ""2024-10-14 10:22""
            }
        }";

        var templateBody = @"<root><text>{{orders.orderTime}}</text></root>";

        // Act
        var processedData = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var xmlDoc = TemplateHelpers.RenderTemplate(templateBody, processedData);

        // Assert
        xmlDoc.Should().NotBeNull();
        xmlDoc.OuterXml.Should().Contain("2024-10-14 10:22");
    }
}