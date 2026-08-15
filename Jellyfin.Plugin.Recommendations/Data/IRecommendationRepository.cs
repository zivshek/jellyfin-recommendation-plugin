using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Data;

/// <summary>
/// Persistence abstraction for recommendation plugin state.
/// </summary>
public interface IRecommendationRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<long> AddPlaybackEventAsync(PlaybackEvent playbackEvent, CancellationToken cancellationToken);

    Task UpsertUserItemStatsAsync(UserItemStats stats, CancellationToken cancellationToken);

    Task<UserItemStats?> GetUserItemStatsAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserItemStats>> GetUserItemStatsAsync(Guid userId, CancellationToken cancellationToken);

    Task UpsertLibraryItemAsync(LibraryItem item, CancellationToken cancellationToken);

    Task<IReadOnlyList<LibraryItem>> GetLibraryItemsAsync(CancellationToken cancellationToken);

    Task<LibraryItem?> GetLibraryItemAsync(Guid itemId, CancellationToken cancellationToken);

    Task UpsertDoubanItemAsync(DoubanItem item, CancellationToken cancellationToken);

    Task<IReadOnlyList<DoubanItem>> GetDoubanItemsAsync(CancellationToken cancellationToken);

    Task UpsertExternalRatingAsync(ExternalRating rating, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalRating>> GetExternalRatingsAsync(Guid userId, CancellationToken cancellationToken);

    Task UpsertItemMatchAsync(ItemMatch match, CancellationToken cancellationToken);

    Task<IReadOnlyList<ItemMatch>> GetItemMatchesAsync(string provider, CancellationToken cancellationToken);

    Task<long> AddRecommendationRunAsync(RecommendationRun run, CancellationToken cancellationToken);

    Task AddRecommendationItemsAsync(long runId, IReadOnlyList<ValidatedRecommendation> items, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecommendationItem>> GetLatestRecommendationItemsAsync(Guid userId, CancellationToken cancellationToken);

    Task UpsertManagedCollectionAsync(ManagedCollection collection, CancellationToken cancellationToken);

    Task<ManagedCollection?> GetManagedCollectionAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetManagedCollectionItemIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task ReplaceManagedCollectionItemIdsAsync(Guid userId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);

    Task<PluginStatus> GetStatusAsync(CancellationToken cancellationToken);
}
