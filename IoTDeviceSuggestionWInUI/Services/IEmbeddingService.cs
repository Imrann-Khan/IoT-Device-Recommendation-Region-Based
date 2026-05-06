using System.Collections.Generic;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Interface for text embedding generation.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generate embedding for a single text.
        /// </summary>
        float[] GenerateEmbedding(string text);

        /// <summary>
        /// Generate embeddings for multiple texts.
        /// </summary>
        float[][] GenerateEmbeddings(IEnumerable<string> texts);

        /// <summary>
        /// Get the dimension of the embeddings.
        /// </summary>
        int EmbeddingDimension { get; }
    }
}