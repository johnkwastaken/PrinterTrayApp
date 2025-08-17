using Xunit;
using FluentAssertions;
using PrinterTrayApp.Services;
using Newtonsoft.Json.Linq;

namespace PrinterTrayApp.Tests.Unit.Services;

public class PrinterTaskProcessorTests
{
    [Fact]
    public void ProcessTemplateData_Should_HandleEmptyData()
    {
        // Arrange
        var emptyData = "";
        
        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(emptyData);
        
        // Assert
        result.Should().Be(emptyData);
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleNullData()
    {
        // Arrange
        string? nullData = null;
        
        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(nullData);
        
        // Assert
        result.Should().Be(nullData);
    }

    [Fact]
    public void ProcessTemplateData_Should_SortCategoriesByOrder()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [
                        {""category"": {""name"": ""Drinks"", ""order"": 2, ""products"": []}},
                        {""category"": {""name"": ""Food"", ""order"": 1, ""products"": []}},
                        {""category"": {""name"": ""Desserts"", ""order"": 3, ""products"": []}}
                    ]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var categories = parsed["orders"]?["mainProducts"]?[0]?["categories"] as JArray;
        categories.Should().NotBeNull();
        categories![0]!["category"]!["name"]!.ToString().Should().Be("Food");  // order: 1
        categories[1]!["category"]!["name"]!.ToString().Should().Be("Drinks"); // order: 2  
        categories[2]!["category"]!["name"]!.ToString().Should().Be("Desserts"); // order: 3
    }

