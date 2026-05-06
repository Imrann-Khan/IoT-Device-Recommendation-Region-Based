using IoTDeviceSuggestionWInUI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Hybrid recommendation service using Sentence Transformer retrieval with 5-component hybrid scoring.
    /// 
    /// Scoring components (matching Python implementation):
    /// - Embedding Similarity (0.67): Semantic match between user profile and device
    /// - Room Overlap (0.10): Device fits user's room(s)
    /// - Complementary Rule (0.10): Device complements owned devices
    /// - Regional Usage (0.10): Device popularity in user's region
    /// - Season (0.03): Device relevance to current season
    /// </summary>
    [Obsolete("RecommendationService is deprecated. Use OnnxBiCrossRecommendationService instead.")]
    public class RecommendationService : IRecommendationService
    {
        private readonly IEmbeddingService? _embeddingService;
        private readonly List<DeviceRecord> _devices;
        private readonly HybridConfig _config;
        private float[][]? _deviceEmbeddings;
        private readonly Dictionary<string, HashSet<string>> _complementaryRules;
        private readonly RegionalUsageService? _regionalUsageService;
        private bool _embeddingsComputed = false;
        private readonly object _lock = new();

        public RecommendationService(
            IEmbeddingService? embeddingService,
            List<DeviceRecord> devices,
            HybridConfig config,
            RegionalUsageService? regionalUsageService = null)
        {
            _embeddingService = embeddingService;
            _devices = devices;
            _config = config;
            _regionalUsageService = regionalUsageService;

            // Build complementary rules lookup
            _complementaryRules = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in config.ComplementaryCategories)
            {
                _complementaryRules[rule.Key] = new HashSet<string>(rule.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Get device recommendations for a user profile using two-stage process.
        /// </summary>
        public async Task<List<RecommendationResult>> GetRecommendationsAsync(UserProfile userProfile, int topN = 8)
        {
            return await Task.Run(() =>
            {
                Console.WriteLine("");
                Console.WriteLine("================================================================================");
                Console.WriteLine("                    IoT DEVICE RECOMMENDATION - TWO STAGE                      ");
                Console.WriteLine("================================================================================");
                Console.WriteLine("");

                var startTime = DateTime.Now;

                // ========== STAGE 1: RETRIEVAL ==========
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  STAGE 1: SENTENCE TRANSFORMER RETRIEVAL                                     ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine("");

                // Step 1: Log user profile
                Console.WriteLine("=== USER PROFILE ===");
                Console.WriteLine($"  User ID: {userProfile.UserId}");
                Console.WriteLine($"  Selected Rooms: {string.Join(", ", userProfile.Rooms)}");
                Console.WriteLine($"  Room Functions: {string.Join(", ", userProfile.RoomFunctionTags)}");
                Console.WriteLine($"  Owned Devices: {userProfile.OwnedDevices.Count}");
                foreach (var device in userProfile.OwnedDevices)
                {
                    Console.WriteLine($"    - {device.Name} (Category: {device.Category})");
                }
                Console.WriteLine("");

                // Step 2: Compute embeddings
                EnsureEmbeddingsComputed();

                // Step 3: Compute user embedding
                float[]? userEmbedding = null;
                if (_embeddingService != null && _deviceEmbeddings != null)
                {
                    Console.WriteLine("[STAGE1] Computing user embedding from profile...");
                    var userText = userProfile.ToEmbeddingText();
                    Console.WriteLine($"[STAGE1] User text: \"{TruncateText(userText, 100)}...\"");
                    userEmbedding = _embeddingService.GenerateEmbedding(userText);
                    Console.WriteLine($"[STAGE1] User embedding computed (dimension: {userEmbedding.Length})");
                }
                Console.WriteLine("");

                // Step 4: Get owned categories
                var ownedCategories = userProfile.OwnedDevices
                    .Select(d => d.Category.ToLower())
                    .ToHashSet();

                Console.WriteLine($"[STAGE1] Owned categories: {string.Join(", ", ownedCategories)}");
                Console.WriteLine("");

                // Step 5: Score all devices with 5-component hybrid scoring
                Console.WriteLine($"[STAGE1] Scoring {_devices.Count} devices with 5-component hybrid scoring...");
                Console.WriteLine($"[STAGE1] User region: {userProfile.Region}, Season: {userProfile.Season}");
                var candidates = new List<RetrievalCandidate>();

                for (int i = 0; i < _devices.Count; i++)
                {
                    var device = _devices[i];

                    // Skip owned devices (same category)
                    if (ownedCategories.Contains(device.Category.ToLower()))
                        continue;

                    // Calculate all 5 scores
                    double embeddingScore = 0;
                    if (userEmbedding != null && _deviceEmbeddings != null)
                    {
                        embeddingScore = CosineSimilarity(userEmbedding, _deviceEmbeddings[i]);
                    }

                    var matchedRooms = GetMatchedRooms(userProfile.Rooms, device.Rooms);
                    var roomScore = matchedRooms.Count > 0 ? 1.0 : 0.0;
                    var ruleScore = GetRuleScore(ownedCategories, device.Category);
                    var relatedOwned = GetRelatedOwnedCategories(ownedCategories, device.Category);
                    
                    // NEW: Regional usage score
                    var regionalScore = GetRegionalScore(device.Category, userProfile.Region);
                    
                    // NEW: Season score
                    var seasonScore = GetSeasonScore(device.Category, userProfile.Season);

                    // Calculate final hybrid score with all 5 components
                    var finalScore =
                        embeddingScore * _config.Weights.EmbeddingSimilarity +
                        roomScore * _config.Weights.RoomOverlap +
                        ruleScore * _config.Weights.ComplementaryRule +
                        regionalScore * _config.Weights.RegionalUsage +
                        seasonScore * _config.Weights.Season;

                    candidates.Add(new RetrievalCandidate
                    {
                        CandidateId = $"C{i + 1:D02}",
                        DeviceIndex = i,
                        Device = device,
                        EmbeddingScore = embeddingScore,
                        RoomScore = roomScore,
                        RuleScore = ruleScore,
                        RegionalScore = regionalScore,
                        SeasonScore = seasonScore,
                        Score = finalScore,
                        MatchedRooms = matchedRooms,
                        RelatedOwned = relatedOwned
                    });
                }

                Console.WriteLine($"[STAGE1] Scored {candidates.Count} candidate devices");
                Console.WriteLine("");

                var fullRankedCandidates = candidates
                    .OrderByDescending(c => c.Score)
                    .ToList();

                // Step 6: Rank and get top N for LLM
                var retrievalTopN = Math.Max(_config.RetrievalTopN, topN * 3);
                var topCandidates = fullRankedCandidates
                    .Take(retrievalTopN)
                    .ToList();

                Console.WriteLine($"[STAGE1] Top {retrievalTopN} candidates for LLM reasoning:");
                for (int i = 0; i < Math.Min(10, topCandidates.Count); i++)
                {
                    var c = topCandidates[i];
                    Console.WriteLine($"  #{i + 1}: {c.Device.Name}");
                    Console.WriteLine($"       Category: {c.Device.Category}");
                    Console.WriteLine($"       Score: {c.Score:F4} (Emb: {c.EmbeddingScore:F3}, Room: {c.RoomScore:F2}, Rule: {c.RuleScore:F2})");
                }
                Console.WriteLine("");

                // ========== STAGE 2: HYBRID RANKING ==========
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  STAGE 2: HYBRID RANKING (LLM REMOVED)                                       ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine("");

                Console.WriteLine("[STAGE2] Using hybrid ranking (LLM removed from application)");
                var results = FallbackRanking(topCandidates, userProfile, topN);

                // Keep category uniqueness as primary behavior, then backfill to guarantee minimum count.
                results = EnsureMinimumResults(results, fullRankedCandidates, userProfile, topN);
                Console.WriteLine("");

                // Show final results
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  FINAL RECOMMENDATIONS                                                       ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.WriteLine("");

                foreach (var r in results)
                {
                    Console.WriteLine($"  RANK #{r.Rank}: {r.DeviceName}");
                    Console.WriteLine($"  ────────────────────────────────────────");
                    Console.WriteLine($"  Category:    {r.Category}");
                    Console.WriteLine($"  Size:        {r.Size}");
                    Console.WriteLine($"  Rooms:       {r.Rooms}");
                    Console.WriteLine($"  Scores:");
                    Console.WriteLine($"    Embedding: {r.EmbeddingScore:F4} × {_config.Weights.EmbeddingSimilarity} = {r.EmbeddingScore * _config.Weights.EmbeddingSimilarity:F4}");
                    Console.WriteLine($"    Room:      {r.RoomScore:F2} × {_config.Weights.RoomOverlap} = {r.RoomScore * _config.Weights.RoomOverlap:F4}");
                    Console.WriteLine($"    Rule:      {r.RuleScore:F2} × {_config.Weights.ComplementaryRule} = {r.RuleScore * _config.Weights.ComplementaryRule:F4}");
                    Console.WriteLine($"    Regional:  {r.RegionalScore:F4} × {_config.Weights.RegionalUsage} = {r.RegionalScore * _config.Weights.RegionalUsage:F4}");
                    Console.WriteLine($"    Season:    {r.SeasonScore:F2} × {_config.Weights.Season} = {r.SeasonScore * _config.Weights.Season:F4}");
                    Console.WriteLine($"    FINAL:     {r.Score:F4}");
                    Console.WriteLine($"  Reasoning: {r.Reasoning}");
                    Console.WriteLine("");
                }

                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                Console.WriteLine("================================================================================");
                Console.WriteLine($"  RECOMMENDATION COMPLETE in {elapsed:F0}ms");
                Console.WriteLine($"  Returned {results.Count} recommendations");
                Console.WriteLine("================================================================================");
                Console.WriteLine("");

                return results;
            });
        }

        /// <summary>
        /// Get device recommendations for a specific room.
        /// </summary>
        public async Task<List<RecommendationResult>> GetRecommendationsForRoomAsync(UserProfile userProfile, string room, int topN = 8)
        {
            // Create a filtered profile for the specific room
            var roomProfile = new UserProfile
            {
                UserId = userProfile.UserId,
                Rooms = new List<string> { room },
                RoomFunctionTags = GetRoomFunctionTags(room),
                OwnedDevices = userProfile.OwnedDevices
            };

            return await GetRecommendationsAsync(roomProfile, topN);
        }

        /// <summary>
        /// Pre-compute embeddings for all devices. Call this during initialization.
        /// </summary>
        public void PrecomputeEmbeddings()
        {
            if (_embeddingService == null)
            {
                Console.WriteLine("[EMBEDDING] No embedding service - skipping pre-computation");
                return;
            }

            if (_embeddingsComputed)
            {
                Console.WriteLine("[EMBEDDING] Embeddings already computed");
                return;
            }

            EnsureEmbeddingsComputed();
        }

        /// <summary>
        /// Compute embeddings lazily on first use.
        /// </summary>
        private void EnsureEmbeddingsComputed()
        {
            if (_embeddingsComputed || _embeddingService == null)
            {
                if (_embeddingsComputed)
                {
                    Console.WriteLine("[EMBEDDING] Device embeddings already computed (cached)");
                }
                else
                {
                    Console.WriteLine("[EMBEDDING] No embedding service - skipping embedding computation");
                }
                return;
            }

            lock (_lock)
            {
                if (_embeddingsComputed)
                    return;

                Console.WriteLine($"[EMBEDDING] Computing embeddings for {_devices.Count} devices...");
                var startTime = DateTime.Now;
                _deviceEmbeddings = _embeddingService.GenerateEmbeddings(
                    _devices.Select(d => d.ToEmbeddingText())
                );
                _embeddingsComputed = true;
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                Console.WriteLine($"[EMBEDDING] Computed {_deviceEmbeddings.Length} device embeddings in {elapsed:F2} seconds");
            }
        }

        /// <summary>
        /// Build the LLM prompt with user context and candidates.
        /// </summary>
        private string BuildLLMPrompt(UserProfile userProfile, List<RetrievalCandidate> candidates, int topN)
        {
            var userRooms = string.Join(", ", userProfile.Rooms) ?? "none";
            var roomFunctions = userProfile.RoomFunctionTags.Count > 0 
                ? string.Join(", ", userProfile.RoomFunctionTags) 
                : "general";

            var connectedDevices = userProfile.OwnedDevices
                .Select(d => $"{d.Name} ({d.Category})")
                .ToList();
            var connectedText = connectedDevices.Count > 0 
                ? string.Join("; ", connectedDevices) 
                : "none";

            var candidateBlocks = candidates.Select((c, i) =>
            {
                var matchedRooms = c.MatchedRooms.Count > 0 
                    ? string.Join(", ", c.MatchedRooms) 
                    : "none";
                var featureSummary = TruncateText(c.Device.FeatureText ?? "", 150);
                var ruleEvidence = c.RelatedOwned.Count > 0
                    ? "Complements: " + string.Join(", ", c.RelatedOwned)
                    : "No explicit category rule match";

                return $"- ID: {c.CandidateId} | Name: {c.Device.Name} | Category: {c.Device.Category} | " +
                       $"Size: {c.Device.Size ?? "none"} | Rooms: {c.Device.Rooms ?? "none"} | " +
                       $"Matched rooms: {matchedRooms} | Rule evidence: {ruleEvidence} | " +
                       $"Score: {c.Score:F3} | Feature: {featureSummary}";
            }).ToList();

            var prompt = $@"You are a smart home recommendation assistant.

USER CONTEXT:
- User rooms: {userRooms}
- Room function tags: {roomFunctions}
- Connected devices: {connectedText}
- Return only devices from the candidate list below.

TASK:
1. Select up to {topN} devices that best fit the user's room and connected devices.
2. Prefer devices that complement the current setup and make practical sense in the room.
3. Use this ranking priority: room match > complementary fit > role-balance fit > candidate score.
4. If two devices are very similar, keep only the stronger one.
5. Prefer category diversity in final picks unless one category is clearly dominant.
6. Do not invent devices or categories.
7. Return strict JSON only.

SELECTION RULES:
- Select only from candidate IDs provided below.
- Output unique candidate IDs only.
- Keep recommendations concise and practical for the stated rooms.

REASONING RULES:
- Write 2 to 3 sentences per item.
- Mention at least one user room or matched room.
- Mention how it complements at least one owned-device category.
- Mention one concrete benefit from the candidate feature context when possible.
- Avoid reusing the same sentence template across items.

CANDIDATES:
{string.Join("\n", candidateBlocks)}

RESPONSE FORMAT:
{{
  ""overall_reasoning"": ""Short summary of the recommendation strategy."",
  ""recommendations"": [
    {{
      ""candidate_id"": ""C01"",
      ""recommended_device_name"": ""Exact device name from the list"",
      ""category"": ""Exact category from the list"",
      ""reasoning"": ""Two concise sentences explaining why it fits this room and setup.""
    }}
  ]
}}";

            return prompt;
        }

        /// <summary>
        /// Parse LLM response and build recommendation results.
        /// </summary>
        private List<RecommendationResult> ParseLLMResponse(
            string llmResponse, 
            List<RetrievalCandidate> candidates, 
            UserProfile userProfile,
            int topN)
        {
            var results = new List<RecommendationResult>();
            var candidateLookup = candidates.ToDictionary(c => c.CandidateId, c => c);
            var selectedIds = new HashSet<string>();
            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownedCategories = userProfile.OwnedDevices.Select(d => d.Category.ToLower()).ToHashSet();

            try
            {
                // Extract JSON from response
                var jsonStart = llmResponse.IndexOf('{');
                var jsonEnd = llmResponse.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonContent = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var response = JsonSerializer.Deserialize<LLMResponse>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (response?.Recommendations != null)
                    {
                        var rank = 1;
                        foreach (var rec in response.Recommendations)
                        {
                            if (string.IsNullOrEmpty(rec.CandidateId) || !candidateLookup.TryGetValue(rec.CandidateId, out var candidate))
                                continue;

                            if (selectedIds.Contains(rec.CandidateId))
                                continue;

                            var candidateCategoryKey = NormalizeCategoryKey(candidate.Device.Category);
                            if (selectedCategories.Contains(candidateCategoryKey))
                                continue;

                            results.Add(new RecommendationResult
                            {
                                Rank = rank++,
                                DeviceName = candidate.Device.Name,
                                Category = candidate.Device.Category,
                                Size = candidate.Device.Size,
                                Rooms = candidate.Device.Rooms,
                                MatchedRooms = candidate.MatchedRooms,
                                FeatureText = candidate.Device.FeatureText,
                                Score = candidate.Score,
                                EmbeddingScore = candidate.EmbeddingScore,
                                RoomScore = candidate.RoomScore,
                                RuleScore = candidate.RuleScore,
                                RegionalScore = candidate.RegionalScore,
                                SeasonScore = candidate.SeasonScore,
                                Reasoning = NormalizeReasoning(rec.Reasoning, candidate, userProfile, ownedCategories)
                            });

                            selectedIds.Add(rec.CandidateId);
                            selectedCategories.Add(candidateCategoryKey);

                            if (results.Count >= topN)
                                break;
                        }

                        Console.WriteLine($"[STAGE2] LLM overall reasoning: {response.OverallReasoning}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STAGE2] Error parsing LLM response: {ex.Message}");
            }

            // Fill remaining slots with fallback if needed
            if (results.Count < topN)
            {
                foreach (var candidate in candidates)
                {
                    if (selectedIds.Contains(candidate.CandidateId))
                        continue;

                    var candidateCategoryKey = NormalizeCategoryKey(candidate.Device.Category);
                    if (selectedCategories.Contains(candidateCategoryKey))
                        continue;

                    results.Add(new RecommendationResult
                    {
                        Rank = results.Count + 1,
                        DeviceName = candidate.Device.Name,
                        Category = candidate.Device.Category,
                        Size = candidate.Device.Size,
                        Rooms = candidate.Device.Rooms,
                        MatchedRooms = candidate.MatchedRooms,
                        FeatureText = candidate.Device.FeatureText,
                        Score = candidate.Score,
                        EmbeddingScore = candidate.EmbeddingScore,
                        RoomScore = candidate.RoomScore,
                        RuleScore = candidate.RuleScore,
                        RegionalScore = candidate.RegionalScore,
                        SeasonScore = candidate.SeasonScore,
                        Reasoning = BuildReasoning(candidate.Device, candidate.MatchedRooms, ownedCategories, candidate.RegionalScore, candidate.SeasonScore, userProfile)
                    });

                    selectedIds.Add(candidate.CandidateId);
                    selectedCategories.Add(candidateCategoryKey);

                    if (results.Count >= topN)
                        break;
                }
            }

            return results;
        }

        /// <summary>
        /// Fallback ranking when LLM is not available.
        /// </summary>
        private List<RecommendationResult> FallbackRanking(List<RetrievalCandidate> candidates, UserProfile userProfile, int topN)
        {
            var ownedCategories = userProfile.OwnedDevices.Select(d => d.Category.ToLower()).ToHashSet();
            var results = new List<RecommendationResult>();
            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First pass: prefer category diversity
            foreach (var candidate in candidates)
            {
                var candidateCategoryKey = NormalizeCategoryKey(candidate.Device.Category);
                if (selectedCategories.Contains(candidateCategoryKey))
                    continue;

                results.Add(new RecommendationResult
                {
                    Rank = results.Count + 1,
                    DeviceName = candidate.Device.Name,
                    Category = candidate.Device.Category,
                    Size = candidate.Device.Size,
                    Rooms = candidate.Device.Rooms,
                    MatchedRooms = candidate.MatchedRooms,
                    FeatureText = candidate.Device.FeatureText,
                    Score = candidate.Score,
                    EmbeddingScore = candidate.EmbeddingScore,
                    RoomScore = candidate.RoomScore,
                    RuleScore = candidate.RuleScore,
                    RegionalScore = candidate.RegionalScore,
                    SeasonScore = candidate.SeasonScore,
                    Reasoning = BuildReasoning(candidate.Device, candidate.MatchedRooms, ownedCategories, candidate.RegionalScore, candidate.SeasonScore, userProfile)
                });

                selectedCategories.Add(candidateCategoryKey);

                if (results.Count >= topN)
                    break;
            }

            return results;
        }

        private List<RecommendationResult> EnsureMinimumResults(
            List<RecommendationResult> results,
            List<RetrievalCandidate> candidates,
            UserProfile userProfile,
            int topN)
        {
            if (results.Count >= topN)
            {
                return results;
            }

            var ownedCategories = userProfile.OwnedDevices.Select(d => d.Category.ToLower()).ToHashSet();
            var selectedNames = new HashSet<string>(results.Select(r => r.DeviceName), StringComparer.OrdinalIgnoreCase);
            var selectedCategories = new HashSet<string>(
                results.Select(r => NormalizeCategoryKey(r.Category)),
                StringComparer.OrdinalIgnoreCase);

            // Fill only with unseen categories from the full ranked pool.
            foreach (var candidate in candidates)
            {
                if (results.Count >= topN)
                    break;

                if (selectedNames.Contains(candidate.Device.Name))
                    continue;

                var categoryKey = NormalizeCategoryKey(candidate.Device.Category);
                if (selectedCategories.Contains(categoryKey))
                    continue;

                results.Add(new RecommendationResult
                {
                    Rank = results.Count + 1,
                    DeviceName = candidate.Device.Name,
                    Category = candidate.Device.Category,
                    Size = candidate.Device.Size,
                    Rooms = candidate.Device.Rooms,
                    MatchedRooms = candidate.MatchedRooms,
                    FeatureText = candidate.Device.FeatureText,
                    Score = candidate.Score,
                    EmbeddingScore = candidate.EmbeddingScore,
                    RoomScore = candidate.RoomScore,
                    RuleScore = candidate.RuleScore,
                    RegionalScore = candidate.RegionalScore,
                    SeasonScore = candidate.SeasonScore,
                    Reasoning = BuildReasoning(candidate.Device, candidate.MatchedRooms, ownedCategories, candidate.RegionalScore, candidate.SeasonScore, userProfile)
                });

                selectedNames.Add(candidate.Device.Name);
                selectedCategories.Add(categoryKey);
            }

            if (results.Count < topN)
            {
                Console.WriteLine($"[STAGE2] Only {results.Count} unique-category recommendations available (requested {topN})");
            }

            return results;
        }

        private List<RetrievalCandidate> SelectPromptCandidatesWithinBudget(
            UserProfile userProfile,
            List<RetrievalCandidate> rankedCandidates,
            int topN,
            int maxPromptChars)
        {
            if (rankedCandidates.Count <= topN)
            {
                return rankedCandidates;
            }

            var takeCount = rankedCandidates.Count;

            while (takeCount >= topN)
            {
                var attempt = rankedCandidates.Take(takeCount).ToList();
                var prompt = BuildLLMPrompt(userProfile, attempt, topN);

                if (prompt.Length <= maxPromptChars)
                {
                    if (takeCount < rankedCandidates.Count)
                    {
                        Console.WriteLine($"[STAGE2] Prompt exceeded safe budget; reduced candidates from {rankedCandidates.Count} to {takeCount}");
                    }

                    return attempt;
                }

                // Shrink candidate context progressively while keeping at least topN options.
                var reduceBy = Math.Max(1, (int)Math.Ceiling(takeCount * 0.15));
                takeCount -= reduceBy;
            }

            var minimum = rankedCandidates.Take(topN).ToList();
            Console.WriteLine($"[STAGE2] Prompt remained large; using minimum candidate set of {minimum.Count}");
            return minimum;
        }

        private static string NormalizeCategoryKey(string? category)
        {
            return (category ?? string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Normalize reasoning text from LLM.
        /// </summary>
        private string NormalizeReasoning(string? reasoning, RetrievalCandidate candidate, UserProfile userProfile, HashSet<string> ownedCategories, double regionalScore = 0, double seasonScore = 0)
        {
            if (string.IsNullOrWhiteSpace(reasoning))
                return BuildReasoning(candidate.Device, candidate.MatchedRooms, ownedCategories, regionalScore, seasonScore, userProfile);

            var text = reasoning.Trim();

            // Check if reasoning mentions device or category
            var mentionsDevice = text.Contains(candidate.Device.Name, StringComparison.OrdinalIgnoreCase);
            var mentionsCategory = text.Contains(candidate.Device.Category, StringComparison.OrdinalIgnoreCase);

            if (!mentionsDevice && !mentionsCategory && text.Length < 40)
                return BuildReasoning(candidate.Device, candidate.MatchedRooms, ownedCategories, regionalScore, seasonScore, userProfile);

            return text;
        }

        /// <summary>
        /// Calculate cosine similarity between two vectors.
        /// </summary>
        private double CosineSimilarity(float[] a, float[] b)
        {
            double dotProduct = 0;
            double normA = 0;
            double normB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        /// <summary>
        /// Get matched rooms between user rooms and device rooms.
        /// </summary>
        private List<string> GetMatchedRooms(List<string> userRooms, string deviceRooms)
        {
            if (string.IsNullOrEmpty(deviceRooms))
                return new List<string>();

            var deviceRoomSet = deviceRooms
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim().ToLower())
                .ToHashSet();

            return userRooms
                .Where(r => deviceRoomSet.Contains(r.ToLower()))
                .ToList();
        }

        /// <summary>
        /// Get rule score based on complementary categories.
        /// </summary>
        private double GetRuleScore(HashSet<string> ownedCategories, string candidateCategory)
        {
            if (!_complementaryRules.Any())
                return 0;

            foreach (var owned in ownedCategories)
            {
                if (_complementaryRules.TryGetValue(owned, out var complementary))
                {
                    if (complementary.Contains(candidateCategory))
                        return 1.0;
                }
            }

            return 0;
        }

        /// <summary>
        /// Get related owned categories for a candidate.
        /// </summary>
        private List<string> GetRelatedOwnedCategories(HashSet<string> ownedCategories, string candidateCategory)
        {
            var related = new List<string>();

            foreach (var owned in ownedCategories)
            {
                if (_complementaryRules.TryGetValue(owned, out var complementary))
                {
                    if (complementary.Contains(candidateCategory))
                        related.Add(owned);
                }
            }

            return related;
        }

        /// <summary>
        /// Get room function tags from config.
        /// </summary>
        private List<string> GetRoomFunctionTags(string room)
        {
            if (_config.RoomFunctionTags.TryGetValue(room.ToLower(), out var tags))
                return tags;

            return new List<string> { "general" };
        }

        /// <summary>
        /// Build reasoning text for a recommendation.
        /// </summary>
        private string BuildReasoning(DeviceRecord device, List<string> matchedRooms, HashSet<string> ownedCategories, double regionalScore = 0, double seasonScore = 0, UserProfile? userProfile = null)
        {
            var parts = new List<string>();

            // 1. COMPLEMENTARY SETUP (primary focus)
            var complementaryDevice = FindComplementaryDevice(ownedCategories, device.Category);
            if (!string.IsNullOrEmpty(complementaryDevice))
            {
                parts.Add($"Perfect complement to your {complementaryDevice} – enhances your smart home ecosystem");
            }
            
            // 2. ROOM MATCH
            if (matchedRooms.Count > 0)
            {
                parts.Add($"Specifically designed for your {string.Join(", ", matchedRooms)} – boosts comfort and control");
            }
            else
            {
                parts.Add("Seamless fit into your smart home setup");
            }
            
            // 3. REGIONAL POPULARITY (trust signal)
            if (userProfile != null && regionalScore > _config.RegionalFallbackScore + 0.15)
            {
                parts.Add($"🔥 Top choice in {userProfile.Region}: {device.Name} is trending for {device.Category.ToLower()} users in your area");
            }
            else if (userProfile != null && regionalScore > _config.RegionalFallbackScore)
            {
                parts.Add($"Popular in {userProfile.Region} for {device.Category.ToLower()} solutions");
            }
            
            // 4. SEASONAL VALUE
            if (seasonScore > 0.5 && userProfile != null)
            {
                parts.Add($"Especially valuable in {userProfile.Season} – maximize seasonal comfort");
            }
            
            // 5. KEY FEATURE HIGHLIGHT
            var benefit = ExtractFeatureBenefit(device.FeatureText);
            if (!string.IsNullOrEmpty(benefit))
            {
                parts.Add($"Key feature: {benefit}");
            }

            // Fallback
            if (parts.Count == 0)
            {
                parts.Add($"{device.Name} is a smart addition to elevate your home");
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Find complementary device from owned categories.
        /// </summary>
        private string? FindComplementaryDevice(HashSet<string> ownedCategories, string candidateCategory)
        {
            foreach (var owned in ownedCategories)
            {
                if (_complementaryRules.TryGetValue(owned, out var complementary))
                {
                    if (complementary.Contains(candidateCategory))
                        return owned;
                }
            }
            return null;
        }

        /// <summary>
        /// Extract key benefit from feature text.
        /// </summary>
        private string ExtractFeatureBenefit(string? featureText)
        {
            if (string.IsNullOrWhiteSpace(featureText))
                return string.Empty;

            // Take first sentence
            var sentence = featureText.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(sentence))
                return featureText;

            // Truncate if too long
            if (sentence.Length > 140)
            {
                sentence = sentence.Substring(0, 140);
                var lastSpace = sentence.LastIndexOf(' ');
                if (lastSpace > 0)
                    sentence = sentence.Substring(0, lastSpace);
            }

            return sentence.Trim();
        }

        /// <summary>
        /// Truncate text with ellipsis.
        /// </summary>
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? "";

            return text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Get regional usage score for a device category.
        /// Uses RegionalUsageService if available, otherwise returns fallback.
        /// </summary>
        private double GetRegionalScore(string category, string region)
        {
            if (_regionalUsageService != null && _regionalUsageService.HasData)
            {
                return _regionalUsageService.GetRegionalScore(category, region);
            }

            // Fallback: return default score from config
            return _config.RegionalFallbackScore;
        }

        /// <summary>
        /// Get season score for a device category.
        /// Returns 1.0 if category matches season, 0.0 otherwise.
        /// </summary>
        private double GetSeasonScore(string category, string season)
        {
            if (string.IsNullOrEmpty(season) || _config.SeasonCategoryMap.Count == 0)
                return 0.0;

            var seasonLower = season.ToLowerInvariant().Trim();
            
            if (!_config.SeasonCategoryMap.TryGetValue(seasonLower, out var seasonCategories))
                return 0.0;

            // Check if category matches any season category (case-insensitive)
            var categoryLower = category.ToLowerInvariant().Trim();
            foreach (var seasonCategory in seasonCategories)
            {
                if (string.Equals(seasonCategory, categoryLower, StringComparison.OrdinalIgnoreCase) ||
                    seasonCategory.Contains(categoryLower) ||
                    categoryLower.Contains(seasonCategory.ToLowerInvariant()))
                {
                    return 1.0;
                }
            }

            return 0.0;
        }

        // Internal classes for retrieval and LLM response

        private class RetrievalCandidate
        {
            public string CandidateId { get; set; } = string.Empty;
            public int DeviceIndex { get; set; }
            public DeviceRecord Device { get; set; } = null!;
            public double EmbeddingScore { get; set; }
            public double RoomScore { get; set; }
            public double RuleScore { get; set; }
            public double RegionalScore { get; set; }
            public double SeasonScore { get; set; }
            public double Score { get; set; }
            public List<string> MatchedRooms { get; set; } = new();
            public List<string> RelatedOwned { get; set; } = new();
        }

        private class LLMResponse
        {
            public string? OverallReasoning { get; set; }
            public List<LLMRecommendation>? Recommendations { get; set; }
        }

        private class LLMRecommendation
        {
            public string? CandidateId { get; set; }
            public string? RecommendedDeviceName { get; set; }
            public string? Category { get; set; }
            public string? Reasoning { get; set; }
        }
    }
}