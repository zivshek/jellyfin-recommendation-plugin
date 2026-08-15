using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Indexes Jellyfin movies and series as recommendation candidates.
/// </summary>
public sealed class LibraryIndexService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryIndexService"/> class.
    /// </summary>
    public LibraryIndexService(ILibraryManager libraryManager, IRecommendationRepository repository)
    {
        _libraryManager = libraryManager;
        _repository = repository;
    }

    /// <summary>
    /// Rebuilds the local candidate cache.
    /// </summary>
    public async Task<OperationResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var result = _libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false
        });

        var count = 0;
        foreach (var item in result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _repository.UpsertLibraryItemAsync(ToLibraryItem(item), cancellationToken).ConfigureAwait(false);
            count++;
        }

        return new OperationResult(true, $"Indexed {count} library items.", count);
    }

    private LibraryItem ToLibraryItem(BaseItem item)
    {
        item.ProviderIds.TryGetValue("Imdb", out var imdbId);
        item.ProviderIds.TryGetValue("Tmdb", out var tmdbId);
        item.ProviderIds.TryGetValue("Tvdb", out var tvdbId);
        var people = _libraryManager.GetPeople(item)
            .Where(person => !string.IsNullOrWhiteSpace(person.Name))
            .Select(person => string.IsNullOrWhiteSpace(person.Type.ToString())
                ? person.Name
                : $"{person.Type}: {person.Name}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LibraryItem(
            item.Id,
            item.Name,
            item.OriginalTitle,
            item.ProductionYear,
            item.GetClientTypeName(),
            item.Genres ?? [],
            people,
            item.Studios ?? [],
            item.Overview,
            item.CommunityRating,
            imdbId,
            tmdbId,
            tvdbId,
            !item.IsVirtualItem,
            DateTimeOffset.UtcNow);
    }
}