    [Fact]
    public void ProcessTemplateData_Should_SortCoursesByOrder()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""courses"": [
                        {""course"": {""name"": ""Dessert"", ""order"": 3, ""products"": []}},
                        {""course"": {""name"": ""Main"", ""order"": 2, ""products"": []}},
                        {""course"": {""name"": ""Starter"", ""order"": 1, ""products"": []}}
                    ]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var courses = parsed["orders"]?["mainProducts"]?[0]?["courses"] as JArray;
        courses.Should().NotBeNull();
        courses![0]!["course"]!["name"]!.ToString().Should().Be("Starter"); // order: 1
        courses[1]!["course"]!["name"]!.ToString().Should().Be("Main");    // order: 2
        courses[2]!["course"]!["name"]!.ToString().Should().Be("Dessert"); // order: 3
    }

    [Fact]
    public void ProcessTemplateData_Should_SortProductsByName()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""order"": 1,
                            ""products"": [
                                {""printName"": ""Zebra Burger"", ""qty"": ""1"", ""level"": 0},
                                {""printName"": ""Apple Pie"", ""qty"": ""1"", ""level"": 0},
                                {""printName"": ""Burger Deluxe"", ""qty"": ""1"", ""level"": 0}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var products = parsed["orders"]?["mainProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        products.Should().NotBeNull();
        products![0]!["printName"]!.ToString().Should().Be("Apple Pie");
        products[1]!["printName"]!.ToString().Should().Be("Burger Deluxe");
        products[2]!["printName"]!.ToString().Should().Be("Zebra Burger");
    }

    [Fact]
    public void ProcessTemplateData_Should_ApplyHidingRules_RootLevel()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""products"": [
                                {""printName"": ""Visible Product"", ""level"": 0, ""hide"": false},
                                {""printName"": ""Hidden Product"", ""level"": 1, ""hide"": true, ""finalUnitPrice"": 0}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var products = parsed["orders"]?["mainProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        products.Should().NotBeNull();
        products!.Count.Should().Be(1); // Hidden product should be removed
        products[0]!["printName"]!.ToString().Should().Be("Visible Product");
    }

    [Fact]
    public void ProcessTemplateData_Should_HideModifierGroups()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""products"": [
                                {""printName"": ""Regular Product"", ""level"": 0, ""categorisation"": ""PRODUCT""},
                                {""printName"": ""Empty Modifier Group"", ""level"": 1, ""hide"": false, ""categorisation"": ""MODIFIER_GROUP"", ""children"": []}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var products = parsed["orders"]?["mainProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        products.Should().NotBeNull();
        products!.Count.Should().Be(1); // Empty modifier group should be hidden
        products[0]!["printName"]!.ToString().Should().Be("Regular Product");
    }

    [Fact]
    public void ProcessTemplateData_Should_CompressProductsWithSameId()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""products"": [
                                {""printName"": ""Burger"", ""productId"": ""burger-1"", ""qty"": ""2"", ""sum"": 20.00},
                                {""printName"": ""Burger"", ""productId"": ""burger-1"", ""qty"": ""1"", ""sum"": 10.00},
                                {""printName"": ""Pizza"", ""productId"": ""pizza-1"", ""qty"": ""1"", ""sum"": 15.00}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData, enableCompression: true);
        var parsed = JObject.Parse(result);

        // Assert
        var products = parsed["orders"]?["mainProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        products.Should().NotBeNull();
        products!.Count.Should().Be(2); // Two burger items should be compressed into one

        var burger = products.FirstOrDefault(p => p["productId"]?.ToString() == "burger-1");
        burger.Should().NotBeNull();
        burger!["qty"]!.ToString().Should().Be("3"); // 2 + 1
        burger["sum"]!.Value<decimal>().Should().Be(30.00m); // 20.00 + 10.00
    }

    [Fact]
    public void ProcessTemplateData_Should_NotCompressWhenDisabled()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Food"",
                            ""products"": [
                                {""printName"": ""Burger"", ""productId"": ""burger-1"", ""qty"": ""2""},
                                {""printName"": ""Burger"", ""productId"": ""burger-1"", ""qty"": ""1""}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData, enableCompression: false);
        var parsed = JObject.Parse(result);

        // Assert
        var products = parsed["orders"]?["mainProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        products.Should().NotBeNull();
        products!.Count.Should().Be(2); // Should keep separate items when compression disabled
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleComplexHierarchy()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Main Course"",
                            ""order"": 2,
                            ""products"": [
                                {""printName"": ""Steak"", ""level"": 0, ""qty"": ""1""},
                                {""printName"": ""  Medium Rare"", ""level"": 1, ""qty"": """"}
                            ]
                        }
                    }],
                    ""courses"": [{
                        ""course"": {
                            ""name"": ""Appetizers"",
                            ""order"": 1,
                            ""products"": [
                                {""printName"": ""Salad"", ""level"": 0, ""qty"": ""1""}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert - Should preserve both categories and courses
        var categories = parsed["orders"]?["mainProducts"]?[0]?["categories"] as JArray;
        var courses = parsed["orders"]?["mainProducts"]?[0]?["courses"] as JArray;
        
        categories.Should().NotBeNull();
        courses.Should().NotBeNull();
        
        // Products should be sorted by name within each grouping
        var categoryProducts = categories![0]!["category"]!["products"] as JArray;
        categoryProducts!.Count.Should().Be(2);
        
        var courseProducts = courses![0]!["course"]!["products"] as JArray;
        courseProducts!.Count.Should().Be(1);
        courseProducts[0]!["printName"]!.ToString().Should().Be("Salad");
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleOtherProducts()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""otherProducts"": [{
                    ""categories"": [{
                        ""category"": {
                            ""name"": ""Beverages"",
                            ""order"": 1,
                            ""products"": [
                                {""printName"": ""Water"", ""level"": 0, ""qty"": ""2""},
                                {""printName"": ""Coffee"", ""level"": 0, ""qty"": ""1""}
                            ]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var otherProducts = parsed["orders"]?["otherProducts"]?[0]?["categories"]?[0]?["category"]?["products"] as JArray;
        otherProducts.Should().NotBeNull();
        otherProducts!.Count.Should().Be(2);
        
        // Should be sorted by name
        otherProducts[0]!["printName"]!.ToString().Should().Be("Coffee");
        otherProducts[1]!["printName"]!.ToString().Should().Be("Water");
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleFlatProductLists()
    {
        // Arrange
        var templateData = @"{
            ""mainGroupProduct"": [
                {""printName"": ""Zebra"", ""category"": {""name"": ""Animals"", ""order"": 1}},
                {""printName"": ""Apple"", ""category"": {""name"": ""Fruits"", ""order"": 2}},
                {""printName"": ""Bear"", ""category"": {""name"": ""Animals"", ""order"": 1}}
            ]
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert - Should group by category and sort
        var mainGroupProduct = parsed["mainGroupProduct"]?[0]?["categories"] as JArray;
        mainGroupProduct.Should().NotBeNull();
        
        // Should have 2 categories (Animals order:1, Fruits order:2)
        mainGroupProduct!.Count.Should().Be(2);
        mainGroupProduct[0]!["category"]!["name"]!.ToString().Should().Be("Animals"); // order: 1 first
        mainGroupProduct[1]!["category"]!["name"]!.ToString().Should().Be("Fruits");  // order: 2 second
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleInvalidJson_Gracefully()
    {
        // Arrange
        var invalidJson = "not valid json";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(invalidJson);

        // Assert - Should return original data when processing fails
        result.Should().Be(invalidJson);
    }

    [Fact]
    public void ProcessTemplateData_Should_RemoveEmptyCategories()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""categories"": [
                        {""category"": {""name"": ""Empty Category"", ""products"": []}},
                        {""category"": {""name"": ""Valid Category"", ""products"": [
                            {""printName"": ""Product"", ""level"": 0}
                        ]}}
                    ]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var categories = parsed["orders"]?["mainProducts"]?[0]?["categories"] as JArray;
        categories.Should().NotBeNull();
        categories!.Count.Should().Be(1); // Empty category should be removed
        categories[0]!["category"]!["name"]!.ToString().Should().Be("Valid Category");
    }

    [Fact]
    public void ProcessTemplateData_Should_HandleCategoriesWithinCourses()
    {
        // Arrange
        var templateData = @"{
            ""orders"": {
                ""mainProducts"": [{
                    ""courses"": [{
                        ""course"": {
                            ""name"": ""Main Course"",
                            ""order"": 1,
                            ""products"": [],
                            ""categories"": [{
                                ""category"": {
                                    ""name"": ""Meat"",
                                    ""order"": 1,
                                    ""products"": [
                                        {""printName"": ""Steak"", ""level"": 0}
                                    ]
                                }
                            }]
                        }
                    }]
                }]
            }
        }";

        // Act
        var result = PrinterTaskProcessor.ProcessTemplateData(templateData);
        var parsed = JObject.Parse(result);

        // Assert
        var courseCategories = parsed["orders"]?["mainProducts"]?[0]?["courses"]?[0]?["course"]?["categories"] as JArray;
        courseCategories.Should().NotBeNull();
        courseCategories!.Count.Should().Be(1);
        
        var products = courseCategories[0]!["category"]!["products"] as JArray;
        products.Should().NotBeNull();
        products!.Count.Should().Be(1);
        products[0]!["printName"]!.ToString().Should().Be("Steak");
    }
}