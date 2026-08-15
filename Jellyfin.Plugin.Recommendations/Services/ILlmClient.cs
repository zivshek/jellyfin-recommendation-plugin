using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Configuration;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// LLM client abstraction for recommendation reranking.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Requests recommendations from an LLM.
    /// </summary>
    Task<IReadOnlyList<ValidatedRecommendation>> RecommendAsync(
        PluginConfiguration configuration,
        IReadOnlyList<RecommendationCandidate> candidates,
        int limit,
        CancellationToken cancellationToken);
}
