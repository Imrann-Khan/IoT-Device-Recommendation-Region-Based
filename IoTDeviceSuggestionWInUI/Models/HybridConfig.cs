using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IoTDeviceSuggestionWInUI.Models
{
    /// <summary>
    /// Configuration for the hybrid recommendation system.
    /// Matches the Python hybrid_config.json structure.
    /// </summary>
    public class HybridConfig
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("llm_model")]
        public string LlmModel { get; set; } = string.Empty;

        [JsonPropertyName("top_n")]
        public int TopN { get; set; } = 8;

        [JsonPropertyName("retrieval_top_n")]
        public int RetrievalTopN { get; set; } = 20;

        [JsonPropertyName("weights")]
        public ConfigWeights Weights { get; set; } = new();

        [JsonPropertyName("room_function_tags")]
        public Dictionary<string, List<string>> RoomFunctionTags { get; set; } = new();

        [JsonPropertyName("complementary_categories")]
        public Dictionary<string, List<string>> ComplementaryCategories { get; set; } = new();

        [JsonPropertyName("regional_usage_file")]
        public string RegionalUsageFile { get; set; } = string.Empty;

        [JsonPropertyName("default_region")]
        public string DefaultRegion { get; set; } = "OTHER";

        [JsonPropertyName("regional_fallback_score")]
        public double RegionalFallbackScore { get; set; } = 0.5;

        [JsonPropertyName("season_category_map")]
        public Dictionary<string, List<string>> SeasonCategoryMap { get; set; } = new();

        /// <summary>
        /// Convenience property for embedding similarity weight (as float).
        /// </summary>
        [JsonIgnore]
        public float EmbeddingSimilarityWeight => (float)(Weights?.EmbeddingSimilarity ?? 0.67);

        /// <summary>
        /// Convenience property for room overlap weight (as float).
        /// </summary>
        [JsonIgnore]
        public float RoomOverlapWeight => (float)(Weights?.RoomOverlap ?? 0.10);

        /// <summary>
        /// Convenience property for complementary rule weight (as float).
        /// </summary>
        [JsonIgnore]
        public float ComplementaryRuleWeight => (float)(Weights?.ComplementaryRule ?? 0.10);

        /// <summary>
        /// Convenience property for regional usage weight (as float).
        /// </summary>
        [JsonIgnore]
        public float RegionalUsageWeight => (float)(Weights?.RegionalUsage ?? 0.10);
    }

    /// <summary>
    /// Weights for the hybrid scoring algorithm.
    /// Total should sum to 1.0.
    /// </summary>
    public class ConfigWeights
    {
        [JsonPropertyName("embedding_similarity")]
        public double EmbeddingSimilarity { get; set; } = 0.67;

        [JsonPropertyName("room_overlap")]
        public double RoomOverlap { get; set; } = 0.10;

        [JsonPropertyName("complementary_rule")]
        public double ComplementaryRule { get; set; } = 0.10;

        [JsonPropertyName("regional_usage")]
        public double RegionalUsage { get; set; } = 0.10;

        [JsonPropertyName("season")]
        public double Season { get; set; } = 0.03;
    }
}