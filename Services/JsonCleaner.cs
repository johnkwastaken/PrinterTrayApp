using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PrinterTrayApp.Services;

/// <summary>
/// Cleans JSON data to remove nested category/course objects that cause circular references
/// </summary>
public static class JsonCleaner
{
    /// <summary>
    /// Cleans MongoDB types from the entire PrinterTask JSON
    /// </summary>
    public static string CleanFullJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        try
        {
            // Parse the JSON
            var obj = JObject.Parse(json);
            
            // Clean MongoDB dates throughout the entire object
            CleanMongoDbTypes(obj);
            
            // Return cleaned JSON
            return obj.ToString(Formatting.None);
        }
        catch
        {
            // If we can't parse it, return original
            return json;
        }
    }
    
    /// <summary>
    /// Removes nested category and course objects from products to prevent JObject assignment errors
    /// Also converts MongoDB dates to ISO format
    /// </summary>
    public static string CleanTemplateData(string templateData)
    {
        if (string.IsNullOrEmpty(templateData))
            return templateData;

        try
        {
            DebugLogger.Log("[JsonCleaner] Starting CleanTemplateData");
            
            // Parse the JSON
            var obj = JObject.Parse(templateData);
            DebugLogger.Log("[JsonCleaner] Parsed JSON successfully");
            
            // Clean MongoDB dates FIRST (before other processing)
            CleanMongoDbTypes(obj);
            DebugLogger.Log("[JsonCleaner] Cleaned MongoDB types");
            
            // Clean products in mainProducts
            var mainProducts = obj.SelectToken("orders.mainProducts");
            if (mainProducts != null)
            {
                DebugLogger.Log($"[JsonCleaner] Found orders.mainProducts with {(mainProducts as JArray)?.Count ?? 0} items");
            }
            CleanProductsInPath(obj, "orders.mainProducts");
            
            // Clean products in otherProducts  
            CleanProductsInPath(obj, "orders.otherProducts");
            
            // Clean products in mainGroupProduct
            CleanProductsInPath(obj, "mainGroupProduct");
            
            // Clean products in printerTaskProduct
            CleanProductsInPath(obj, "printerTaskProduct");
            
            DebugLogger.Log("[JsonCleaner] All cleaning complete");
            
            // Return cleaned JSON
            return obj.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError($"[JsonCleaner] Failed to clean: {ex.Message}", ex);
            // If we can't parse it, try a more aggressive string-based approach
            return CleanTemplateDataWithRegex(templateData);
        }
    }
    
    private static void CleanMongoDbTypes(JToken token)
    {
        if (token == null) return;
        
        if (token is JObject obj)
        {
            // Check if this is a MongoDB date object
            if (obj.Count == 1 && obj.ContainsKey("$date"))
            {
                var dateValue = obj["$date"];
                if (dateValue != null)
                {
                    // Convert MongoDB date to ISO string
                    if (dateValue.Type == JTokenType.Float || dateValue.Type == JTokenType.Integer)
                    {
                        var unixTime = dateValue.Value<double>();
                        DateTime dateTime;
                        
                        // Check if it's in seconds (with decimal) or milliseconds
                        if (unixTime < 32503680000) // Year 3000 in seconds
                        {
                            // It's in seconds with decimal fractions
                            var seconds = (long)unixTime;
                            var milliseconds = (long)((unixTime - seconds) * 1000);
                            dateTime = DateTimeOffset.FromUnixTimeSeconds(seconds).AddMilliseconds(milliseconds).DateTime;
                        }
                        else
                        {
                            // It's in milliseconds
                            dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)unixTime).DateTime;
                        }
                        
                        // Replace the parent with a simple string value
                        if (obj.Parent is JProperty prop)
                        {
                            prop.Value = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }
                }
                return; // Don't recurse into MongoDB date object
            }
            
            // Check if this is a MongoDB ObjectId
            if (obj.Count == 1 && obj.ContainsKey("$oid"))
            {
                var oidValue = obj["$oid"]?.Value<string>();
                if (!string.IsNullOrEmpty(oidValue))
                {
                    // Replace the parent with the ObjectId string
                    if (obj.Parent is JProperty prop)
                    {
                        prop.Value = oidValue;
                    }
                }
                return; // Don't recurse into MongoDB oid object
            }
            
            // Recursively clean all properties
            var properties = obj.Properties().ToList();
            foreach (var prop in properties)
            {
                CleanMongoDbTypes(prop.Value);
            }
        }
        else if (token is JArray array)
        {
            // Recursively clean all array items
            foreach (var item in array)
            {
                CleanMongoDbTypes(item);
            }
        }
    }
    
    private static void CleanProductsInPath(JObject root, string path)
    {
        var token = root.SelectToken(path);
        if (token == null) return;
        
        if (token is JArray array)
        {
            CleanProductsInArray(array);
        }
    }
    
    private static void CleanProductsInArray(JArray array)
    {
        DebugLogger.Log($"[JsonCleaner] CleanProductsInArray called with {array.Count} items");
        
        // First, clean this array itself (remove category objects from products)
        CleanProductArray(array);
        
        // Then process nested structures
        foreach (var item in array)
        {
            if (item is JObject obj)
            {
                // Process categories
                var categories = obj.SelectToken("categories");
                if (categories is JArray catArray)
                {
                    DebugLogger.Log($"[JsonCleaner] Found categories array with {catArray.Count} items");
                    foreach (var catItem in catArray)
                    {
                        var category = catItem.SelectToken("category");
                        if (category is JObject categoryObj)
                        {
                            // Remove the entire products array to prevent circular references
                            if (categoryObj.ContainsKey("products"))
                            {
                                DebugLogger.Log("[JsonCleaner] Removing products array from category object");
                                categoryObj.Remove("products");
                            }
                        }
                    }
                }
                
                // Process courses
                var courses = obj.SelectToken("courses");
                if (courses is JArray courseArray)
                {
                    foreach (var courseItem in courseArray)
                    {
                        var course = courseItem.SelectToken("course");
                        if (course is JObject courseObj)
                        {
                            // Remove the entire products array to prevent circular references
                            if (courseObj.ContainsKey("products"))
                            {
                                courseObj.Remove("products");
                            }
                        }
                    }
                }
                
                // Process direct products array
                var directProducts = obj.SelectToken("products");
                if (directProducts is JArray directProdArray)
                {
                    CleanProductArray(directProdArray);
                }
            }
        }
    }
    
    private static void CleanProductArray(JArray products)
    {
        foreach (var product in products)
        {
            if (product is JObject productObj)
            {
                // Remove nested category object
                if (productObj.ContainsKey("category"))
                {
                    productObj.Remove("category");
                }
                
                // Remove nested course object
                if (productObj.ContainsKey("course"))
                {
                    productObj.Remove("course");
                }
                
                // Remove rawValue if present
                if (productObj.ContainsKey("rawValue"))
                {
                    productObj.Remove("rawValue");
                }
                
                // Recursively clean children
                var children = productObj.SelectToken("children");
                if (children is JArray childArray)
                {
                    CleanProductArray(childArray);
                }
            }
        }
    }
    
    private static string CleanTemplateDataWithRegex(string templateData)
    {
        // This is a fallback method using regex to remove nested objects
        // It's less precise but works when JSON parsing fails
        
        // Pattern to find products array and clean each product
        var pattern = @"""products""\s*:\s*\[(.*?)\](?=\s*[,}])";
        
        return Regex.Replace(templateData, pattern, match =>
        {
            var productsContent = match.Groups[1].Value;
            
            // Remove category objects within products
            productsContent = Regex.Replace(productsContent, 
                @",?\s*""category""\s*:\s*\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", 
                "", 
                RegexOptions.Singleline);
            
            // Remove course objects within products
            productsContent = Regex.Replace(productsContent, 
                @",?\s*""course""\s*:\s*\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", 
                "", 
                RegexOptions.Singleline);
            
            // Remove rawValue objects within products
            productsContent = Regex.Replace(productsContent, 
                @",?\s*""rawValue""\s*:\s*\{[^{}]*\}", 
                "", 
                RegexOptions.Singleline);
            
            // Clean up any double commas or trailing commas
            productsContent = Regex.Replace(productsContent, @",\s*,", ",");
            productsContent = Regex.Replace(productsContent, @",\s*(?=\])", "");
            
            return $@"""products"":[{productsContent}]";
        }, RegexOptions.Singleline);
    }
}