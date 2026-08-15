using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Deterministic recommendation scorer used as the MVP engine and LLM fallback.
/// </summary>
public sealed class RecommendationEngine
{
    /// <summary>
    /// Scores candidates and returns ordered recommendations.
    /// </summary>
    /// <param name="candidates">Candidate items.</param>
    /// <param name="limit">Maximum result count.</param>
    /// <param name="includeWatched">Whether watched items may be returned.</param>
    public IReadOnlyList<ValidatedRecommendation> Recommend(
        IReadOnlyList<RecommendationCandidate> candidates,
        int limit,
        bool includeWatched)
    {
        return candidates
            .Where(candidate => candidate.Item.IsPlayable)
            .Where(candidate => includeWatched || candidate.UserStats is null || !candidate.UserStats.Played)
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = Score(candidate)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.Item.CommunityRating ?? 0)
            .ThenBy(item => item.Candidate.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .Select((item, index) => new ValidatedRecommendation(
                item.Candidate.Item.ItemId,
                index + 1,
                BuildReason(item.Candidate, item.Score),
                Math.Clamp(item.Score / 10, 0.01, 0.99),
                "deterministic"))
            .ToArray();
    }

    /// <summary>
    /// Scores one candidate.
    /// </summary>
    /// <param name="candidate">Candidate item.</param>
    public static double Score(RecommendationCandidate candidate)
    {
        var score = 0.0;
        var item = candidate.Item;
        if (item.CommunityRating.HasValue)
        {
            score += item.CommunityRating.Value * 0.35;
        }

        foreach (var rating in candidate.ExternalRatings)
        {
            if (rating.Rating.HasValue)
            {
                score += (rating.Rating.Value - 5) * 0.8;
            }

            if (string.Equals(rating.Status, "想看", StringComparison.OrdinalIgnoreCase))
            {
                score += 1.5;
            }
        }

        var stats = candidate.UserStats;
        if (stats is null)
        {
            return score + 1;
        }

        if (stats.JellyfinRating.HasValue)
        {
            score += (stats.JellyfinRating.Value - 5) * 1.2;
        }

        if (stats.IsFavorite)
        {
            score += 4;
        }

        if (stats.Likes == true)
        {
            score += 3;
        }
        else if (stats.Likes == false)
        {
            score -= 4;
        }

        if (stats.Finished)
        {
            score += 1.5;
        }

        if (stats.PlayCount > 1)
        {
            score += Math.Min(3, stats.PlayCount - 1);
        }

        if (stats.Abandoned)
        {
            score -= 1;
        }

        return score;
    }

    private static string BuildReason(RecommendationCandidate candidate, double score)
    {
        var reasons = new List<string>();
        if (candidate.Item.CommunityRating.HasValue)
        {
            reasons.Add("strong community rating");
        }

        if (candidate.ExternalRatings.Any(static rating => rating.Rating >= 8))
        {
            reasons.Add("matches imported high-rating signals");
        }

        if (candidate.UserStats?.IsFavorite == true || candidate.UserStats?.Likes == true)
        {
            reasons.Add("related to explicit Jellyfin likes");
        }

        return reasons.Count == 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Deterministic score {score:0.0}.")
            : string.Concat(char.ToUpperInvariant(reasons[0][0]), reasons[0][1..], reasons.Count > 1 ? "; " + string.Join("; ", reasons.Skip(1)) : ".");
    }
}
