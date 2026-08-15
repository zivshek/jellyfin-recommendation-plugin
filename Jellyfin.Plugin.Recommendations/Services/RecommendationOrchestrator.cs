using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Configuration;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Coordinates candidate loading, LLM validation, fallback ranking, and persistence.
/// </summary>
public sealed class RecommendationOrchestrator
{
    private readonly IRecommendationRepository _repository;
    private readonly RecommendationEngine _engine;
    private readonly RecommendationValidator _validator;
    private readonly ILlmClient _llmClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationOrchestrator"/> class.
    /// </summary>
    public RecommendationOrchestrator(
        IRecommendationRepository repository,
        RecommendationEngine engine,
        RecommendationValidator validator,
        ILlmClient llmClient)
    {
        _repository = repository;
        _engine = engine;
        _validator = validator;
        _llmClient = llmClient;
    }

    /// <summary>
    /// Generates and stores recommendations for one user.
    /// </summary>
    public async Task<OperationResult> GenerateAsync(Guid userId, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var candidates = await BuildCandidatesAsync(userId, cancellationToken).ConfigureAwait(false);
        var eligibleCandidateIds = candidates
            .Where(candidate => IsEligible(candidate, configuration.IncludeWatchedItems))
            .Select(static candidate => candidate.Item.ItemId)
            .ToHashSet();
        if (candidates.Count == 0 || eligibleCandidateIds.Count == 0)
        {
            await StoreRunAsync(userId, "empty", configuration, "Failed", "No eligible library candidates found.", [], cancellationToken).ConfigureAwait(false);
            return new OperationResult(false, "No eligible library candidates found.");
        }

        var limit = Math.Clamp(configuration.RecommendationLimit, 1, 100);
        IReadOnlyList<ValidatedRecommendation> recommendations = [];
        var provider = "deterministic";
        var model = "fallback";
        string? error = null;

        try
        {
            var llmItems = await _llmClient.RecommendAsync(configuration, candidates, limit, cancellationToken).ConfigureAwait(false);
            recommendations = _validator.Validate(llmItems, eligibleCandidateIds);
            if (recommendations.Count > 0)
            {
                provider = configuration.LlmProvider;
                model = configuration.LlmModel;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (recommendations.Count == 0)
        {
            recommendations = _engine.Recommend(candidates, limit, configuration.IncludeWatchedItems);
        }

        var inputHash = HashCandidates(candidates);
        await StoreRunAsync(userId, inputHash, configuration, "Succeeded", error, recommendations, cancellationToken, provider, model).ConfigureAwait(false);
        return new OperationResult(true, $"Generated {recommendations.Count} recommendations.", recommendations.Count);
    }

    /// <summary>
    /// Builds recommendation candidates for a user.
    /// </summary>
    public async Task<IReadOnlyList<RecommendationCandidate>> BuildCandidatesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var libraryItems = await _repository.GetLibraryItemsAsync(cancellationToken).ConfigureAwait(false);
        var stats = (await _repository.GetUserItemStatsAsync(userId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(static item => item.ItemId);
        var externalRatings = await _repository.GetExternalRatingsAsync(userId, cancellationToken).ConfigureAwait(false);
        var ratingsByItem = externalRatings
            .Where(static rating => rating.ItemId.HasValue)
            .GroupBy(static rating => rating.ItemId!.Value)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ExternalRating>)group.ToArray());

        return libraryItems
            .Select(item =>
            {
                stats.TryGetValue(item.ItemId, out var itemStats);
                ratingsByItem.TryGetValue(item.ItemId, out var itemRatings);
                return new RecommendationCandidate(item, itemStats, itemRatings ?? []);
            })
            .ToArray();
    }

    private async Task StoreRunAsync(
        Guid userId,
        string inputHash,
        PluginConfiguration configuration,
        string status,
        string? error,
        IReadOnlyList<ValidatedRecommendation> recommendations,
        CancellationToken cancellationToken,
        string provider = "deterministic",
        string model = "fallback")
    {
        var runId = await _repository.AddRecommendationRunAsync(
            new RecommendationRun(0, userId, inputHash, provider, model, status, error, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        if (recommendations.Count > 0)
        {
            await _repository.AddRecommendationItemsAsync(runId, recommendations, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsEligible(RecommendationCandidate candidate, bool includeWatched)
        => candidate.Item.IsPlayable
            && (includeWatched || candidate.UserStats is null || !candidate.UserStats.Played);

    private static string HashCandidates(IReadOnlyList<RecommendationCandidate> candidates)
    {
        var json = JsonSerializer.Serialize(candidates.Select(static candidate => new
        {
            candidate.Item.ItemId,
            ItemUpdatedAt = candidate.Item.UpdatedAt,
            StatsUpdatedAt = candidate.UserStats?.UpdatedAt
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
