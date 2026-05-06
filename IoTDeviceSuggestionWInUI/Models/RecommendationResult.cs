 using System.Collections.Generic;

namespace IoTDeviceSuggestionWInUI.Models
{
    /// <summary>
    /// Represents a single recommendation result with all scoring components.
    /// </summary>
    public class RecommendationResult
    {
        public int Rank { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Rooms { get; set; } = string.Empty;
        public List<string> MatchedRooms { get; set; } = new();
        public string FeatureText { get; set; } = string.Empty;

        // Score breakdown - all 5 components matching Python implementation
        public double Score { get; set; }
        public double EmbeddingScore { get; set; }
        public double RoomScore { get; set; }
        public double RuleScore { get; set; }
        public double RegionalScore { get; set; }
        public double SeasonScore { get; set; }

        // LLM-generated reasoning
        public string Reasoning { get; set; } = string.Empty;

        /// <summary>
        /// Returns a formatted score breakdown string.
        /// </summary>
        public string GetScoreBreakdown()
        {
            return $"Score: {Score:F4} (Embedding: {EmbeddingScore:F4}, Room: {RoomScore:F4}, Rule: {RuleScore:F4}, Regional: {RegionalScore:F4}, Season: {SeasonScore:F4})";
        }
    }
}