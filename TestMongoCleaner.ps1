# PowerShell test script for MongoDB date cleaning

$testJson = @'
{
  "_id": {
    "id": "test-001",
    "siteId": "site-001"
  },
  "createdTime": {
    "$date": 1751688065.611
  },
  "printedTime": {
    "$date": 1751688068.157916
  },
  "template": {
    "createdTime": {
      "$date": 1747649800.171
    },
    "body": "<root>test</root>"
  }
}
'@

Write-Host "Testing MongoDB date cleaning..." -ForegroundColor Yellow
Write-Host "Original JSON:" -ForegroundColor Cyan
Write-Host $testJson

# Add the test method to Program.cs temporarily
$code = @'
using System;
using Newtonsoft.Json.Linq;

var testJson = @"{
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
  }
}";

Console.WriteLine("Original JSON:");
Console.WriteLine(testJson);
Console.WriteLine();

try 
{
    var obj = JObject.Parse(testJson);
    
    // Check if MongoDB dates exist
    var createdTime = obj["createdTime"];
    if (createdTime != null && createdTime is JObject ctObj)
    {
        if (ctObj.ContainsKey("$date"))
        {
            Console.WriteLine("Found MongoDB date at root.createdTime");
            var dateValue = ctObj["$date"];
            if (dateValue != null)
            {
                var unixTime = dateValue.Value<double>();
                DateTime dateTime;
                
                if (unixTime < 32503680000) // Year 3000 in seconds
                {
                    var seconds = (long)unixTime;
                    var milliseconds = (long)((unixTime - seconds) * 1000);
                    dateTime = DateTimeOffset.FromUnixTimeSeconds(seconds).AddMilliseconds(milliseconds).DateTime;
                }
                else
                {
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds((long)unixTime).DateTime;
                }
                
                Console.WriteLine($"Converted to: {dateTime:yyyy-MM-dd HH:mm:ss}");
                
                // Replace in the JSON
                obj["createdTime"] = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }
    }
    
    Console.WriteLine();
    Console.WriteLine("Cleaned JSON:");
    Console.WriteLine(obj.ToString());
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
'@

# Save and compile test
$code | Out-File -FilePath "MongoTest.cs" -Encoding UTF8

Write-Host "`nCompiling test..." -ForegroundColor Yellow
dotnet new console -n MongoTest -f net8.0 --force | Out-Null
Move-Item -Path "MongoTest.cs" -Destination "MongoTest\Program.cs" -Force
Set-Location "MongoTest"
dotnet add package Newtonsoft.Json | Out-Null
dotnet run

Set-Location ..
Remove-Item -Path "MongoTest" -Recurse -Force

Write-Host "`nTest complete!" -ForegroundColor Green