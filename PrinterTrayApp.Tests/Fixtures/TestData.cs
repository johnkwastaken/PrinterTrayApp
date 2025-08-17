using PrinterTrayApp.Models;
using PrinterTrayApp.Services;
using System.Text.Json;

namespace PrinterTrayApp.Tests.Fixtures;

public static class TestData
{
    public static class Templates
    {
        public const string SimpleReceipt = @"<root>
            <text align=""center"" size=""wide-high"">RECEIPT</text>
            <separator />
            <text>Date: {{dateOfPrinting}}</text>
            <text>Order: {{orderId}}</text>
            <blank lines=""2""/>
            <command cmd=""cut""/>
        </root>";

        public const string KitchenDocket = @"<root>
            <text align=""center"" size=""wide-high"">Kitchen Order</text>
            <separator />
            <docket-section>
                <text scale=""2"">{{categoryName}}</text>
                <text iterate=""printerTaskProduct"">{{quantity}}x {{productName}}</text>
                <text iterate=""printerTaskProduct.modifiers"">  + {{modifierName}}</text>
            </docket-section>
            <blank lines=""3""/>
            <command cmd=""cut""/>
        </root>";

        public const string CustomerReceipt = @"<root>
            <receipt-section>
                <text align=""center"" size=""wide-high"">{{storeName}}</text>
                <text align=""center"">{{storeAddress}}</text>
                <separator />
                <text>Date: {{dateOfPrinting}}</text>
                <text>Order #{{orderId}}</text>
                <separator />
                <table iterate=""items"">
                    <column width=""50"">{{name}}</column>
                    <column width=""15"" align=""center"">{{qty}}</column>
                    <column width=""15"" align=""right"">{{price}}</column>
                    <column width=""20"" align=""right"">{{total}}</column>
                </table>
                <separator char=""=""/>
                <text align=""right"" font-weight=""bold"" size=""wide"">Total: {{grandTotal}}</text>
                <blank lines=""2""/>
                <text align=""center"">Thank you for your order!</text>
                <blank lines=""3""/>
                <command cmd=""cut""/>
            </receipt-section>
        </root>";

        public const string BarReceipt = @"<root>
            <text align=""center"" size=""wide-high"">BAR ORDER</text>
            <separator />
            <text>Table: {{tableNumber}}</text>
            <text>Server: {{serverName}}</text>
            <separator />
            <text iterate=""drinks"" size=""wide"">{{quantity}}x {{drinkName}}</text>
            <text iterate=""drinks.modifiers"">  {{modifierName}}</text>
            <separator />
            <text>Time: {{orderTime}}</text>
            <blank lines=""3""/>
            <command cmd=""cut""/>
        </root>";
    }

