using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationRefreshTask"/> class.
    /// </summary>
    public RecommendationRefreshTask(
        IUserManager userManager,
        LibraryIndexService libraryIndexService,
        IDoubanImportService doubanImportService,
        ItemMatchingService itemMatchingService,
        RecommendationOrchestrator orchestrator,
        ManagedCollectionService collectionService)
    {
        _userManager = userManager;
        _libraryIndexService = libraryIndexService;
        _doubanImportService = doubanImportService;
        _itemMatchingService = itemMatchingService;
        _orchestrator = orchestrator;
        _collectionService = collectionService;
    }

    /// <inheritdoc />
    public string Name => "Refresh recommendations";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.Recommendations.Refresh";

    /// <inheritdoc />
    public string Description => "Rebuilds the local recommendation index, generates recommendations, and updates managed collections.";

    /// <inheritdoc />
    public string Category => "Recommendations";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        await _libraryIndexService.RebuildAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(20);
        var users = _userManager.GetUsers().ToArray();
        for (var i = 0; i < users.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDoubanEnabled(configuration) && !string.IsNullOrWhiteSpace(configuration.DoubanExportPath))
            {
                await _doubanImportService.ImportAsync(configuration.DoubanExportPath, users[i].Id, cancellationToken).ConfigureAwait(false);
                await _itemMatchingService.MatchDoubanAsync(users[i].Id, cancellationToken).ConfigureAwait(false);
            }

            await _orchestrator.GenerateAsync(users[i].Id, configuration, cancellationToken).ConfigureAwait(false);
            await _collectionService.UpdateCollectionAsync(users[i].Id, configuration, cancellationToken).ConfigureAwait(false);
            progress.Report(20 + (i + 1) / (double)Math.Max(1, users.Length) * 80);
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
}
