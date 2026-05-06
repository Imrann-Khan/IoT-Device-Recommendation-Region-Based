using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IoTDeviceSuggestionWInUI.Models;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Weather service that fetches weather data from Open-Meteo API
    /// and detects season based on weather conditions.
    /// </summary>
    public class WeatherService : IWeatherService
    {
        private readonly HttpClient _httpClient;
        private const string OpenMeteoBaseUrl = "https://api.open-meteo.com/v1/forecast";

        public WeatherService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// Get current weather data for a location using Open-Meteo API.
        /// </summary>
        public async Task<WeatherData?> GetWeatherDataAsync(double latitude, double longitude)
        {
            Console.WriteLine("");
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    WEATHER API CALL - Open-Meteo                             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");

            try
            {
                // Build API URL with current weather parameters
                var url = $"{OpenMeteoBaseUrl}?" +
                          $"latitude={latitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}&" +
                          $"longitude={longitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}&" +
                          $"current=temperature_2m,relative_humidity_2m,precipitation,rain,weather_code";

                Console.WriteLine($"[Weather] Request Parameters:");
                Console.WriteLine($"  • Latitude:  {latitude:F4}°");
                Console.WriteLine($"  • Longitude: {longitude:F4}°");
                Console.WriteLine("");
                Console.WriteLine($"[Weather] API URL: {url}");
                Console.WriteLine("");

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"[Weather] API Response Status: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine("");
                Console.WriteLine("[Weather] Raw JSON Response:");
                Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
                Console.WriteLine(FormatJson(jsonContent));
                Console.WriteLine("────────────────────────────────────────────────────────────────────────────────");
                Console.WriteLine("");

                var weatherData = JsonSerializer.Deserialize<WeatherData>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (weatherData?.Current != null)
                {
                    Console.WriteLine("[Weather] Parsed Weather Data:");
                    Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────┐");
                    Console.WriteLine($"│  Temperature:     {weatherData.Current.TemperatureCelsius,8:F1}°C                         │");
                    Console.WriteLine($"│  Humidity:        {weatherData.Current.HumidityPercent,8:F1}%                          │");
                    Console.WriteLine($"│  Precipitation:   {weatherData.Current.PrecipitationMm,8:F2}mm                        │");
                    Console.WriteLine($"│  Rain:            {weatherData.Current.RainMm,8:F2}mm                        │");
                    Console.WriteLine($"│  Weather Code:    {weatherData.Current.WeatherCode,8}                            │");
                    Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
                    Console.WriteLine("");
                }

                return weatherData;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[Weather] ❌ HTTP Error: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[Weather] ❌ JSON Parse Error: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Weather] ❌ Request timed out");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Weather] ❌ Unexpected error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Detect season based on weather data and location.
        /// Uses weather-based detection for tropical regions (latitude -30° to +30°).
        /// Uses calendar-based detection for non-tropical regions.
        /// </summary>
        public string DetectSeason(WeatherData weatherData, double latitude)
        {
            Console.WriteLine("");
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    SEASON DETECTION                                          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");

            if (weatherData?.Current == null)
            {
                Console.WriteLine("[Weather] ⚠ No weather data available, defaulting to 'summer'");
                return "summer";
            }

            var current = weatherData.Current;
            var month = DateTime.Now.Month;
            var isTropical = Math.Abs(latitude) < 30; // Tropics: -30° to +30° latitude

            Console.WriteLine("[Weather] Input Parameters:");
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────┐");
            Console.WriteLine($"│  Latitude:        {latitude,8:F2}° {(isTropical ? "(TROPICAL)" : "(TEMPERATE)"),-20}│");
            Console.WriteLine($"│  Month:           {month,8} ({GetMonthName(month)})                      │");
            Console.WriteLine($"│  Temperature:     {current.TemperatureCelsius,8:F1}°C                         │");
            Console.WriteLine($"│  Humidity:        {current.HumidityPercent,8:F1}%                          │");
            Console.WriteLine($"│  Precipitation:   {current.PrecipitationMm,8:F2}mm                        │");
            Console.WriteLine($"│  Rain:            {current.RainMm,8:F2}mm                        │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
            Console.WriteLine("");

            string season;

            // ========== TROPICAL REGIONS: Weather-Based Detection ==========
            if (isTropical)
            {
                Console.WriteLine("[Weather] Using: TROPICAL detection (weather-based)");
                Console.WriteLine("");
                season = DetectTropicalSeason(current, month, latitude);
            }
            // ========== NON-TROPICAL: Calendar + Hemisphere ==========
            else
            {
                Console.WriteLine("[Weather] Using: TEMPERATE detection (calendar + hemisphere)");
                Console.WriteLine("");
                season = DetectTemperateSeason(month, latitude);
            }

            Console.WriteLine("");
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  ✓ DETECTED SEASON: {season.ToUpperInvariant(),-54}║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine("");

            return season;
        }

        /// <summary>
        /// Detect season for tropical regions using weather data.
        /// Tropical seasons are defined by temperature, humidity, and rainfall.
        /// </summary>
        private string DetectTropicalSeason(CurrentWeather current, int month, double latitude)
        {
            double temp = current.TemperatureCelsius;
            double humidity = current.HumidityPercent;
            double rainfall = current.RainMm;

            Console.WriteLine("[Weather] Step-by-step Detection:");
            Console.WriteLine("");

            // MONSOON: High rainfall OR very high humidity
            Console.WriteLine($"  Step 1: Check rainfall > 10mm?");
            Console.WriteLine($"          Rainfall = {rainfall:F2}mm");
            Console.WriteLine($"          Result: {(rainfall > 10 ? "YES → MONSOON" : "NO")}");
            if (rainfall > 10)
            {
                Console.WriteLine("");
                return "monsoon";
            }

            Console.WriteLine("");
            Console.WriteLine($"  Step 2: Check humidity > 85%?");
            Console.WriteLine($"          Humidity = {humidity:F1}%");
            Console.WriteLine($"          Result: {(humidity > 85 ? "YES → MONSOON" : "NO")}");
            if (humidity > 85)
            {
                Console.WriteLine("");
                return "monsoon";
            }

            Console.WriteLine("");
            Console.WriteLine($"  Step 3: Check temperature > 32°C?");
            Console.WriteLine($"          Temperature = {temp:F1}°C");
            Console.WriteLine($"          Result: {(temp > 32 ? "YES" : "NO")}");
            
            if (temp > 32)
            {
                Console.WriteLine("");
                Console.WriteLine($"  Step 4: High temp detected. Check humidity > 75%?");
                Console.WriteLine($"          Humidity = {humidity:F1}%");
                Console.WriteLine($"          Result: {(humidity > 75 ? "YES → MONSOON (hot + humid)" : "NO → SUMMER (hot + dry)")}");
                
                if (humidity > 75)
                {
                    Console.WriteLine("");
                    return "monsoon";
                }
                Console.WriteLine("");
                return "summer";
            }

            Console.WriteLine("");
            Console.WriteLine($"  Step 5: Check temperature < 22°C?");
            Console.WriteLine($"          Temperature = {temp:F1}°C");
            Console.WriteLine($"          Result: {(temp < 22 ? "YES → WINTER" : "NO")}");
            if (temp < 22)
            {
                Console.WriteLine("");
                return "winter";
            }

            Console.WriteLine("");
            Console.WriteLine($"  Step 6: Temperature is moderate (22-32°C).");
            Console.WriteLine($"          Using month to distinguish spring/autumn...");
            Console.WriteLine($"          Current month: {month} ({GetMonthName(month)})");
            
            // SPRING/AUTUMN: Moderate temperature (22-32°C)
            if (month >= 2 && month <= 4)
            {
                Console.WriteLine($"          Months 2-4 → SPRING");
                Console.WriteLine("");
                return "spring";
            }
            if (month >= 10 && month <= 11)
            {
                Console.WriteLine($"          Months 10-11 → AUTUMN");
                Console.WriteLine("");
                return "autumn";
            }

            // Default to summer for hot moderate days
            if (temp > 28)
            {
                Console.WriteLine($"          Temp > 28°C → SUMMER");
                Console.WriteLine("");
                return "summer";
            }

            Console.WriteLine($"          Default → SPRING");
            Console.WriteLine("");
            return "spring";
        }

        /// <summary>
        /// Detect season for temperate regions using calendar and hemisphere.
        /// </summary>
        private string DetectTemperateSeason(int month, double latitude)
        {
            bool isNorthernHemisphere = latitude >= 0;

            Console.WriteLine($"[Weather] Hemisphere: {(isNorthernHemisphere ? "NORTHERN" : "SOUTHERN")}");
            Console.WriteLine($"[Weather] Month: {month} ({GetMonthName(month)})");
            Console.WriteLine("");

            if (isNorthernHemisphere)
            {
                // Northern Hemisphere: Europe, North America, East Asia
                Console.WriteLine("[Weather] Northern Hemisphere calendar:");
                Console.WriteLine("  Mar-May: Spring | Jun-Aug: Summer | Sep-Nov: Autumn | Dec-Feb: Winter");
                
                if (month >= 3 && month <= 5) return "spring";
                if (month >= 6 && month <= 8) return "summer";
                if (month >= 9 && month <= 11) return "autumn";
                return "winter"; // Dec, Jan, Feb
            }
            else
            {
                // Southern Hemisphere: Australia, South America, South Africa
                Console.WriteLine("[Weather] Southern Hemisphere calendar:");
                Console.WriteLine("  Mar-May: Autumn | Jun-Aug: Winter | Sep-Nov: Spring | Dec-Feb: Summer");
                
                if (month >= 3 && month <= 5) return "autumn";
                if (month >= 6 && month <= 8) return "winter";
                if (month >= 9 && month <= 11) return "spring";
                return "summer"; // Dec, Jan, Feb
            }
        }

        private string GetMonthName(int month)
        {
            return month switch
            {
                1 => "January",
                2 => "February",
                3 => "March",
                4 => "April",
                5 => "May",
                6 => "June",
                7 => "July",
                8 => "August",
                9 => "September",
                10 => "October",
                11 => "November",
                12 => "December",
                _ => "Unknown"
            };
        }

        private string FormatJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
            catch
            {
                return json;
            }
        }
    }
}