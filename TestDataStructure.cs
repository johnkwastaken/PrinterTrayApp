using System;
using Newtonsoft.Json.Linq;

public class TestDataStructure
{
    public static void Main()
    {
        // Load the templateData from printertask.json
        var json = System.IO.File.ReadAllText(@"C:\Users\johnk\repo\printing\PrinterTrayApp\printertask.json");
        var task = JObject.Parse(json);
        var templateData = task["templateData"].ToString();
        
        // Parse the templateData
        var data = JObject.Parse(templateData);
        
        // Navigate to mainProducts
        var mainProducts = data["orders"]["mainProducts"] as JArray;
        Console.WriteLine($"mainProducts count: {mainProducts?.Count ?? 0}");
        
        if (mainProducts != null && mainProducts.Count > 0)
        {
            var firstProduct = mainProducts[0];
            Console.WriteLine($"\nFirst product structure:");
            Console.WriteLine($"Has 'categories': {firstProduct["categories"] != null}");
            Console.WriteLine($"Has 'productName': {firstProduct["productName"] != null}");
            Console.WriteLine($"Has 'printName': {firstProduct["printName"] != null}");
            
            var categories = firstProduct["categories"] as JArray;
            if (categories != null && categories.Count > 0)
            {
                Console.WriteLine($"\nCategories count: {categories.Count}");
                var firstCat = categories[0];
                
                Console.WriteLine($"First category structure:");
                Console.WriteLine($"Has 'category': {firstCat["category"] != null}");
                Console.WriteLine($"Has 'products': {firstCat["products"] != null}");
                
                if (firstCat["category"] != null)
                {
                    var category = firstCat["category"];
                    Console.WriteLine($"\nCategory object:");
                    Console.WriteLine($"Name: {category["name"]}");
                    Console.WriteLine($"Has 'products': {category["products"] != null}");
                    
                    var products = category["products"] as JArray;
                    if (products != null && products.Count > 0)
                    {
                        Console.WriteLine($"\nProducts in category: {products.Count}");
                        var product = products[0];
                        Console.WriteLine($"Product name: {product["productName"]}");
                        Console.WriteLine($"Print name: {product["printName"]}");
                        Console.WriteLine($"Qty: {product["qty"]}");
                    }
                }
            }
        }
    }
}