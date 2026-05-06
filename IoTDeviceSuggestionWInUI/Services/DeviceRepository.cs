using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using IoTDeviceSuggestionWInUI.Models;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Repository for loading device data and configuration.
    /// </summary>
    public class DeviceRepository
    {
        private readonly string _dataPath;
        private readonly string _configPath;
        private List<DeviceRecord>? _cachedDevices;
        private HybridConfig? _cachedConfig;

        public DeviceRepository(string dataPath, string configPath)
        {
            _dataPath = dataPath;
            _configPath = configPath;
        }

        /// <summary>
        /// Load all devices from the JSON file.
        /// </summary>
        public List<DeviceRecord> LoadDevices()
        {
            if (_cachedDevices != null)
                return _cachedDevices;

            if (!File.Exists(_dataPath))
                throw new FileNotFoundException($"Device data file not found: {_dataPath}");

            Console.WriteLine($"[DeviceRepository] Loading devices from: {_dataPath}");
            
            var json = File.ReadAllText(_dataPath);
            
            // Use permissive options to handle mixed types (e.g., size can be string or number)
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
            };
            
            var data = JsonSerializer.Deserialize<DeviceData>(json, options);
            _cachedDevices = data?.Devices ?? new List<DeviceRecord>();
            
            Console.WriteLine($"[DeviceRepository] Loaded {_cachedDevices.Count} devices");
            
            // Log first few devices to verify categories are loaded
            foreach (var device in _cachedDevices.Take(5))
            {
                Console.WriteLine($"[DeviceRepository] Sample device: Name=\"{device.Name}\", Category=\"{device.Category}\"");
            }
            
            // Count devices with empty categories
            var emptyCategories = _cachedDevices.Count(d => string.IsNullOrEmpty(d.Category));
            if (emptyCategories > 0)
            {
                Console.WriteLine($"[DeviceRepository] WARNING: {emptyCategories} devices have empty categories!");
            }
            
            return _cachedDevices;
        }

        /// <summary>
        /// Load configuration from the JSON file.
        /// </summary>
        public HybridConfig LoadConfig()
        {
            if (_cachedConfig != null)
                return _cachedConfig;

            if (!File.Exists(_configPath))
                throw new FileNotFoundException($"Config file not found: {_configPath}");

            var json = File.ReadAllText(_configPath);
            _cachedConfig = JsonSerializer.Deserialize<HybridConfig>(json) ?? new HybridConfig();
            return _cachedConfig;
        }

        /// <summary>
        /// Get device categories.
        /// </summary>
        public List<string> GetCategories()
        {
            var devices = LoadDevices();
            return devices.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();
        }

        /// <summary>
        /// Get available rooms.
        /// </summary>
        public List<string> GetAvailableRooms()
        {
            var devices = LoadDevices();
            var rooms = new HashSet<string>();
            foreach (var device in devices)
            {
                var deviceRooms = device.Rooms.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var room in deviceRooms)
                {
                    rooms.Add(room.Trim());
                }
            }
            return rooms.OrderBy(r => r).ToList();
        }

        /// <summary>
        /// Find device category by name with smart matching.
        /// </summary>
        public string FindDeviceCategory(string deviceName, string? brand = null)
        {
            var devices = LoadDevices();
            var deviceNameLower = deviceName.ToLower().Trim();
            var categories = devices.Select(d => d.Category.ToLower()).Distinct().ToList();
            
            Console.WriteLine($"[LOOKUP] Finding category for: \"{deviceName}\"");

            // Step 1: Check if the device name matches a category name directly
            var matchingCategory = categories.FirstOrDefault(c => 
                c == deviceNameLower || 
                deviceNameLower.Contains(c) ||
                c.Contains(deviceNameLower));
            
            if (matchingCategory != null)
            {
                Console.WriteLine($"[LOOKUP] Matched category name directly: \"{matchingCategory}\"");
                // Return the proper category name from the dataset
                var categoryDevice = devices.FirstOrDefault(d => d.Category.ToLower() == matchingCategory);
                if (categoryDevice != null)
                {
                    Console.WriteLine($"[LOOKUP] Returning category: \"{categoryDevice.Category}\"");
                    return categoryDevice.Category;
                }
            }

            // Step 2: Try exact device name match
            var exactMatch = devices.FirstOrDefault(d => d.Name.ToLower() == deviceNameLower);
            if (exactMatch != null)
            {
                Console.WriteLine($"[LOOKUP] Exact device match: \"{exactMatch.Name}\" -> \"{exactMatch.Category}\"");
                return exactMatch.Category;
            }

            // Step 3: Try word-boundary matching (device name as a complete word in device names)
            var wordBoundaryMatch = devices.FirstOrDefault(d => 
            {
                var words = d.Name.ToLower().Split(new[] { ' ', '-', '/', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
                return words.Any(w => w == deviceNameLower);
            });
            
            if (wordBoundaryMatch != null)
            {
                Console.WriteLine($"[LOOKUP] Word boundary match: \"{wordBoundaryMatch.Name}\" -> \"{wordBoundaryMatch.Category}\"");
                return wordBoundaryMatch.Category;
            }

            // Step 4: Check if any category keywords are in the device name
            var categoryKeywords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "TV", new[] { "tv", "television", "smart tv" } },
                { "Air Conditioner", new[] { "air conditioner", "ac", "aircon", "air conditioning" } },
                { "Lighting", new[] { "light", "bulb", "lamp" } },
                { "Speaker", new[] { "speaker", "audio" } },
                { "Soundbar", new[] { "soundbar", "sound bar" } },
                { "Camera", new[] { "camera", "cam" } },
                { "Vacuum Cleaner", new[] { "vacuum", "cleaner" } },
                { "Router", new[] { "router", "wifi", "mesh" } },
                { "Hub", new[] { "hub", "bridge" } },
                { "Outlet", new[] { "outlet", "plug", "socket" } },
                { "Door Bell", new[] { "doorbell", "door bell", "chime" } },
                { "Window Treatment", new[] { "blind", "curtain", "shade", "window" } },
                { "Air Purifier", new[] { "purifier", "air quality" } },
                { "Button", new[] { "button", "switch" } },
                { "Roller Shade", new[] { "roller", "shade" } }
            };

            foreach (var kvp in categoryKeywords)
            {
                if (kvp.Value.Any(kw => deviceNameLower.Contains(kw) || kw.Contains(deviceNameLower)))
                {
                    // Verify this category exists in our dataset
                    var matchingCat = devices.FirstOrDefault(d => 
                        d.Category.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
                    if (matchingCat != null)
                    {
                        Console.WriteLine($"[LOOKUP] Keyword match: \"{deviceNameLower}\" -> category \"{matchingCat.Category}\"");
                        return matchingCat.Category;
                    }
                }
            }

            // Step 5: Fallback - try partial match but with preference for device names that START with the search term
            var startMatch = devices.FirstOrDefault(d => d.Name.ToLower().StartsWith(deviceNameLower + " ") || 
                                                          d.Name.ToLower().StartsWith(deviceNameLower + "-"));
            if (startMatch != null)
            {
                Console.WriteLine($"[LOOKUP] Start match: \"{startMatch.Name}\" -> \"{startMatch.Category}\"");
                return startMatch.Category;
            }

            Console.WriteLine($"[LOOKUP] No match found, returning \"Other\"");
            return "Other";
        }

        /// <summary>
        /// Clear cached data.
        /// </summary>
        public void ClearCache()
        {
            _cachedDevices = null;
            _cachedConfig = null;
        }

        private class DeviceData
        {
            [JsonPropertyName("devices")]
            public List<DeviceRecord>? Devices { get; set; }
        }
    }
}