    public static class JsonData
    {
        public static string SimpleOrder => JsonSerializer.Serialize(new
        {
            orderId = "12345",
            dateOfPrinting = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        public static string KitchenOrder => JsonSerializer.Serialize(new
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
        });

        public static string CustomerOrder => JsonSerializer.Serialize(new
        {
            storeName = "Test Restaurant",
            storeAddress = "123 Main St, Test City",
            orderId = "ORD-2024-001",
            items = new[]
            {
                new { name = "Cheeseburger", qty = "2", price = "$12.99", total = "$25.98" },
                new { name = "French Fries", qty = "2", price = "$4.99", total = "$9.98" },
                new { name = "Coke", qty = "2", price = "$2.99", total = "$5.98" }
            },
            grandTotal = "$41.94"
        });

        public static string BarOrder => JsonSerializer.Serialize(new
        {
            tableNumber = "12",
            serverName = "John",
            orderTime = DateTime.Now.ToString("HH:mm"),
            drinks = new object[]
            {
                new
                {
                    quantity = 2,
                    drinkName = "Margarita",
                    modifiers = new[]
                    {
                        new { modifierName = "Extra Salt" },
                        new { modifierName = "Top Shelf Tequila" }
                    }
                },
                new
                {
                    quantity = 1,
                    drinkName = "Beer",
                    modifiers = new object[] { }
                }
            }
        });

        public static string RealPosTask => @"{
            ""_id"": {""id"": ""670cf948cf079ea3872c1e37"", ""siteId"": ""65e5e19dfa0e95ca09e4d953""},
            ""evicted"": true,
            ""active"": true,
            ""name"": ""Dell Infinity POS station 3"",
            ""deviceId"": ""9e4e3b4e-46e2-46fe-948f-e088797e8a80"",
            ""sourceDevice"": {
                ""name"": ""Dell Infinity POS station 3"",
                ""deviceId"": ""9e4e3b4e-46e2-46fe-948f-e088797e8a80""
            },
            ""template"": {
                ""body"": ""<root><docket-section><text scale=\""2\"">{{categoryName}}</text></docket-section></root>"",
                ""name"": ""Kitchen Docket"",
                ""templateType"": ""Docket"",
                ""templateData"": ""{}""
            },
            ""currentPrinterId"": null,
            ""printerName"": ""Microsoft Print to PDF"",
            ""printerDeviceName"": """",
            ""secondPrinterIds"": [],
            ""isOpenCashDrawer"": false,
            ""isComplete"": false,
            ""inProgress"": false,
            ""retryCount"": 0,
            ""isSuspend"": false,
            ""ipAddress"": """",
            ""appVersion"": """",
            ""templateVersion"": """",
            ""error"": """",
            ""jobId"": null,
            ""name"": ""Task-670cf948cf079ea3872c1e37"",
            ""registerName"": ""Main Register"",
            ""printerLocation"": null,
            ""createdTime"": {""$date"": 1751700552312},
            ""updatedTime"": {""$date"": 1751700552312},
            ""updatedBySystem"": ""KDS3-b10e826a"",
            ""mainGroupProduct"": [{
                ""categoryName"": ""Burgers"",
                ""printerTaskProduct"": [
                    {""productName"": ""Cheeseburger""},
                    {""productName"": ""Hamburger""}
                ]
            }]
        }";
    }

    public static class PrinterTasks
    {
        public static PrinterTask SimpleTask => new()
        {
            _id = new ObjectId { id = "test-001", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = Templates.SimpleReceipt,
                name = "Simple Receipt",
                templateType = "Receipt"
            },
            templateData = JsonData.SimpleOrder,
            printerDeviceName = "TestPrinter",
            isOpenCashDrawer = false
        };

        public static PrinterTask KitchenTask => new()
        {
            _id = new ObjectId { id = "test-002", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = Templates.KitchenDocket,
                name = "Kitchen Docket",
                templateType = "Docket"
            },
            templateData = JsonData.KitchenOrder,
            printerDeviceName = "KitchenPrinter",
            isOpenCashDrawer = false
        };

        public static PrinterTask CustomerReceiptTask => new()
        {
            _id = new ObjectId { id = "test-003", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = Templates.CustomerReceipt,
                name = "Customer Receipt",
                templateType = "Receipt"
            },
            templateData = JsonData.CustomerOrder,
            printerDeviceName = "ReceiptPrinter",
            isOpenCashDrawer = true
        };

        public static PrinterTask BarTask => new()
        {
            _id = new ObjectId { id = "test-004", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = Templates.BarReceipt,
                name = "Bar Order",
                templateType = "Docket"
            },
            templateData = JsonData.BarOrder,
            printerDeviceName = "BarPrinter",
            isOpenCashDrawer = false
        };

        public static PrinterTask InvalidTask => new()
        {
            _id = new ObjectId { id = "test-invalid", siteId = "site-001" },
            template = null, // Invalid - no template
            templateData = "{}",
            printerDeviceName = "TestPrinter",
            isOpenCashDrawer = false
        };

        public static PrinterTask EmptyPrinterTask => new()
        {
            _id = new ObjectId { id = "test-empty", siteId = "site-001" },
            template = new ReceiptTemplate
            {
                body = "<root><text>Test</text></root>",
                name = "Test"
            },
            templateData = "{}",
            printerDeviceName = "", // Empty printer
            printerName = "" // Also empty
        };
    }

    public static class ExpectedCommands
    {
        public static string[] SimpleReceiptCommands => new[]
        {
            POS80Commands.align[PrinterAlign.Center],
            POS80Commands.size[PrinterScale.WideHigh],
            "RECEIPT",
            POS80Commands.size[PrinterScale.Normal],
            "----------------------------------------",
            "Date:",
            "Order: 12345",
            "\n\n",
            POS80Commands.cut
        };

        public static string[] KitchenDocketCommands => new[]
        {
            "Kitchen Order",
            "Main Course",
            "2x Grilled Salmon",
            "Extra Lemon",
            "No Salt",
            "1x Caesar Salad",
            "Extra Dressing",
            "Beverages",
            "3x Coke",
            POS80Commands.cut
        };

        public static string[] CashDrawerCommands => new[]
        {
            POS80Commands.openCashDrawer[PrinterPulse.Duration_100]
        };
    }

    public static class ErrorMessages
    {
        public const string EmptyBody = "Request body is empty";
        public const string InvalidFormat = "Invalid print task format";
        public const string NoPrinter = "No printer specified";
        public const string PrinterNotFound = "Printer '{}' not found";
        public const string TemplateEmpty = "Template is missing or empty";
    }
}