using System.Text.Json.Serialization;

namespace IoTDeviceSuggestionWInUI.Models
{
    /// <summary>
    /// Weather data response from Open-Meteo API.
    /// </summary>
    public class WeatherData
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("current")]
        public CurrentWeather? Current { get; set; }

        /// <summary>
        /// Detected season based on weather data.
        /// </summary>
        public string DetectedSeason { get; set; } = "summer";
    }

    public class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")]
        public double TemperatureCelsius { get; set; }

        [JsonPropertyName("relative_humidity_2m")]
        public double HumidityPercent { get; set; }

        [JsonPropertyName("precipitation")]
        public double PrecipitationMm { get; set; }

        [JsonPropertyName("rain")]
        public double RainMm { get; set; }

        [JsonPropertyName("weather_code")]
        public int WeatherCode { get; set; }
    }
}