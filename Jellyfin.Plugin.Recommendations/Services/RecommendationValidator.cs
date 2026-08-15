using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Validates recommendation output against the known candidate set.
/// </summary>
public sealed class RecommendationValidator
{
    /// <summary>
    /// Removes invalid IDs and duplicate items while preserving order.
    /// </summary>
    /// <param name="items">Raw recommendations.</param>
    /// <param name="candidateIds">Allowed candidate IDs.</param>
    public IReadOnlyList<ValidatedRecommendation> Validate(
        IReadOnlyList<ValidatedRecommendation> items,
        IReadOnlySet<Guid> candidateIds)
    {
        var seen = new HashSet<Guid>();
        return items
            .Where(item => candidateIds.Contains(item.ItemId))
            .Where(item => seen.Add(item.ItemId))
            .Select((item, index) => item with
            {
                Rank = index + 1,
                Confidence = Math.Clamp(item.Confidence, 0, 1),
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? "Recommended from validated candidate metadata." : item.Reason
            })
            .ToArray();
    }
}
