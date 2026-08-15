using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Matches external Douban subjects to cached Jellyfin library items.
/// </summary>
public sealed class ItemMatchingService
{
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemMatchingService"/> class.
    /// </summary>
    public ItemMatchingService(IRecommendationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Matches Douban cache rows to Jellyfin items and attaches external ratings for one user.
    /// </summary>
    public async Task<OperationResult> MatchDoubanAsync(Guid userId, CancellationToken cancellationToken)
    {
        var libraryItems = await _repository.GetLibraryItemsAsync(cancellationToken).ConfigureAwait(false);
        var doubanItems = await _repository.GetDoubanItemsAsync(cancellationToken).ConfigureAwait(false);
        var matched = 0;

        foreach (var douban in doubanItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = libraryItems
                .Select(item => Score(douban, item))
                .OfType<MatchScore>()
                .Where(static result => result.Confidence >= 0.75)
                .OrderByDescending(static result => result.Confidence)
                .ThenBy(static result => result.RequiresReview)
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            await _repository.UpsertItemMatchAsync(
                new ItemMatch(0, "douban", douban.DoubanSubjectId, match.Item.ItemId, match.Method, match.Confidence, match.RequiresReview, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            if (!match.RequiresReview && userId != Guid.Empty)
            {
                await _repository.UpsertExternalRatingAsync(
                    new ExternalRating(0, userId, "douban", douban.DoubanSubjectId, match.Item.ItemId, douban.UserRating * 2, douban.UserStatus, douban.UserComment, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            matched++;
        }

        return new OperationResult(true, $"Matched {matched} Douban items.", matched);
    }

    private static MatchScore? Score(DoubanItem douban, LibraryItem item)
    {
        if (!string.Equals(douban.MediaType, item.MediaType, StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(douban.MediaType, "Movie", StringComparison.OrdinalIgnoreCase) && string.Equals(item.MediaType, "Series", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(douban.ImdbId)
            && string.Equals(douban.ImdbId, item.ImdbId, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchScore(item, "imdb-id", 1.0, false);
        }

        if (!string.IsNullOrWhiteSpace(douban.TmdbId)
            && string.Equals(douban.TmdbId, item.TmdbId, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchScore(item, "tmdb-id", 0.99, false);
        }

        if (!string.IsNullOrWhiteSpace(douban.TvdbId)
            && string.Equals(douban.TvdbId, item.TvdbId, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchScore(item, "tvdb-id", 0.99, false);
        }

        var doubanTitle = NormalizeTitle(douban.Title);
        var itemTitle = NormalizeTitle(item.Name);
        var originalTitle = NormalizeTitle(item.OriginalTitle);
        if (string.IsNullOrWhiteSpace(doubanTitle))
        {
            return null;
        }

        var titleMatches = doubanTitle == itemTitle || (!string.IsNullOrWhiteSpace(originalTitle) && doubanTitle == originalTitle);
        if (!titleMatches)
        {
            return null;
        }

        if (douban.Year.HasValue && item.Year.HasValue && douban.Year.Value == item.Year.Value)
        {
            return new MatchScore(item, "normalized-title-year", 0.95, false);
        }

        return new MatchScore(item, "normalized-title", 0.80, true);
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private sealed record MatchScore(LibraryItem Item, string Method, double Confidence, bool RequiresReview);
}
