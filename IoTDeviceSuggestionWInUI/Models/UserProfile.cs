using System;
using System.Collections.Generic;
using System.Linq;

namespace IoTDeviceSuggestionWInUI.Models
{
    /// <summary>
    /// Represents a user's profile with rooms, connected devices, region, and season.
    /// </summary>
    public class UserProfile
    {
        public string UserId { get; set; } = string.Empty;
        public List<string> Rooms { get; set; } = new();
        public List<string> RoomFunctionTags { get; set; } = new();
        public Dictionary<string, List<DeviceItem>> ConnectedDevicesByRoom { get; set; } = new();
        public List<DeviceItem> OwnedDevices { get; set; } = new();
        
        /// <summary>
        /// User's geographic region for regional usage scoring.
        /// Examples: "S.E. Asia", "North America", "Europe", "OTHER"
        /// </summary>
        public string Region { get; set; } = "OTHER";
        
        /// <summary>
        /// Current season for season-based recommendations.
        /// Examples: "summer", "winter", "monsoon", "autumn", "spring"
        /// </summary>
        public string Season { get; set; } = "summer";

        /// <summary>
        /// Build text representation for embedding.
        /// </summary>
        public string ToEmbeddingText()
        {
            var roomsText = string.Join(", ", Rooms);
            var devicesText = string.Join("; ", OwnedDevices.Select(d => $"{d.Name} ({d.Category})"));
            var tagsText = string.Join(", ", RoomFunctionTags);
            return $"Home rooms: {roomsText}. Room functions: {tagsText}. Connected devices: {devicesText}. Region: {Region}. Season: {Season}. Recommend complementary IoT devices.";
        }
    }

    /// <summary>
    /// Represents a single device item owned by the user.
    /// </summary>
    public class DeviceItem
    {
        public string Name { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
    }
}