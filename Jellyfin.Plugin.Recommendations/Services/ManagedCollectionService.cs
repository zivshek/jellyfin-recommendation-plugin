using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Configuration;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Creates and updates Jellyfin collections managed by the plugin.
/// </summary>
public sealed class ManagedCollectionService
{
    private readonly ICollectionManager _collectionManager;
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedCollectionService"/> class.
    /// </summary>
    public ManagedCollectionService(ICollectionManager collectionManager, IRecommendationRepository repository)
    {
        _collectionManager = collectionManager;
        _repository = repository;
    }

    /// <summary>
    /// Updates the recommendation collection for one user.
    /// </summary>
    public async Task<OperationResult> UpdateCollectionAsync(Guid userId, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var recommendations = await _repository.GetLatestRecommendationItemsAsync(userId, cancellationToken).ConfigureAwait(false);
        if (recommendations.Count == 0)
        {
            return new OperationResult(false, "No successful recommendation run found for this user.");
        }

        var collection = await _repository.GetManagedCollectionAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? await CreateCollectionAsync(userId, configuration, recommendations, cancellationToken).ConfigureAwait(false);

        var desiredIds = recommendations.Select(static item => item.ItemId).Distinct().ToArray();
        var previousIds = await _repository.GetManagedCollectionItemIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var diff = ComputeDiff(previousIds, desiredIds);
        if (diff.Remove.Count > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(collection.CollectionId, diff.Remove).ConfigureAwait(false);
        }

        if (diff.Add.Count > 0)
        {
            await _collectionManager.AddToCollectionAsync(collection.CollectionId, diff.Add).ConfigureAwait(false);
        }

        await _repository.ReplaceManagedCollectionItemIdsAsync(userId, desiredIds, cancellationToken).ConfigureAwait(false);

        return new OperationResult(true, $"Updated collection '{collection.Name}' with {desiredIds.Length} items.", desiredIds.Length);
    }

    /// <summary>
    /// Computes which plugin-managed items to add and remove.
    /// </summary>
    public static CollectionDiff ComputeDiff(IReadOnlyList<Guid> previousIds, IReadOnlyList<Guid> desiredIds)
    {
        var previous = previousIds.ToHashSet();
        var desired = desiredIds.ToHashSet();
        return new CollectionDiff(
            desired.Where(id => !previous.Contains(id)).ToArray(),
            previous.Where(id => !desired.Contains(id)).ToArray());
    }

    private async Task<ManagedCollection> CreateCollectionAsync(
        Guid userId,
        PluginConfiguration configuration,
        IReadOnlyList<RecommendationItem> recommendations,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(configuration.RecommendationCollectionName)
            ? "Recommended For You"
            : configuration.RecommendationCollectionName;
        var boxSet = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
        {
            Name = name,
            UserIds = [userId],
            ItemIdList = recommendations.Select(static item => item.ItemId.ToString("N")).ToArray(),
            IsLocked = true
        }).ConfigureAwait(false);

        var collection = new ManagedCollection(userId, boxSet.Id, name, DateTimeOffset.UtcNow);
        await _repository.UpsertManagedCollectionAsync(collection, cancellationToken).ConfigureAwait(false);
        return collection;
    }
}

/// <summary>
/// Collection update diff.
/// </summary>
public sealed record CollectionDiff(IReadOnlyList<Guid> Add, IReadOnlyList<Guid> Remove);
