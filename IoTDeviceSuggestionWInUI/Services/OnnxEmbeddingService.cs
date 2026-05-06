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
    /// ONNX-based embedding service using sentence transformer model.
    /// </summary>
    public class OnnxEmbeddingService : IEmbeddingService, IDisposable
    {
        private readonly InferenceSession _session;
        private readonly Dictionary<string, long> _vocabulary = new();
        private bool _disposed = false;

        public int EmbeddingDimension { get; private set; } = 384;

        public OnnxEmbeddingService(string modelPath, string? tokenizerPath = null)
        {
            Console.WriteLine($"[ONNX] Loading ONNX model from: {modelPath}");
            
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"ONNX model not found: {modelPath}");
            }

            var startTime = DateTime.Now;
            _session = new InferenceSession(modelPath);
            var loadTime = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"[ONNX] ✓ Model loaded in {loadTime:F2} seconds");
            
            // Get embedding dimension from model output
            var outputInfo = _session.OutputMetadata.Values.FirstOrDefault();
            if (outputInfo != null)
            {
                EmbeddingDimension = outputInfo.Dimensions.Last();
                Console.WriteLine($"[ONNX] Output dimensions: {string.Join(", ", outputInfo.Dimensions)}");
            }
            Console.WriteLine($"[ONNX] Embedding dimension: {EmbeddingDimension}");

            // Load vocabulary from tokenizer.json
            var tokenizerFile = tokenizerPath ?? Path.Combine(Path.GetDirectoryName(modelPath) ?? "", "tokenizer.json");
            if (File.Exists(tokenizerFile))
            {
                Console.WriteLine($"[ONNX] Loading vocabulary from: {tokenizerFile}");
                LoadVocabulary(tokenizerFile);
                Console.WriteLine($"[ONNX] ✓ Loaded {_vocabulary.Count} vocabulary tokens");
            }
            else
            {
                Console.WriteLine($"[ONNX] WARNING: tokenizer.json not found at {tokenizerFile}");
                Console.WriteLine("[ONNX] Using simple word-based tokenization (less accurate)");
            }
        }

        private void LoadVocabulary(string tokenizerPath)
        {
            try
            {
                var json = File.ReadAllText(tokenizerPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // The vocabulary is under model.vocab
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
                Console.WriteLine($"[ONNX] Error loading vocabulary: {ex.Message}");
            }
        }

        public float[] GenerateEmbedding(string text)
        {
            var embeddings = GenerateEmbeddings(new[] { text });
            return embeddings[0];
        }

        public float[][] GenerateEmbeddings(IEnumerable<string> texts)
        {
            var textList = texts.ToList();
            Console.WriteLine($"[ONNX] Generating embeddings for {textList.Count} texts...");
            
            var startTime = DateTime.Now;
            var results = new List<float[]>();
            
            for (int i = 0; i < textList.Count; i++)
            {
                var text = textList[i];
                var embedding = GenerateSingleEmbedding(text);
                results.Add(embedding);
                
                // Progress logging every 100 embeddings
                if ((i + 1) % 100 == 0 || (i + 1) == textList.Count)
                {
                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    var rate = (i + 1) / Math.Max(elapsed, 0.001);
                    Console.WriteLine($"[ONNX] Progress: {i + 1}/{textList.Count} ({rate:F1} embeddings/sec)");
                }
            }

            var totalTime = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"[ONNX] ✓ Generated {results.Count} embeddings in {totalTime:F2} seconds");

            return results.ToArray();
        }

        private float[] GenerateSingleEmbedding(string text)
        {
            // Tokenize text
            var (inputIds, attentionMask) = TokenizeText(text);

            // Create input tensors
            var inputIdsTensor = new DenseTensor<long>(inputIds, new int[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new int[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(new long[inputIds.Length], new int[] { 1, inputIds.Length });

            // Create named onnx values
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            // Run inference
            using var results = _session.Run(inputs);
            
            // Get the output tensor
            var outputTensor = results.First().AsTensor<float>();
            
            // Mean pooling over sequence dimension
            var embedding = MeanPooling(outputTensor, attentionMask);
            
            // Normalize the embedding
            return NormalizeEmbedding(embedding);
        }

        private (long[] InputIds, long[] AttentionMask) TokenizeText(string text)
        {
            var maxLength = 128; // Match the tokenizer config
            var tokens = new List<long> { 101 }; // [CLS]
            var attentionMask = new List<long> { 1 };

            // Simple word-based tokenization with vocabulary lookup
            var words = text.ToLower()
                .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']' }, 
                    StringSplitOptions.RemoveEmptyEntries)
                .Take(maxLength - 2);

            foreach (var word in words)
            {
                long tokenId;
                
                // Try exact match first
                if (_vocabulary.TryGetValue(word, out var vocabId))
                {
                    tokenId = vocabId;
                }
                // Try with ## prefix (subword)
                else if (_vocabulary.TryGetValue("##" + word, out var subwordId))
                {
                    tokenId = subwordId;
                }
                // Try lowercase
                else if (_vocabulary.TryGetValue(word.ToLower(), out var lowerId))
                {
                    tokenId = lowerId;
                }
                // Unknown token
                else
                {
                    tokenId = 100; // [UNK] token
                }
                
                tokens.Add(tokenId);
                attentionMask.Add(1);
            }

            tokens.Add(102); // [SEP]
            attentionMask.Add(1);

            // Pad to max length
            while (tokens.Count < maxLength)
            {
                tokens.Add(0);
                attentionMask.Add(0);
            }

            return (tokens.ToArray(), attentionMask.ToArray());
        }

        private float[] MeanPooling(Tensor<float> lastHiddenState, long[] attentionMask)
        {
            var dimensions = lastHiddenState.Dimensions.ToArray();
            int seqLen = (int)dimensions[1];
            int hiddenSize = (int)dimensions[2];

            var pooled = new float[hiddenSize];
            var maskSum = attentionMask.Sum();

            var dataArray = lastHiddenState.ToArray();
            
            for (int h = 0; h < hiddenSize; h++)
            {
                float sum = 0;
                for (int s = 0; s < seqLen; s++)
                {
                    if (attentionMask[s] > 0)
                    {
                        int index = s * hiddenSize + h;
                        sum += dataArray[index];
                    }
                }
                pooled[h] = sum / Math.Max(maskSum, 1);
            }

            return pooled;
        }

        private float[] NormalizeEmbedding(float[] embedding)
        {
            var magnitude = Math.Sqrt(embedding.Sum(x => (double)x * x));
            if (magnitude < 1e-10)
                return embedding;

            return embedding.Select(x => (float)(x / magnitude)).ToArray();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _disposed = true;
            }
        }
    }
}