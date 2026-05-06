using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IoTDeviceSuggestionWInUI.Models;
using IoTDeviceSuggestionWInUI.Services;

namespace IoTDeviceSuggestionWInUI.ViewModels
{
    /// <summary>
    /// Main ViewModel for the IoT Device Recommendation application.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly IRecommendationService? _recommendationService;
        private readonly DeviceRepository? _repository;
        private readonly IWeatherService? _weatherService;

        [ObservableProperty]
        private string _selectedRoom = string.Empty;

        [ObservableProperty]
        private string _ownedDevicesText = string.Empty;

        [ObservableProperty]
        private string _selectedRegion = "OTHER";

        [ObservableProperty]
        private string _latitudeText = "23.8103"; // Default: Dhaka, Bangladesh

        [ObservableProperty]
        private string _longitudeText = "90.4125"; // Default: Dhaka, Bangladesh

        [ObservableProperty]
        private string _detectedSeason = "summer";

        [ObservableProperty]
        private bool _isSeasonDetected = false;

        [ObservableProperty]
        private List<RecommendationResult> _recommendations = new();

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ObservableCollection<string> AvailableRooms { get; } = new();
        
        public ObservableCollection<string> AvailableRegions { get; } = new();

        public MainViewModel(IRecommendationService recommendationService, DeviceRepository repository, IWeatherService? weatherService = null)
        {
            _recommendationService = recommendationService;
            _repository = repository;
            _weatherService = weatherService;
            LoadRooms();
            LoadRegions();
        }

        public MainViewModel()
        {
            // Default constructor for design-time - add default rooms
            foreach (var room in GetDefaultRooms())
            {
                AvailableRooms.Add(room);
            }
            LoadRegions();
        }

