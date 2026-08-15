using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Scheduled task that refreshes recommendations for Jellyfin users.
/// </summary>
public sealed class RecommendationRefreshTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly LibraryIndexService _libraryIndexService;
    private readonly IDoubanImportService _doubanImportService;
    private readonly ItemMatchingService _itemMatchingService;
    private readonly RecommendationOrchestrator _orchestrator;
    private readonly ManagedCollectionService _collectionService;
    private readonly IPluginDiagnosticLog _diagnosticLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationRefreshTask"/> class.
    /// </summary>
    public RecommendationRefreshTask(
        IUserManager userManager,
        LibraryIndexService libraryIndexService,
        IDoubanImportService doubanImportService,
        ItemMatchingService itemMatchingService,
        RecommendationOrchestrator orchestrator,
        ManagedCollectionService collectionService,
        IPluginDiagnosticLog diagnosticLog)
    {
        _userManager = userManager;
        _libraryIndexService = libraryIndexService;
        _doubanImportService = doubanImportService;
        _itemMatchingService = itemMatchingService;
        _orchestrator = orchestrator;
        _collectionService = collectionService;
        _diagnosticLog = diagnosticLog;
    }

    /// <inheritdoc />
    public string Name => "Generate and update recommendations";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.Recommendations.Refresh";

    /// <inheritdoc />
    public string Description => "Rebuilds the local recommendation index, generates recommendations for each user, and updates managed collections.";

    /// <inheritdoc />
    public string Category => "Recommendations";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var users = _userManager.GetUsers().ToArray();
        await AppendLogSafeAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Scheduled refresh started: users={users.Length}; intervalHours={Math.Max(1, configuration.ScheduledRefreshIntervalHours)}."),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var rebuild = await _libraryIndexService.RebuildAsync(cancellationToken).ConfigureAwait(false);
            await AppendLogSafeAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Scheduled refresh rebuild completed: success={rebuild.Success}; affected={rebuild.AffectedCount}; message=\"{rebuild.Message}\"."),
                cancellationToken).ConfigureAwait(false);
            progress.Report(20);

            for (var i = 0; i < users.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var userId = users[i].Id;
                await AppendLogSafeAsync($"Scheduled refresh user started user={userId}.", cancellationToken).ConfigureAwait(false);
                if (IsDoubanEnabled(configuration) && !string.IsNullOrWhiteSpace(configuration.DoubanExportPath))
                {
                    var imported = await _doubanImportService.ImportAsync(configuration.DoubanExportPath, userId, cancellationToken).ConfigureAwait(false);
                    await AppendLogSafeAsync(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Scheduled refresh Douban import user={userId}: success={imported.Success}; affected={imported.AffectedCount}; message=\"{imported.Message}\"."),
                        cancellationToken).ConfigureAwait(false);

                    var matched = await _itemMatchingService.MatchDoubanAsync(userId, cancellationToken).ConfigureAwait(false);
                    await AppendLogSafeAsync(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Scheduled refresh Douban match user={userId}: success={matched.Success}; affected={matched.AffectedCount}; message=\"{matched.Message}\"."),
                        cancellationToken).ConfigureAwait(false);
                }

                var generated = await _orchestrator.GenerateAsync(userId, configuration, cancellationToken).ConfigureAwait(false);
                await AppendLogSafeAsync(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Scheduled refresh generate user={userId}: success={generated.Success}; affected={generated.AffectedCount}; message=\"{generated.Message}\"."),
                    cancellationToken).ConfigureAwait(false);

                var updated = await _collectionService.UpdateCollectionAsync(userId, configuration, cancellationToken).ConfigureAwait(false);
                await AppendLogSafeAsync(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Scheduled refresh collection update user={userId}: success={updated.Success}; affected={updated.AffectedCount}; message=\"{updated.Message}\"."),
                    cancellationToken).ConfigureAwait(false);

                progress.Report(20 + (i + 1) / (double)Math.Max(1, users.Length) * 80);
            }

            await AppendLogSafeAsync("Scheduled refresh completed.", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await AppendLogSafeAsync($"Scheduled refresh failed: {ex}", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(Math.Max(1, configuration.ScheduledRefreshIntervalHours)).Ticks
            }
        ];
    }

    private static bool IsDoubanEnabled(Configuration.PluginConfiguration configuration)
        => !string.Equals(configuration.DoubanSyncProvider, "disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configuration.DoubanSyncProvider, "none", StringComparison.OrdinalIgnoreCase);

    private async Task AppendLogSafeAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticLog.AppendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Scheduled task diagnostics should not alter refresh behavior.
        }
    }
}
