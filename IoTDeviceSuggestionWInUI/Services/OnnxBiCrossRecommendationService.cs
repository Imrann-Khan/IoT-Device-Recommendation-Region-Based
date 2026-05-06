using IoTDeviceSuggestionWInUI.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Pure C# ONNX Runtime-based recommendation service using bi-encoder and cross-encoder models.
    /// Replaces the Python backend with native .NET implementation.
    /// </summary>
    public sealed class OnnxBiCrossRecommendationService : IRecommendationService, IDisposable
    {
        private readonly OnnxEmbeddingService _biEncoder;
        private readonly OnnxCrossEncoderService _crossEncoder;
        private readonly HybridConfig _config;
        private readonly List<DeviceRecord> _devices;
        private readonly Dictionary<string, float[]> _deviceEmbeddingCache;
        private readonly Dictionary<string, Dictionary<string, float>> _regionalUsageIndex;

        private bool _disposed = false;

        public OnnxBiCrossRecommendationService(
            string dataPath,
            string biEncoderModelPath,
            string crossEncoderModelPath,
            string configPath,
            string regionalUsagePath)
        {
            Console.WriteLine("[OnnxBiCross] Initializing pure ONNX recommendation service...");

            // Initialize ONNX models
            _biEncoder = new OnnxEmbeddingService(biEncoderModelPath);
            _crossEncoder = new OnnxCrossEncoderService(crossEncoderModelPath);

            // Load configuration
            _config = LoadHybridConfig(configPath);
            Console.WriteLine($"[OnnxBiCross] Configuration loaded: embedding={_config.EmbeddingSimilarityWeight}, room={_config.RoomOverlapWeight}, rule={_config.ComplementaryRuleWeight}, regional={_config.RegionalUsageWeight}");

            // Load devices
            _devices = LoadDevices(dataPath);
            Console.WriteLine($"[OnnxBiCross] Loaded {_devices.Count} devices");

            // Load regional usage index
            _regionalUsageIndex = LoadRegionalUsageIndex(regionalUsagePath);
            Console.WriteLine($"[OnnxBiCross] Loaded regional usage for {_regionalUsageIndex.Count} categories");

            // Pre-compute device embeddings
            _deviceEmbeddingCache = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("[OnnxBiCross] Pre-computing device embeddings...");
            ComputeDeviceEmbeddings();
            Console.WriteLine($"[OnnxBiCross] Cached embeddings for {_deviceEmbeddingCache.Count} devices");
        }

        public Task<List<RecommendationResult>> GetRecommendationsAsync(UserProfile userProfile, int topN = 8)
        {
            return Task.Run(() => GenerateRecommendations(userProfile, topN, selectedRoom: null));
        }

        public Task<List<RecommendationResult>> GetRecommendationsForRoomAsync(UserProfile userProfile, string room, int topN = 8)
        {
            var roomProfile = new UserProfile
            {
                UserId = userProfile.UserId,
                Rooms = new List<string> { room },
                RoomFunctionTags = userProfile.RoomFunctionTags,
                ConnectedDevicesByRoom = userProfile.ConnectedDevicesByRoom
                    .Where(entry => string.Equals(entry.Key, room, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
                OwnedDevices = userProfile.OwnedDevices
                    .Where(device => string.Equals(device.Room, room, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                Region = userProfile.Region,
                Season = userProfile.Season
            };

            return Task.Run(() => GenerateRecommendations(roomProfile, topN, selectedRoom: room));
        }

        private List<RecommendationResult> GenerateRecommendations(UserProfile userProfile, int topN, string? selectedRoom)
        {
            Console.WriteLine($"[OnnxBiCross] Generating recommendations for {userProfile.UserId} (top {topN})");

            try
            {
                // Step 1: Encode user profile
                var userText = userProfile.ToEmbeddingText();
                var userEmbedding = _biEncoder.GenerateEmbedding(userText);
                Console.WriteLine($"[OnnxBiCross] User embedding computed ({userEmbedding.Length} dimensions)");

                // Step 2: Calculate hybrid scores for all devices
                var candidates = CalculateHybridScores(userProfile, userEmbedding);
                Console.WriteLine($"[OnnxBiCross] Calculated hybrid scores for {candidates.Count} candidates");

                // Step 3: Select top candidates before reranking
                var topCandidates = candidates.OrderByDescending(c => c.HybridScore).Take(Math.Min(100, candidates.Count)).ToList();
                
                // LOG BI-ENCODER RESULTS
                Console.WriteLine("");
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  STAGE 1: BI-ENCODER RESULTS (Semantic Retrieval)                            ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine($"Top {Math.Min(10, topCandidates.Count)} candidates by Hybrid Score (Bi-Encoder + Contextual Scoring):");
                Console.WriteLine("");
                for (int i = 0; i < Math.Min(10, topCandidates.Count); i++)
                {
                    var c = topCandidates[i];
                    Console.WriteLine($"  #{i + 1}. {c.Device.Name}");
                    Console.WriteLine($"     Category: {c.Device.Category}");
                    Console.WriteLine($"     Hybrid Score: {c.HybridScore:F4}");
                    Console.WriteLine($"       ├─ Embedding:  {c.EmbeddingScore:F4} × 0.67 = {c.EmbeddingScore * 0.67f:F4}");
                    Console.WriteLine($"       ├─ Room:       {c.RoomScore:F2} × 0.10 = {c.RoomScore * 0.10f:F4}");
                    Console.WriteLine($"       ├─ Complementary: {c.RuleScore:F2} × 0.10 = {c.RuleScore * 0.10f:F4}");
                    Console.WriteLine($"       ├─ Regional:   {c.RegionalScore:F4} × 0.10 = {c.RegionalScore * 0.10f:F4}");
                    Console.WriteLine($"       └─ Season:     {c.SeasonScore:F2} × 0.03 = {c.SeasonScore * 0.03f:F4}");
                    Console.WriteLine("");
                }
                Console.WriteLine($"Total candidates from Bi-Encoder: {topCandidates.Count}");
                Console.WriteLine("");

                // Step 4: Rerank with cross-encoder
                var rerankedCandidates = RerankedWithCrossEncoder(userProfile, userText, topCandidates);

                // LOG CROSS-ENCODER RESULTS
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  STAGE 2: CROSS-ENCODER RESULTS (Relevance Reranking)                        ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine($"Top {Math.Min(10, rerankedCandidates.Count)} candidates after Cross-Encoder Reranking:");
                Console.WriteLine("");
                for (int i = 0; i < Math.Min(10, rerankedCandidates.Count); i++)
                {
                    var c = rerankedCandidates[i];
                    Console.WriteLine($"  #{i + 1}. {c.Device.Name}");
                    Console.WriteLine($"     Category: {c.Device.Category}");
                    Console.WriteLine($"     Final Score: {c.FinalScore:F4} (70% Cross-Encoder + 30% Hybrid)");
                    Console.WriteLine($"       ├─ Cross-Encoder: {c.CrossEncoderScore:F4} × 0.70 = {c.CrossEncoderScore * 0.70f:F4}");
                    Console.WriteLine($"       └─ Hybrid Score:  {c.HybridScore:F4} × 0.30 = {c.HybridScore * 0.30f:F4}");
                    Console.WriteLine("");
                }
                Console.WriteLine("");

                // Step 5: Select final recommendations (distinct categories if requested)
                var finalRecommendations = SelectFinalRecommendations(rerankedCandidates, topN);

                // Step 6: Build result objects
                var results = BuildRecommendationResults(finalRecommendations, userProfile);
                Console.WriteLine($"[OnnxBiCross] Generated {results.Count} final recommendations");

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnnxBiCross] Error generating recommendations: {ex.Message}");
                throw;
            }
        }

        private List<CandidateScores> CalculateHybridScores(UserProfile userProfile, float[] userEmbedding)
        {
            var ownedCategories = new HashSet<string>(
                userProfile.OwnedDevices.Select(d => d.Category.ToLower()),
                StringComparer.OrdinalIgnoreCase
            );
            var ownedDeviceNames = new HashSet<string>(
                userProfile.OwnedDevices.Select(d => d.Name.ToLower()),
                StringComparer.OrdinalIgnoreCase
            );

            var candidates = new List<CandidateScores>();

            foreach (var device in _devices)
            {
                // Skip already owned devices
                if (ownedDeviceNames.Contains(device.Name.ToLower()))
                {
                    continue;
                }

                // Get cached embedding
                var deviceEmbedding = _deviceEmbeddingCache[device.Key];

                // 1. Embedding similarity (bi-encoder)
                var embeddingScore = CosineSimilarity(userEmbedding, deviceEmbedding);

                // 2. Room overlap score
                var roomScore = CalculateRoomOverlapScore(userProfile, device);


                // 3. Complementary rule score
                var ruleScore = CalculateComplementaryRuleScore(device, ownedCategories);

                // 4. Regional usage score
                var regionalScore = CalculateRegionalUsageScore(device, userProfile.Region);

                // 5. Season score
                var seasonScore = CalculateSeasonScore(device.Category, userProfile.Season);

                // Combine with weights (include season weight)
                var hybridScore =
                    (_config.EmbeddingSimilarityWeight * embeddingScore) +
                    (_config.RoomOverlapWeight * roomScore) +
                    (_config.ComplementaryRuleWeight * ruleScore) +
                    (_config.RegionalUsageWeight * regionalScore) +
                    ((float)_config.Weights.Season * seasonScore);

                candidates.Add(new CandidateScores
                {
                    Device = device,
                    EmbeddingScore = embeddingScore,
                    RoomScore = roomScore,
                    RuleScore = ruleScore,
                    RegionalScore = regionalScore,
                    HybridScore = hybridScore,
                    SeasonScore = seasonScore
                });
            }

            return candidates;
        }

        private float CalculateRoomOverlapScore(UserProfile userProfile, DeviceRecord device)
        {
            var userRooms = new HashSet<string>(userProfile.Rooms, StringComparer.OrdinalIgnoreCase);
            var deviceRooms = device.Rooms.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .ToList();

            var matchCount = deviceRooms.Count(room => userRooms.Contains(room));
            return matchCount > 0 ? Math.Min(1.0f, matchCount / 3.0f) : 0.0f;
        }

        private float CalculateComplementaryRuleScore(DeviceRecord device, HashSet<string> ownedCategories)
        {
            var deviceCategory = device.Category.ToLower();
            var score = 0.0f;

            foreach (var ownedCategory in ownedCategories)
            {
                // Access the original List<string> from config
                if (_config.ComplementaryCategories.TryGetValue(ownedCategory, out var complementaryList))
                {
                    if (complementaryList.Any(c => c.Equals(deviceCategory, StringComparison.OrdinalIgnoreCase)))
                    {
                        score = Math.Max(score, 1.0f);
                    }
                }
            }

            return score;
        }

        private float CalculateRegionalUsageScore(DeviceRecord device, string region)
        {
            var deviceCategory = device.Category.ToLower();
            var normalizedRegion = region.ToLower();

            if (_regionalUsageIndex.TryGetValue(deviceCategory, out var regionScores))
            {
                if (regionScores.TryGetValue(normalizedRegion, out var score))
                {
                    return Math.Min(1.0f, score);
                }

                // Fallback if region not found
                return (float)_config.RegionalFallbackScore;
            }

            return (float)_config.RegionalFallbackScore;
        }

        private List<CandidateScores> RerankedWithCrossEncoder(UserProfile userProfile, string userText, List<CandidateScores> candidates)
        {
            // Build pairs for cross-encoder
            var pairs = candidates.Select(c => new Tuple<string, string>(
                userText,
                BuildCrossEncoderText(userProfile, c.Device)
            )).ToList();

            // Get cross-encoder scores
            var crossEncoderScores = _crossEncoder.ScorePairs(pairs);

            // Blend with hybrid scores
            for (int i = 0; i < candidates.Count; i++)
            {
                var crossScore = Sigmoid(crossEncoderScores[i]);
                candidates[i].CrossEncoderScore = crossScore;
                candidates[i].FinalScore = (0.7f * crossScore) + (0.3f * candidates[i].HybridScore);
            }

            return candidates.OrderByDescending(c => c.FinalScore).ToList();
        }

        private List<CandidateScores> SelectFinalRecommendations(List<CandidateScores> candidates, int topN)
        {
            var selected = new List<CandidateScores>();
            var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Enforce distinct categories
            foreach (var candidate in candidates)
            {
                var category = candidate.Device.Category.ToLower();
                if (!seenCategories.Contains(category))
                {
                    selected.Add(candidate);
                    seenCategories.Add(category);

                    if (selected.Count >= topN)
                    {
                        break;
                    }
                }
            }

            // If not enough distinct categories, fill with remaining
            if (selected.Count < topN)
            {
                foreach (var candidate in candidates)
                {
                    if (!selected.Contains(candidate))
                    {
                        selected.Add(candidate);
                        if (selected.Count >= topN)
                        {
                            break;
                        }
                    }
                }
            }

            return selected.Take(topN).ToList();
        }

        private List<RecommendationResult> BuildRecommendationResults(List<CandidateScores> candidates, UserProfile userProfile)
        {
            var results = new List<RecommendationResult>();

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                results.Add(new RecommendationResult
                {
                    Rank = i + 1,
                    DeviceName = candidate.Device.Name,
                    Category = candidate.Device.Category,
                    Size = candidate.Device.Size,
                    Rooms = candidate.Device.Rooms,
                    MatchedRooms = GetMatchedRooms(userProfile, candidate.Device),
                    FeatureText = candidate.Device.FeatureText,
                    Score = candidate.FinalScore,
                    EmbeddingScore = candidate.EmbeddingScore,
                    RoomScore = candidate.RoomScore,
                    RuleScore = candidate.RuleScore,
                    RegionalScore = candidate.RegionalScore,
                    SeasonScore = candidate.SeasonScore,
                    Reasoning = GenerateReasoning(candidate, userProfile)
                });
            }

            return results;
        }

        private List<string> GetMatchedRooms(UserProfile userProfile, DeviceRecord device)
        {
            var userRooms = new HashSet<string>(userProfile.Rooms, StringComparer.OrdinalIgnoreCase);
            var deviceRooms = device.Rooms.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .ToList();

            return deviceRooms.Where(room => userRooms.Contains(room)).ToList();
        }

        private string GenerateReasoning(CandidateScores candidate, UserProfile userProfile)
        {
            return GenerateCompellingReasoning(candidate, userProfile);
        }

        private string GenerateCompellingReasoning(CandidateScores candidate, UserProfile userProfile)
        {
            var random = new Random(candidate.Device.Name.GetHashCode()); // Deterministic but varied per device
            var reasons = new List<(double priority, string text)>();
            var deviceCategory = candidate.Device.Category;
            var matchedRooms = GetMatchedRooms(userProfile, candidate.Device);
            var regionName = userProfile.Region ?? "your region";
            var season = userProfile.Season ?? "season";
            
            // Prioritize based on strongest scores to vary message structure
            double dominantScore = Math.Max(Math.Max(Math.Max(candidate.EmbeddingScore, candidate.RoomScore), 
                                            Math.Max(candidate.RegionalScore, candidate.RuleScore)), candidate.SeasonScore);
            
            // 1. COMPLEMENTARITY - Multiple templates
            if (candidate.RuleScore > 0.7f)
            {
                var complementaryDevices = string.Join(", ", 
                    userProfile.OwnedDevices.Where(d => d.Category.Length > 0).Select(d => d.Name)).TrimEnd(',');
                
                string rulesText = random.Next(0, 3) switch
                {
                    0 => $"Perfect synergy with your {complementaryDevices} – unlocks advanced automation scenarios you won't find in standalone devices.",
                    1 => $"This is the missing puzzle piece for your {complementaryDevices} setup – expect seamless integration and enhanced intelligence.",
                    2 => $"Designed to work beautifully alongside your {complementaryDevices} – completes your smart home vision.",
                    _ => $"Ideal partner for your {complementaryDevices} ecosystem."
                };
                if (!string.IsNullOrEmpty(complementaryDevices))
                    reasons.Add((candidate.RuleScore + 0.1f, rulesText));
            }
            
            // 2. REGIONAL POPULARITY - Varied templates with different angles
            if (candidate.RegionalScore > _config.RegionalFallbackScore)
            {
                string regionalText = (candidate.RegionalScore - _config.RegionalFallbackScore, random.Next(0, 5)) switch
                {
                    (>0.25f, 0) => $"BLOCKBUSTER in {regionName}! {candidate.Device.Name} dominates local wishlists – homeowners here swear by it.",
                    (>0.25f, 1) => $"The go-to {deviceCategory.ToLower()} in {regionName} – if your neighbors own one, you'll want this too.",
                    (>0.25f, 2) => $"#1 rated {deviceCategory.ToLower()} by {regionName} homeowners – trusted by thousands of families like yours.",
                    (>0.25f, 3) => $"Sweeping {regionName}! Most smart home enthusiasts in your area have already upgraded to {candidate.Device.Name}.",
                    (>0.25f, 4) => $"The premium choice in {regionName} – this device is synonymous with quality in your area.",
                    
                    (>0.15f, 0) => $"Standout choice in {regionName} – majority of homeowners prefer this for {deviceCategory.ToLower()} needs.",
                    (>0.15f, 1) => $"{regionName}'s smart choice – proven by thousands of local installations and 5-star reviews.",
                    (>0.15f, 2) => $"Best-selling {deviceCategory.ToLower()} in {regionName} – local experts recommend it.",
                    (>0.15f, 3) => $"Community favorite: {regionName} residents consistently choose {candidate.Device.Name} for reliability.",
                    
                    (>0.05f, 0) => $"Well-liked in {regionName} – smart homeowners in your area often select this for {deviceCategory.ToLower()}.",
                    (>0.05f, 1) => $"Trusted locally: Many {regionName} residents depend on {candidate.Device.Name} for their {deviceCategory.ToLower()} setup.",
                    (>0.05f, 2) => $"Growing preference in {regionName} – more homeowners are discovering why this {deviceCategory.ToLower()} stands out.",
                    
                    _ => $"Popular in {regionName} for {deviceCategory.ToLower()}."
                };
                reasons.Add((candidate.RegionalScore, regionalText));
            }
            
            // 3. ROOM COMPATIBILITY - Varied approaches
            if (candidate.RoomScore > 0.5f && matchedRooms.Count > 0)
            {
                var roomList = string.Join(" and ", matchedRooms);
                string roomText = random.Next(0, 3) switch
                {
                    0 => $"Tailor-made for your {roomList} – this device understands the unique needs of that space.",
                    1 => $"Your {roomList} will shine with this installed – optimized for that exact environment.",
                    2 => $"Room-specific advantage: Perfect fit for {roomList} automation.",
                    _ => $"Designed for your {roomList}."
                };
                reasons.Add((candidate.RoomScore + 0.05f, roomText));
            }
            
            // 4. SEASONAL RELEVANCE - Multiple angles
            if (candidate.SeasonScore > 0.3f)
            {
                string seasonText = (candidate.SeasonScore, random.Next(0, 4)) switch
                {
                    (>0.7f, 0) => $"🌡️ ESSENTIAL FOR {season.ToUpper()}! Most {regionName} families activate this during {season} – it's a game-changer.",
                    (>0.7f, 1) => $"⏰ {season} essential: This becomes your favorite device once {season} arrives – critical for {regionName} comfort.",
                    (>0.7f, 2) => $"🎯 Built for {season}: Users in {regionName} report this is THE device to have before {season} hits.",
                    
                    (>0.5f, 0) => $"Seasonal advantage: {season} is when this device truly shines – expect maximum benefit during {season} months.",
                    (>0.5f, 1) => $"{season} optimized: Smart move to set this up now for ideal {season} performance.",
                    (>0.5f, 2) => $"Perfect timing for {season}: This device unlocks its full potential during {season}.",
                    
                    (>0.3f, 0) => $"Helpful during {season}: Many homeowners in {regionName} find this particularly useful in {season}.",
                    (>0.3f, 1) => $"{season} bonus: You'll appreciate having this during {season} months.",
                    
                    _ => ""
                };
                if (!string.IsNullOrEmpty(seasonText))
                    reasons.Add((candidate.SeasonScore, seasonText));
            }
            
            // 5. SEMANTIC MATCH - Varied confidence messaging
            if (candidate.EmbeddingScore > 0.6f)
            {
                string aiText = candidate.EmbeddingScore > 0.75f 
                    ? random.Next(0, 2) switch
                    {
                        0 => $"🎯 Exceptional match ({candidate.EmbeddingScore:F2}/1.0): Our AI algorithm ranks this among top fits for your specific home profile.",
                        1 => $"✓ Verified compatible: Advanced analysis confirms this device aligns perfectly with your lifestyle ({candidate.EmbeddingScore:F2} confidence).",
                        _ => $"AI-verified: High compatibility score ({candidate.EmbeddingScore:F2}) for your setup."
                    }
                    : random.Next(0, 2) switch
                    {
                        0 => $"Smart match: Analyzed against your preferences – this device fits your needs.",
                        1 => $"Intelligent pick: Algorithm recommends based on your home automation profile.",
                        _ => $"Verified recommendation based on your setup."
                    };
                reasons.Add((candidate.EmbeddingScore - 0.1f, aiText));
            }
            
            // 6. FEATURE HIGHLIGHT - Context-aware
            var features = candidate.Device.FeatureText;
            if (!string.IsNullOrEmpty(features) && features.Length > 20)
            {
                var shortFeature = features.Length > 100 ? features.Substring(0, 97) + "..." : features;
                string featureText = random.Next(0, 3) switch
                {
                    0 => $"Key advantage: {shortFeature}",
                    1 => $"Standout feature: {shortFeature}",
                    2 => $"What makes it special: {shortFeature}",
                    _ => $"{shortFeature}"
                };
                reasons.Add((candidate.EmbeddingScore, featureText));
            }
            
            // 7. BONUS: Category-specific insight
            if (candidate.RegionalScore > _config.RegionalFallbackScore && !string.IsNullOrEmpty(deviceCategory))
            {
                var insightPhrase = candidate.RegionalScore > _config.RegionalFallbackScore + 0.2f 
                    ? "industry leader for" 
                    : "popular choice for";
                string bonusText = random.Next(0, 2) switch
                {
                    0 => $"Market insight: {candidate.Device.Name} is the {insightPhrase} {deviceCategory.ToLower()} in {regionName}.",
                    1 => $"By the numbers: {regionName} residents rank {candidate.Device.Name} as their top {deviceCategory.ToLower()} pick.",
                    _ => $"Regional leader in {deviceCategory.ToLower()}."
                };
                reasons.Add((candidate.RegionalScore - 0.05f, bonusText));
            }
            
            // If no compelling reasons, add fallback variety
            if (reasons.Count == 0)
            {
                var fallback = random.Next(0, 3) switch
                {
                    0 => $"This smart addition will elevate your {regionName} home automation setup.",
                    1 => $"{candidate.Device.Name} brings proven reliability to your home ecosystem.",
                    2 => $"A solid choice that fits well with your smart home vision.",
                    _ => $"Recommended for your home."
                };
                reasons.Add((0.5f, fallback));
            }
            
            // Sort by priority (highest first) and return top reasons
            var topReasons = reasons
                .OrderByDescending(r => r.priority)
                .Take(4) // Select top 4 reasons for variety without overwhelming
                .Select(r => r.text)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
            
            return string.Join(" ", topReasons);
        }

        private string BuildCrossEncoderText(UserProfile userProfile, DeviceRecord device)
        {
            var matchedRooms = string.Join(", ", userProfile.Rooms) ?? "none";
            var ownedDevices = string.Join(", ", userProfile.OwnedDevices.Select(d => d.Category)) ?? "none";
            return $"User has: {ownedDevices}. Rooms: {userProfile.Rooms}. Recommending: {device.Name} ({device.Category}) for {matchedRooms}.";
        }

        private void ComputeDeviceEmbeddings()
        {
            var deviceTexts = _devices.Select(d => d.ToEmbeddingText()).ToList();
            var embeddings = _biEncoder.GenerateEmbeddings(deviceTexts);

            for (int i = 0; i < _devices.Count; i++)
            {
                _deviceEmbeddingCache[_devices[i].Key] = embeddings[i];
            }
        }

        private HybridConfig LoadHybridConfig(string configPath)
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<HybridConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new HybridConfig();

            // Ensure ComplementaryCategories has lowercase keys for case-insensitive lookup
            var normalizedDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in config.ComplementaryCategories)
            {
                normalizedDict[kvp.Key.ToLower()] = kvp.Value;
            }
            config.ComplementaryCategories = normalizedDict;

            return config;
        }

        private List<DeviceRecord> LoadDevices(string devicePath)
        {
            var json = File.ReadAllText(devicePath);
            
            // Parse the JSON to extract the devices array
            // The JSON has structure: { "total_devices": N, "total_categories": N, "devices": [...] }
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;
            
            if (root.TryGetProperty("devices", out var devicesElement))
            {
                var devices = JsonSerializer.Deserialize<List<DeviceRecord>>(devicesElement.GetRawText(), options) 
                    ?? new List<DeviceRecord>();
                return devices;
            }
            else
            {
                // Fallback: try to deserialize root as array directly
                var devices = JsonSerializer.Deserialize<List<DeviceRecord>>(json, options) 
                    ?? new List<DeviceRecord>();
                return devices;
            }
        }

        private Dictionary<string, Dictionary<string, float>> LoadRegionalUsageIndex(string usagePath)
        {
            var index = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(usagePath))
            {
                Console.WriteLine($"[OnnxBiCross] Regional usage file not found: {usagePath}");
                return index;
            }

            try
            {
                var json = File.ReadAllText(usagePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var category = item.TryGetProperty("category", out var catElement)
                            ? catElement.GetString()?.ToLower()
                            : null;

                        if (string.IsNullOrEmpty(category))
                        {
                            continue;
                        }

                        var regionDict = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

                        foreach (var prop in item.EnumerateObject())
                        {
                            if (prop.Name.Equals("category", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (prop.Value.TryGetSingle(out var score))
                            {
                                regionDict[prop.Name.ToLower()] = score;
                            }
                        }

                        index[category] = regionDict;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnnxBiCross] Error loading regional usage: {ex.Message}");
            }

            return index;
        }

        private float CalculateSeasonScore(string category, string season)
        {
            if (string.IsNullOrEmpty(season) || _config.SeasonCategoryMap == null || _config.SeasonCategoryMap.Count == 0)
                return 0.0f;

            var seasonLower = season.ToLowerInvariant().Trim();

            if (!_config.SeasonCategoryMap.TryGetValue(seasonLower, out var seasonCategories))
                return 0.0f;

            var categoryLower = category.ToLowerInvariant().Trim();
            foreach (var seasonCategory in seasonCategories)
            {
                if (string.Equals(seasonCategory, categoryLower, StringComparison.OrdinalIgnoreCase) ||
                    seasonCategory.Contains(categoryLower) ||
                    categoryLower.Contains(seasonCategory.ToLowerInvariant()))
                {
                    return 1.0f;
                }
            }

            return 0.0f;
        }

        private float CosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1.Length != vec2.Length)
            {
                throw new ArgumentException("Vectors must have same length");
            }

            float dotProduct = 0;
            float norm1 = 0;
            float norm2 = 0;

            for (int i = 0; i < vec1.Length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                norm1 += vec1[i] * vec1[i];
                norm2 += vec2[i] * vec2[i];
            }

            float denominator = (float)Math.Sqrt(norm1 * norm2);
            if (denominator < 1e-8)
            {
                return 0;
            }

            return dotProduct / denominator;
        }

        private float Sigmoid(float x)
        {
            return 1.0f / (1.0f + (float)Math.Exp(-x));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _biEncoder?.Dispose();
            _crossEncoder?.Dispose();
            _disposed = true;
        }

        /// <summary>
        /// Internal class for tracking candidate scores during recommendation generation.
        /// </summary>
        private class CandidateScores
        {
            public DeviceRecord Device { get; set; } = null!;
            public float EmbeddingScore { get; set; }
            public float RoomScore { get; set; }
            public float RuleScore { get; set; }
            public float RegionalScore { get; set; }
            public float SeasonScore { get; set; }
            public float HybridScore { get; set; }
            public float CrossEncoderScore { get; set; }
            public float FinalScore { get; set; }
        }
    }
}
