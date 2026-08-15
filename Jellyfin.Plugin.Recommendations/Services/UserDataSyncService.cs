using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Merges Jellyfin user data into local user-item aggregates.
/// </summary>
public sealed class UserDataSyncService
{
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataSyncService"/> class.
    /// </summary>
    public UserDataSyncService(IRecommendationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Syncs one Jellyfin user-data save event.
    /// </summary>
    public async Task SyncUserDataAsync(UserDataSaveEventArgs args, CancellationToken cancellationToken)
    {
        if (args.Item is null || args.UserData is null || args.UserId == Guid.Empty)
        {
            return;
        }

        var existing = await _repository.GetUserItemStatsAsync(args.UserId, args.Item.Id, cancellationToken).ConfigureAwait(false);
        var runtimeTicks = args.Item.RunTimeTicks;
        var playedPercentage = args.UserData.Played ? 100 : CalculatePlayedPercentage(args.UserData.PlaybackPositionTicks, runtimeTicks);
        var lastPlayed = args.UserData.LastPlayedDate.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(args.UserData.LastPlayedDate.Value, DateTimeKind.Utc))
            : existing?.LastPlayedAt;
        var now = DateTimeOffset.UtcNow;

        await _repository.UpsertUserItemStatsAsync(
            new UserItemStats(
                args.UserId,
                args.Item.Id,
                args.Item.GetClientTypeName(),
                existing?.FirstPlayedAt ?? lastPlayed,
                lastPlayed,
                Math.Max(existing?.PlayCount ?? 0, args.UserData.PlayCount),
                Math.Max(existing?.PlayedPercentage ?? 0, playedPercentage),
                (existing?.Finished ?? false) || args.UserData.Played,
                existing?.Abandoned ?? false,
                args.UserData.Rating,
                args.UserData.Likes,
                args.UserData.IsFavorite,
                args.UserData.Played,
                now),
            cancellationToken).ConfigureAwait(false);
    }

    private static double CalculatePlayedPercentage(long playbackPositionTicks, long? runtimeTicks)
    {
        if (!runtimeTicks.HasValue || runtimeTicks.Value <= 0)
        {
            return 0;
        }

        return Math.Clamp(playbackPositionTicks / (double)runtimeTicks.Value * 100, 0, 100);
    }
}
