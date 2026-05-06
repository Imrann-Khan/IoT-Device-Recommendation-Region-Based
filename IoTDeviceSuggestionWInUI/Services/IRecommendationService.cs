using IoTDeviceSuggestionWInUI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IoTDeviceSuggestionWInUI.Services
{
    /// <summary>
    /// Interface for the recommendation service.
    /// </summary>
    public interface IRecommendationService
    {
        /// <summary>
        /// Get device recommendations for a user profile.
        /// </summary>
        Task<List<RecommendationResult>> GetRecommendationsAsync(UserProfile userProfile, int topN = 8);

        /// <summary>
        /// Get device recommendations for a specific room.
        /// </summary>
        Task<List<RecommendationResult>> GetRecommendationsForRoomAsync(UserProfile userProfile, string room, int topN = 8);
    }
}