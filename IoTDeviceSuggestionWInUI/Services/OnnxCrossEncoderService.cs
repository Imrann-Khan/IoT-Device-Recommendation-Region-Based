using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Cross-encoder service using ONNX Runtime for relevance scoring.
    /// Scores pairs of (query, candidate) texts using the cross-encoder model.
    /// </summary>
    public class OnnxCrossEncoderService : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly Dictionary<string, long> _vocabulary = new();
        private bool _disposed = false;

        public OnnxCrossEncoderService(string modelPath, string? tokenizerPath = null)
        {
            Console.WriteLine($"[CrossEncoder] Loading ONNX model from: {modelPath}");

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"ONNX model not found: {modelPath}");
            }

            var startTime = DateTime.Now;
            _session = new InferenceSession(modelPath);
            var loadTime = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"[CrossEncoder] ✓ Model loaded in {loadTime:F2} seconds");

            // Load vocabulary from tokenizer.json
            var tokenizerFile = tokenizerPath ?? Path.Combine(Path.GetDirectoryName(modelPath) ?? "", "tokenizer.json");
            if (File.Exists(tokenizerFile))
            {
                Console.WriteLine($"[CrossEncoder] Loading vocabulary from: {tokenizerFile}");
                LoadVocabulary(tokenizerFile);
                Console.WriteLine($"[CrossEncoder] ✓ Loaded {_vocabulary.Count} vocabulary tokens");
            }
            else
            {
                Console.WriteLine($"[CrossEncoder] WARNING: tokenizer.json not found at {tokenizerFile}");
                Console.WriteLine("[CrossEncoder] Using simple word-based tokenization");
            }
        }

        private void LoadVocabulary(string tokenizerPath)
        {
            try
            {
                var json = File.ReadAllText(tokenizerPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("model", out var model) && 
                    model.TryGetProperty("vocab", out var vocab))
                {
                    foreach (var prop in vocab.EnumerateObject())
                    {
                        _vocabulary[prop.Name] = prop.Value.GetInt64();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CrossEncoder] Error loading vocabulary: {ex.Message}");
            }
        }

        /// <summary>
        /// Score a single pair (query, candidate).
        /// </summary>
        public float ScorePair(string query, string candidate)
        {
            var text = $"{query} [SEP] {candidate}";
            var (inputIds, attentionMask) = TokenizeText(text);

            var inputIdsTensor = new DenseTensor<long>(inputIds, new int[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new int[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(new long[inputIds.Length], new int[] { 1, inputIds.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            // For cross-encoder, output is usually a single logit per example
            return outputTensor[0, 0];
        }

        /// <summary>
        /// Score multiple pairs in batch.
        /// </summary>
        public float[] ScorePairs(List<Tuple<string, string>> pairs)
        {
            var scores = new float[pairs.Count];

            for (int i = 0; i < pairs.Count; i++)
            {
                try
                {
                    scores[i] = ScorePair(pairs[i].Item1, pairs[i].Item2);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CrossEncoder] Error scoring pair {i}: {ex.Message}");
                    scores[i] = 0.0f;
                }

                // Progress logging
                if ((i + 1) % 10 == 0 || (i + 1) == pairs.Count)
                {
                    Console.WriteLine($"[CrossEncoder] Scored {i + 1}/{pairs.Count} pairs");
                }
            }

            return scores;
        }

        private (long[] InputIds, long[] AttentionMask) TokenizeText(string text)
        {
            var maxLength = 512;
            var tokens = new List<long> { 101 }; // [CLS]
            var attentionMask = new List<long> { 1 };

            var words = text.ToLower()
                .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']' }, 
                    StringSplitOptions.RemoveEmptyEntries)
                .Take(maxLength - 2);

            foreach (var word in words)
            {
                long tokenId;

                if (_vocabulary.TryGetValue(word, out var vocabId))
                {
                    tokenId = vocabId;
                }
                else if (_vocabulary.TryGetValue("##" + word, out var subwordId))
                {
                    tokenId = subwordId;
                }
                else if (_vocabulary.TryGetValue(word.ToLower(), out var lowerId))
                {
                    tokenId = lowerId;
                }
                else
                {
                    tokenId = 100; // [UNK]
                }

                tokens.Add(tokenId);
                attentionMask.Add(1);
            }

            tokens.Add(102); // [SEP]
            attentionMask.Add(1);

            while (tokens.Count < maxLength)
            {
                tokens.Add(0);
                attentionMask.Add(0);
            }

            return (tokens.ToArray(), attentionMask.ToArray());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _session?.Dispose();
            _disposed = true;
        }
    }
}
