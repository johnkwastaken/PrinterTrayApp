using System;
using Newtonsoft.Json.Linq;
using PrinterTrayApp.Services;

namespace PrinterTrayApp;

public class TestMongoDbCleaner
{
    public static void RunTest()
    {
        // Test JSON with MongoDB dates at different levels
        string testJson = @"{
  ""_id"": {
    ""id"": ""test-001"",
    ""siteId"": ""site-001""
  },
  ""createdTime"": {
    ""$date"": 1751688065.611
  },
  ""printedTime"": {
    ""$date"": 1751688068.157916
  },
  ""template"": {
    ""createdTime"": {
      ""$date"": 1747649800.171
    },
    ""body"": ""<root>test</root>""
  },
  ""templateData"": ""{\""test\"":{\""$date\"":1751688065.611}}""
}";

        Console.WriteLine("Original JSON:");
        Console.WriteLine(testJson);
        Console.WriteLine("\n" + new string('=', 50) + "\n");

        try
        {
            // Test the cleaner
            string cleaned = JsonCleaner.CleanFullJson(testJson);
            
            Console.WriteLine("Cleaned JSON:");
            Console.WriteLine(cleaned);
            Console.WriteLine("\n" + new string('=', 50) + "\n");
            
            // Verify it can be parsed
            var parsed = JObject.Parse(cleaned);
            
            // Check that dates were converted
            var createdTime = parsed["createdTime"]?.ToString();
            var printedTime = parsed["printedTime"]?.ToString();
            var templateCreatedTime = parsed["template"]?["createdTime"]?.ToString();
            
            Console.WriteLine("Verification:");
            Console.WriteLine($"createdTime: {createdTime}");
            Console.WriteLine($"printedTime: {printedTime}");
            Console.WriteLine($"template.createdTime: {templateCreatedTime}");
            
            // Check if any $date objects remain
            bool hasMongoDate = cleaned.Contains("\"$date\"");
            Console.WriteLine($"\nContains MongoDB dates: {hasMongoDate}");
            
            if (!hasMongoDate)
            {
                Console.WriteLine("\n✓ SUCCESS: All MongoDB dates were cleaned!");
            }
            else
            {
                Console.WriteLine("\n✗ FAILED: MongoDB dates still present!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
    
    public static void Main()
    {
        RunTest();
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}