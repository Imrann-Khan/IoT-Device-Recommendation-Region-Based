using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTDeviceSuggestionWInUI.Converters
{
    /// <summary>
    /// JSON converter that handles both string and number types, converting them to string.
    /// </summary>
    public class StringOrNumberConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.GetDouble().ToString(),
                JsonTokenType.Null => string.Empty,
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to string")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}