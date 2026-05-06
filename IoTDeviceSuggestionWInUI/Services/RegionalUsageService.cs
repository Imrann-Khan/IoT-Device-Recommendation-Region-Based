using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Service for loading and querying regional usage data.
    /// Implements smart fallback using median of other regions when user's region is not found.
    /// </summary>
    public class RegionalUsageService
    {
        private readonly Dictionary<string, Dictionary<string, double>> _regionalIndex;
        private readonly HashSet<string> _availableRegions;
        private readonly string _defaultRegion;
        private readonly double _fallbackScore;

        // Known region name mappings (case-insensitive)
        private static readonly Dictionary<string, string> RegionAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "s.e. asia", "S.E ASIA" },
            { "s.e asia", "S.E ASIA" },
            { "southeast asia", "S.E ASIA" },
            { "se asia", "S.E ASIA" },
            { "s.w. asia", "S.W ASIA" },
            { "s.w asia", "S.W ASIA" },
            { "southwest asia", "S.W ASIA" },
            { "sw asia", "S.W ASIA" },
            { "north america", "NORTH AMERICA" },
            { "na", "NORTH AMERICA" },
            { "latin america", "LATIN AMERICA" },
            { "la", "LATIN AMERICA" },
            { "middle east", "MIDDLE EAST" },
            { "me", "MIDDLE EAST" },
            { "africa", "AFRICA" },
            { "europe", "EUROPE" },
            { "eu", "EUROPE" },
            { "china", "CHINA" },
            { "cn", "CHINA" },
            { "japan", "JAPAN" },
            { "jp", "JAPAN" },
            { "korea", "KOREA" },
            { "kr", "KOREA" },
            { "south korea", "KOREA" },
            { "cis", "CIS" },
            { "other", "OTHER" },
        };

        public RegionalUsageService(string filePath, string defaultRegion = "OTHER", double fallbackScore = 0.5)
        {
            _regionalIndex = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            _availableRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _defaultRegion = NormalizeRegion(defaultRegion);
            _fallbackScore = fallbackScore;

            LoadRegionalData(filePath);
        }

        /// <summary>
        /// Load regional usage data from JSON file.
        /// </summary>
        private void LoadRegionalData(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[RegionalUsage] Warning: Regional usage file not found: {filePath}");
                return;
            }

            try
            {
                var jsonContent = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(jsonContent);

                if (data == null || data.Count == 0)
                {
                    Console.WriteLine("[RegionalUsage] Warning: No data loaded from file");
                    return;
                }

                foreach (var item in data)
                {
                    if (!item.TryGetValue("Category", out var categoryElement))
                        continue;

                    var category = categoryElement.GetString();
                    if (string.IsNullOrEmpty(category))
                        continue;

                    var regionScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in item)
                    {
                        if (kvp.Key.Equals("Category", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var regionName = kvp.Key;
                        var score = kvp.Value.GetDouble();

                        regionScores[regionName] = score;
                        _availableRegions.Add(regionName);
                    }

                    _regionalIndex[category] = regionScores;
                }

                Console.WriteLine($"[RegionalUsage] Loaded {_regionalIndex.Count} categories for {_availableRegions.Count} regions");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RegionalUsage] Error loading regional data: {ex.Message}");
            }
        }

        /// <summary>
        /// Get regional usage score for a category and region.
        /// Implements smart fallback using median of other regions.
        /// </summary>
        public double GetRegionalScore(string category, string region)
        {
            if (_regionalIndex.Count == 0)
                return _fallbackScore;

            var normalizedRegion = NormalizeRegion(region);
            var normalizedCategory = NormalizeCategory(category);

            // Try to find the category
            if (!_regionalIndex.TryGetValue(normalizedCategory, out var regionScores))
            {
                // Try partial match
                var matchingCategory = _regionalIndex.Keys
                    .FirstOrDefault(k => k.Contains(normalizedCategory) || normalizedCategory.Contains(k));

                if (matchingCategory == null)
                    return _fallbackScore;

                regionScores = _regionalIndex[matchingCategory];
            }

            // Try to get score for the specific region
            if (regionScores.TryGetValue(normalizedRegion, out var score))
            {
                return NormalizeScore(score);
            }

            // Fallback: Use median of other regions
            return CalculateMedianFallback(regionScores);
        }

        /// <summary>
        /// Normalize region name to match the data format.
        /// </summary>
        private string NormalizeRegion(string region)
        {
            if (string.IsNullOrEmpty(region))
                return _defaultRegion;

            var normalized = region.Trim().ToUpperInvariant();

            // Check aliases
            if (RegionAliases.TryGetValue(region, out var mapped))
                return mapped;

            // Check if it exists as-is
            if (_availableRegions.Contains(region))
                return region;

            // Try case-insensitive match
            var match = _availableRegions.FirstOrDefault(r => 
                r.Equals(region, StringComparison.OrdinalIgnoreCase));

            return match ?? _defaultRegion;
        }

        /// <summary>
        /// Normalize category name for matching.
        /// </summary>
        private static string NormalizeCategory(string category)
        {
            if (string.IsNullOrEmpty(category))
                return string.Empty;

            return category.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Normalize raw score to 0.0-1.0 range.
        /// Uses min-max normalization with clipping.
        /// </summary>
        private double NormalizeScore(double rawScore)
        {
            // Most scores in the data are small decimals
            // Apply a sigmoid-like normalization for better distribution
            if (rawScore <= 0)
                return 0.0;

            if (rawScore >= 1.0)
                return 1.0;

            // Use square root for better spread of small values
            return Math.Sqrt(rawScore);
        }

        /// <summary>
        /// Calculate median fallback score when region is not found.
        /// This matches the Python implementation's smart fallback logic.
        /// </summary>
        private double CalculateMedianFallback(Dictionary<string, double> regionScores)
        {
            if (regionScores.Count == 0)
                return _fallbackScore;

            var sortedScores = regionScores.Values.OrderBy(v => v).ToList();
            var count = sortedScores.Count;

            if (count % 2 == 0)
            {
                // Even number of elements - average the two middle ones
                var mid1 = sortedScores[count / 2 - 1];
                var mid2 = sortedScores[count / 2];
                return NormalizeScore((mid1 + mid2) / 2.0);
            }
            else
            {
                // Odd number - take the middle one
                return NormalizeScore(sortedScores[count / 2]);
            }
        }

        /// <summary>
        /// Get all available regions.
        /// </summary>
        public IReadOnlyCollection<string> GetAvailableRegions()
        {
            return _availableRegions.ToList().AsReadOnly();
        }

        /// <summary>
        /// Get all available categories.
        /// </summary>
        public IReadOnlyCollection<string> GetAvailableCategories()
        {
            return _regionalIndex.Keys.ToList().AsReadOnly();
        }

        /// <summary>
        /// Check if regional data is loaded.
        /// </summary>
        public bool HasData => _regionalIndex.Count > 0;
    }
}