        private void LoadRooms()
        {
            AvailableRooms.Clear();
            
            try
            {
                if (_repository != null)
                {
                    var rooms = _repository.GetAvailableRooms();
                    if (rooms.Count > 0)
                    {
                        foreach (var room in rooms)
                        {
                            AvailableRooms.Add(room);
                        }
                        StatusMessage = $"Loaded {AvailableRooms.Count} rooms";
                        Console.WriteLine($"[INIT] Loaded {AvailableRooms.Count} rooms from data file");
                    }
                    else
                    {
                        foreach (var room in GetDefaultRooms())
                        {
                            AvailableRooms.Add(room);
                        }
                        StatusMessage = "Using default rooms (no data found)";
                        Console.WriteLine("[INIT] Using default rooms (no data found)");
                    }
                }
                else
                {
                    foreach (var room in GetDefaultRooms())
                    {
                        AvailableRooms.Add(room);
                    }
                    StatusMessage = "Using default rooms (no repository)";
                    Console.WriteLine("[INIT] Using default rooms (no repository)");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading rooms: {ex.Message}";
                Console.WriteLine($"[ERROR] Error loading rooms: {ex.Message}");
                foreach (var room in GetDefaultRooms())
                {
                    AvailableRooms.Add(room);
                }
            }
        }

        private void LoadRegions()
        {
            AvailableRegions.Clear();
            var regions = GetDefaultRegions();
            foreach (var region in regions)
            {
                AvailableRegions.Add(region);
            }
            Console.WriteLine($"[INIT] Loaded {AvailableRegions.Count} regions");
        }

        private List<string> GetDefaultRooms()
        {
            return new List<string>
            {
                "Living Room",
                "Bedroom", 
                "Kitchen",
                "Bathroom",
                "Office",
                "Laundry Room",
                "Garage",
                "Entrance"
            };
        }

        private List<string> GetDefaultRegions()
        {
            return new List<string>
            {
                "S.E ASIA",
                "NORTH AMERICA",
                "EUROPE",
                "KOREA",
                "JAPAN",
                "CHINA",
                "MIDDLE EAST",
                "LATIN AMERICA",
                "AFRICA",
                "S.W ASIA",
                "CIS",
                "OTHER"
            };
        }

        [RelayCommand]
        private async Task DetectSeasonFromLocation()
        {
            if (_weatherService == null)
            {
                StatusMessage = "Weather service not available";
                Console.WriteLine("[Weather] Weather service not initialized");
                return;
            }

            // Parse latitude and longitude
            if (!double.TryParse(LatitudeText, out double latitude))
            {
                StatusMessage = "Invalid latitude value";
                Console.WriteLine("[Weather] Invalid latitude");
                return;
            }

            if (!double.TryParse(LongitudeText, out double longitude))
            {
                StatusMessage = "Invalid longitude value";
                Console.WriteLine("[Weather] Invalid longitude");
                return;
            }

            Console.WriteLine("");
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    AUTO-DETECTING SEASON FROM WEATHER                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");

            StatusMessage = "Fetching weather data...";

            try
            {
                // Step 1: Get weather data
                var weatherData = await _weatherService.GetWeatherDataAsync(latitude, longitude);

                if (weatherData == null || weatherData.Current == null)
                {
                    StatusMessage = "Failed to fetch weather data. Using default season.";
                    DetectedSeason = "summer";
                    IsSeasonDetected = false;
                    return;
                }

                // Step 2: Detect season from weather
                DetectedSeason = _weatherService.DetectSeason(weatherData, latitude);
                IsSeasonDetected = true;

                StatusMessage = $"Season detected: {DetectedSeason.ToUpperInvariant()} (Temp: {weatherData.Current.TemperatureCelsius}°C, Humidity: {weatherData.Current.HumidityPercent}%)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error detecting season: {ex.Message}";
                Console.WriteLine($"[Weather] Error: {ex.Message}");
                DetectedSeason = "summer";
                IsSeasonDetected = false;
            }
        }

        [RelayCommand]
        private async Task GetRecommendations()
        {
            Console.WriteLine("");
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    GET RECOMMENDATIONS BUTTON CLICKED                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");

            if (string.IsNullOrEmpty(SelectedRoom))
            {
                StatusMessage = "Please select a room first.";
                Console.WriteLine("[ERROR] No room selected");
                return;
            }

            if (_recommendationService == null || _repository == null)
            {
                StatusMessage = "Services not initialized.";
                Console.WriteLine("[ERROR] Services not initialized");
                return;
            }

            IsLoading = true;
            StatusMessage = "Starting recommendation process...";

            try
            {
                // Parse latitude and longitude
                if (!double.TryParse(LatitudeText, out double latitude))
                {
                    StatusMessage = "Invalid latitude value";
                    IsLoading = false;
                    return;
                }

                if (!double.TryParse(LongitudeText, out double longitude))
                {
                    StatusMessage = "Invalid longitude value";
                    IsLoading = false;
                    return;
                }

                // Auto-detect season from weather if weather service is available
                string season = DetectedSeason;
                if (_weatherService != null)
                {
                    StatusMessage = "Fetching weather data for season detection...";
                    
                    var weatherData = await _weatherService.GetWeatherDataAsync(latitude, longitude);
                    if (weatherData?.Current != null)
                    {
                        season = _weatherService.DetectSeason(weatherData, latitude);
                        DetectedSeason = season;
                        IsSeasonDetected = true;
                        StatusMessage = $"Season detected: {season.ToUpperInvariant()}";
                    }
                    else
                    {
                        Console.WriteLine("[Weather] Could not fetch weather, using previous season value");
                    }
                }

                Console.WriteLine("[INPUT] User Input:");
                Console.WriteLine($"  Selected Room: {SelectedRoom}");
                Console.WriteLine($"  Region: {SelectedRegion}");
                Console.WriteLine($"  Latitude: {latitude}");
                Console.WriteLine($"  Longitude: {longitude}");
                Console.WriteLine($"  Season (auto-detected): {season}");
                Console.WriteLine($"  Owned Devices Text: \"{OwnedDevicesText}\"");
                Console.WriteLine("");

                // Parse owned devices from text (device names)
                var ownedDevices = ParseOwnedDevices(OwnedDevicesText);
                
                Console.WriteLine("[PARSING] Parsed owned devices:");
                foreach (var device in ownedDevices)
                {
                    Console.WriteLine($"  - Device Name: \"{device.Name}\"");
                }
                Console.WriteLine("");

                // Look up categories for each device name
                Console.WriteLine("[LOOKUP] Looking up device categories:");
                foreach (var device in ownedDevices)
                {
                    var foundCategory = _repository.FindDeviceCategory(device.Name, device.Brand);
                    device.Category = foundCategory;
                    Console.WriteLine($"  - \"{device.Name}\" -> Category: \"{device.Category}\"");
                }
                Console.WriteLine("");

                StatusMessage = "Computing embeddings and generating recommendations...";
                Console.WriteLine("[INFO] Calling GetRecommendationsAsync...");
                Console.WriteLine("[INFO] This may take a while on first run (computing embeddings for 2000+ devices)");
                Console.WriteLine("");

                Recommendations = await _recommendationService.GetRecommendationsAsync(userProfile: new UserProfile
                {
                    UserId = "user1",
                    Rooms = new List<string> { SelectedRoom },
                    OwnedDevices = ownedDevices,
                    Region = SelectedRegion,
                    Season = season
                });
                
                StatusMessage = $"Found {Recommendations.Count} recommendations for {SelectedRoom} (Season: {season})";
                
                Console.WriteLine("");
                Console.WriteLine("[RESULT] Recommendations displayed in UI:");
                foreach (var r in Recommendations)
                {
                    Console.WriteLine($"  #{r.Rank}: {r.DeviceName} (Score: {r.Score:F4})");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Console.WriteLine($"[ERROR] {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ClearInput()
        {
            Console.WriteLine("");
            Console.WriteLine("[CLEAR] Clearing all input and results");
            Console.WriteLine("");
            
            SelectedRoom = string.Empty;
            OwnedDevicesText = string.Empty;
            SelectedRegion = "OTHER";
            LatitudeText = "23.8103";
            LongitudeText = "90.4125";
            DetectedSeason = "summer";
            IsSeasonDetected = false;
            Recommendations = new List<RecommendationResult>();
            StatusMessage = string.Empty;
        }

        private List<DeviceItem> ParseOwnedDevices(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("[PARSE] No owned devices provided");
                return new List<DeviceItem>();
            }

            var devices = text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d =>
                {
                    // Try to split by space to get brand (if provided)
                    var parts = d.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    return new DeviceItem
                    {
                        Name = d, // Use full device name
                        Brand = parts.Length > 1 ? parts[1] : string.Empty,
                        Category = string.Empty // Will be looked up later
                    };
                })
                .ToList();

            Console.WriteLine($"[PARSE] Parsed {devices.Count} device names from input");
            return devices;
        }
    }
}