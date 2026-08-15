using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Converts playback callbacks into durable events and user-item aggregates.
/// </summary>
public sealed class PlaybackHistoryService
{
    private const double FinishedPercentage = 90;
    private const double AbandonedPercentage = 10;
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackHistoryService"/> class.
    /// </summary>
    /// <param name="repository">Recommendation repository.</param>
    public PlaybackHistoryService(IRecommendationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Records a playback start event.
    /// </summary>
    /// <param name="update">Playback update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordStartAsync(PlaybackUpdate update, CancellationToken cancellationToken)
        => RecordAsync(update with { EventKind = "Start" }, cancellationToken);

    /// <summary>
    /// Records a playback progress event.
    /// </summary>
    /// <param name="update">Playback update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordProgressAsync(PlaybackUpdate update, CancellationToken cancellationToken)
        => RecordAsync(update with { EventKind = "Progress" }, cancellationToken);

    /// <summary>
    /// Records a playback stop event.
    /// </summary>
    /// <param name="update">Playback update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RecordStopAsync(PlaybackUpdate update, CancellationToken cancellationToken)
        => RecordAsync(update with { EventKind = "Stop" }, cancellationToken);

    private async Task RecordAsync(PlaybackUpdate update, CancellationToken cancellationToken)
    {
        if (update.UserId == Guid.Empty || update.ItemId == Guid.Empty)
        {
            return;
        }

        var now = update.Timestamp.ToUniversalTime();
        var playedPercentage = CalculatePlayedPercentage(update.PositionTicks, update.RuntimeTicks);
        var finished = update.PlayedToCompletion || playedPercentage >= FinishedPercentage;
        var abandoned = update.EventKind == "Stop" && !finished && playedPercentage is > 0 and < AbandonedPercentage;

        await _repository.AddPlaybackEventAsync(
            new PlaybackEvent(
                0,
                update.UserId,
                update.ItemId,
                update.MediaType,
                update.SessionId,
                update.EventKind == "Start" ? now : update.StartedAt?.ToUniversalTime() ?? now,
                update.EventKind == "Stop" ? now : null,
                update.PositionTicks,
                playedPercentage,
                finished,
                abandoned,
                update.EventKind),
            cancellationToken).ConfigureAwait(false);

        var existing = await _repository.GetUserItemStatsAsync(update.UserId, update.ItemId, cancellationToken).ConfigureAwait(false);
        var playCount = existing?.PlayCount ?? 0;
        if (update.EventKind == "Stop" && finished)
        {
            playCount++;
        }

        var merged = new UserItemStats(
            update.UserId,
            update.ItemId,
            update.MediaType,
            existing?.FirstPlayedAt ?? now,
            now,
            Math.Max(playCount, existing?.PlayCount ?? 0),
            Math.Max(existing?.PlayedPercentage ?? 0, playedPercentage ?? 0),
            (existing?.Finished ?? false) || finished,
            abandoned || (existing?.Abandoned ?? false && !finished),
            existing?.JellyfinRating,
            existing?.Likes,
            existing?.IsFavorite ?? false,
            (existing?.Played ?? false) || finished,
            now);

        await _repository.UpsertUserItemStatsAsync(merged, cancellationToken).ConfigureAwait(false);
    }

    private static double? CalculatePlayedPercentage(long? positionTicks, long? runtimeTicks)
    {
        if (!positionTicks.HasValue || !runtimeTicks.HasValue || runtimeTicks.Value <= 0)
        {
            return null;
        }

        return Math.Clamp(positionTicks.Value / (double)runtimeTicks.Value * 100, 0, 100);
    }
}

/// <summary>
/// Playback update normalized from Jellyfin events or tests.
/// </summary>
public sealed record PlaybackUpdate(
    Guid UserId,
    Guid ItemId,
    string MediaType,
    string SessionId,
    DateTimeOffset Timestamp,
    long? PositionTicks,
    long? RuntimeTicks,
    bool PlayedToCompletion,
    DateTimeOffset? StartedAt = null,
    string EventKind = "Progress");
