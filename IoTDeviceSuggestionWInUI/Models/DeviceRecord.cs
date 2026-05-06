using System.Text.Json.Serialization;
using IoTDeviceSuggestionWInUI.Converters;

namespace IoTDeviceSuggestionWInUI.Models
{
    /// <summary>
    /// Represents a single IoT device from the dataset.
    /// </summary>
    public class DeviceRecord
    {
        [JsonPropertyName("name")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("category")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string Category { get; set; } = string.Empty;
        
        [JsonPropertyName("size")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string Size { get; set; } = string.Empty;
        
        [JsonPropertyName("rooms")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string Rooms { get; set; } = string.Empty;
        
        [JsonPropertyName("feature_text")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string FeatureText { get; set; } = string.Empty;

        /// <summary>
        /// Unique key for the device based on name and category.
        /// </summary>
        public string Key => $"{Name.ToLower()}|{Category.ToLower()}";

        /// <summary>
        /// Build text representation for embedding.
        /// </summary>
        public string ToEmbeddingText()
        {
            return $"Name: {Name} | Category: {Category} | Size: {Size} | Rooms: {Rooms} | Description: {FeatureText}";
        }
    }
}