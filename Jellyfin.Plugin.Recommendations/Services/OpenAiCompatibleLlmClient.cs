using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Configuration;
using Jellyfin.Plugin.Recommendations.Domain;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Minimal OpenAI-compatible chat completions client.
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private static readonly SemaphoreSlim RequestThrottle = new(1, 1);
    private static readonly TimeSpan MinimumRequestSpacing = TimeSpan.FromSeconds(2);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly ILogger<OpenAiCompatibleLlmClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleLlmClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public OpenAiCompatibleLlmClient(ILogger<OpenAiCompatibleLlmClient> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidatedRecommendation>> RecommendAsync(
        PluginConfiguration configuration,
        IReadOnlyList<RecommendationCandidate> candidates,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.Equals(configuration.LlmProvider, "disabled", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuration.LlmBaseUrl)
            || string.IsNullOrWhiteSpace(configuration.LlmApiKey)
            || string.IsNullOrWhiteSpace(configuration.LlmModel))
        {
            return [];
        }

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.LlmApiKey);

        var endpoint = new Uri(new Uri(configuration.LlmBaseUrl.TrimEnd('/') + "/"), "chat/completions");
        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Requesting {Limit} recommendations from LLM provider {Provider} model {Model}.", limit, configuration.LlmProvider, configuration.LlmModel);
        using var response = await httpClient.PostAsync(
            endpoint,
            new StringContent(BuildRequest(configuration, candidates, limit), Encoding.UTF8, "application/json"),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var parsed = ParseRecommendations(content);
        _logger.LogInformation("Parsed {RecommendationCount} recommendations from LLM response.", parsed.Count);
        return parsed;
    }

    private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        await RequestThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
            if (elapsed < MinimumRequestSpacing)
            {
                await Task.Delay(MinimumRequestSpacing - elapsed, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            RequestThrottle.Release();
        }
    }

    private static string BuildRequest(PluginConfiguration configuration, IReadOnlyList<RecommendationCandidate> candidates, int limit)
    {
        var candidatePayload = candidates.Take(80).Select(candidate => new
        {
            id = candidate.Item.ItemId,
            eligible = configuration.IncludeWatchedItems || candidate.UserStats is null || !candidate.UserStats.Played,
            candidate.Item.Name,
            candidate.Item.OriginalTitle,
            candidate.Item.Year,
            type = candidate.Item.MediaType,
            candidate.Item.Genres,
            candidate.Item.People,
            candidate.Item.Studios,
            overview = Truncate(candidate.Item.Overview, 240),
            candidate.Item.CommunityRating,
            candidate.Item.ImdbId,
            candidate.Item.TmdbId,
            candidate.Item.TvdbId,
            watched = candidate.UserStats?.Played ?? false,
            completed = candidate.UserStats?.Finished ?? false,
            playCount = candidate.UserStats?.PlayCount ?? 0,
            rating = candidate.UserStats?.JellyfinRating,
            favorite = candidate.UserStats?.IsFavorite ?? false,
            liked = candidate.UserStats?.Likes,
            externalRatings = candidate.ExternalRatings.Select(rating => new
            {
                rating.Provider,
                rating.ExternalId,
                rating.Rating,
                rating.Status
            })
        });
        var prompt = new
        {
            instructions = "Return strict JSON only: {\"items\":[{\"itemId\":\"guid\",\"reason\":\"short reason\",\"confidence\":0.0}]}. Recommend only candidate IDs where eligible is true.",
            limit,
            tasteProfile = BuildTasteProfile(candidates),
            candidates = candidatePayload
        };
        var request = new
        {
            model = configuration.LlmModel,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = "You recommend existing Jellyfin library items. You must output valid JSON only." },
                new { role = "user", content = JsonSerializer.Serialize(prompt, JsonOptions) }
            }
        };

        return JsonSerializer.Serialize(request, JsonOptions);
    }

    private static object BuildTasteProfile(IReadOnlyList<RecommendationCandidate> candidates)
    {
        var liked = candidates
            .Where(IsPositiveSignal)
            .OrderByDescending(PreferenceStrength)
            .Take(12)
            .Select(ToTasteSignal);
        var disliked = candidates
            .Where(IsNegativeSignal)
            .OrderBy(PreferenceStrength)
            .Take(12)
            .Select(ToTasteSignal);

        return new
        {
            liked,
            disliked
        };
    }

    private static bool IsPositiveSignal(RecommendationCandidate candidate)
        => candidate.UserStats?.JellyfinRating >= 7
            || candidate.UserStats?.IsFavorite == true
            || candidate.UserStats?.Likes == true
            || candidate.ExternalRatings.Any(static rating => rating.Rating >= 8);

    private static bool IsNegativeSignal(RecommendationCandidate candidate)
        => candidate.UserStats?.JellyfinRating <= 4
            || candidate.UserStats?.Likes == false
            || candidate.UserStats?.Abandoned == true
            || candidate.ExternalRatings.Any(static rating => rating.Rating <= 4);

    private static double PreferenceStrength(RecommendationCandidate candidate)
    {
        var score = 0.0;
        if (candidate.UserStats?.JellyfinRating is { } jellyfinRating)
        {
            score += jellyfinRating - 5;
        }

        if (candidate.UserStats?.IsFavorite == true)
        {
            score += 3;
        }

        if (candidate.UserStats?.Likes == true)
        {
            score += 2;
        }
        else if (candidate.UserStats?.Likes == false)
        {
            score -= 3;
        }

        foreach (var rating in candidate.ExternalRatings.Where(static rating => rating.Rating.HasValue))
        {
            score += (rating.Rating!.Value - 5) * 0.5;
        }

        if (candidate.UserStats?.Abandoned == true)
        {
            score -= 1;
        }

        return score;
    }

    private static object ToTasteSignal(RecommendationCandidate candidate)
        => new
        {
            candidate.Item.Name,
            candidate.Item.Year,
            type = candidate.Item.MediaType,
            candidate.Item.Genres,
            candidate.Item.People,
            jellyfinRating = candidate.UserStats?.JellyfinRating,
            favorite = candidate.UserStats?.IsFavorite ?? false,
            liked = candidate.UserStats?.Likes,
            externalRatings = candidate.ExternalRatings.Select(static rating => new
            {
                rating.Provider,
                rating.Rating,
                rating.Status
            })
        };

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static IReadOnlyList<ValidatedRecommendation> ParseRecommendations(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var recommendations = new List<ValidatedRecommendation>();
        var rank = 1;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("itemId", out var itemIdElement)
                || !Guid.TryParse(itemIdElement.GetString(), out var itemId))
            {
                continue;
            }

            var reason = item.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : null;
            var confidence = item.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : 0.5;

            recommendations.Add(new ValidatedRecommendation(
                itemId,
                rank++,
                string.IsNullOrWhiteSpace(reason) ? "Recommended by LLM reranker." : reason,
                confidence,
                "llm"));
        }

        return recommendations;
    }
}
