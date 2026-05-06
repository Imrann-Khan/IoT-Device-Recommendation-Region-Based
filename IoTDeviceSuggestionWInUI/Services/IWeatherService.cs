using System.Threading.Tasks;
using IoTDeviceSuggestionWInUI.Models;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Interface for weather service that fetches weather data and detects season.
    /// </summary>
    public interface IWeatherService
    {
        /// <summary>
        /// Get current weather data for a location.
        /// </summary>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        /// <returns>Weather data including temperature, humidity, precipitation</returns>
        Task<WeatherData?> GetWeatherDataAsync(double latitude, double longitude);

        /// <summary>
        /// Detect season based on weather data and location.
        /// Uses weather-based detection for tropical regions.
        /// Uses calendar-based detection for non-tropical regions.
        /// </summary>
        /// <param name="weatherData">Weather data from API</param>
        /// <param name="latitude">Latitude coordinate (determines hemisphere and tropical zone)</param>
        /// <returns>Season string: "summer", "winter", "monsoon", "autumn", or "spring"</returns>
        string DetectSeason(WeatherData weatherData, double latitude);
    }
}