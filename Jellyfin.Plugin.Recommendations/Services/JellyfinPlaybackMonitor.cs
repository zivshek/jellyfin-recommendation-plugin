using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Subscribes to Jellyfin playback events and records watch history.
/// </summary>
public sealed class JellyfinPlaybackMonitor : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly PlaybackHistoryService _historyService;
    private readonly ILogger<JellyfinPlaybackMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPlaybackMonitor"/> class.
    /// </summary>
    public JellyfinPlaybackMonitor(
        ISessionManager sessionManager,
        PlaybackHistoryService historyService,
        ILogger<JellyfinPlaybackMonitor> logger)
    {
        _sessionManager = sessionManager;
        _historyService = historyService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => _ = RecordAsync(e, false, "Start");

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => _ = RecordAsync(e, false, "Progress");

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e) => _ = RecordAsync(e, e.PlayedToCompletion, "Stop");

    private async Task RecordAsync(PlaybackProgressEventArgs e, bool playedToCompletion, string eventKind)
    {
        if (e.Item is null || e.Users is null)
        {
            return;
        }

        foreach (var user in e.Users)
        {
            try
            {
                var update = new PlaybackUpdate(
                    user.Id,
                    e.Item.Id,
                    e.Item.GetClientTypeName(),
                    string.IsNullOrWhiteSpace(e.PlaySessionId) ? e.Session?.Id ?? string.Empty : e.PlaySessionId,
                    DateTimeOffset.UtcNow,
                    e.PlaybackPositionTicks,
                    e.Item.RunTimeTicks,
                    playedToCompletion,
                    EventKind: eventKind);

                switch (eventKind)
                {
                    case "Start":
                        await _historyService.RecordStartAsync(update, CancellationToken.None).ConfigureAwait(false);
                        break;
                    case "Stop":
                        await _historyService.RecordStopAsync(update, CancellationToken.None).ConfigureAwait(false);
                        break;
                    default:
                        await _historyService.RecordProgressAsync(update, CancellationToken.None).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record playback {EventKind} for item {ItemId}.", eventKind, e.Item.Id);
            }
        }
    }
}
