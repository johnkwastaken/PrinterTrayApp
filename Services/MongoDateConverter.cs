using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrinterTrayApp.Services;

public class MongoDateConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
            
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Handle MongoDB date format: {"$date": 1751700552.312}
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$date", out var dateElement))
            {
                if (dateElement.ValueKind == JsonValueKind.Number)
                {
                    var unixTime = dateElement.GetDouble();
                    // Check if it's in seconds (with decimal) or milliseconds
                    // Values larger than year 3000 in seconds (32503680000) are likely milliseconds
                    if (unixTime < 32503680000)
                    {
                        // It's in seconds with decimal fractions
                        var seconds = (long)unixTime;
                        var milliseconds = (long)((unixTime - seconds) * 1000);
                        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddMilliseconds(milliseconds).DateTime;
                    }
                    else
                    {
                        // It's in milliseconds
                        return DateTimeOffset.FromUnixTimeMilliseconds((long)unixTime).DateTime;
                    }
                }
            }
            return null;
        }
        
        if (reader.TokenType == JsonTokenType.String)
        {
            var dateStr = reader.GetString();
            if (DateTime.TryParse(dateStr, out var date))
                return date;
        }
        
        return null;
    }
    
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("O"));
        else
            writer.WriteNullValue();
    }
}

public class FlexibleBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String => bool.TryParse(reader.GetString(), out var b) && b,
            JsonTokenType.Number => reader.GetInt32() != 0,
            _ => false
        };
    }
    
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}