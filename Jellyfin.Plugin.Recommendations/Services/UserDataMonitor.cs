using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Subscribes to Jellyfin user data changes and syncs explicit taste signals.
/// </summary>
public sealed class UserDataMonitor : IHostedService
{
    private readonly IUserDataManager _userDataManager;
    private readonly UserDataSyncService _syncService;
    private readonly ILogger<UserDataMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataMonitor"/> class.
    /// </summary>
    public UserDataMonitor(IUserDataManager userDataManager, UserDataSyncService syncService, ILogger<UserDataMonitor> logger)
    {
        _userDataManager = userDataManager;
        _syncService = syncService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e) => _ = SyncAsync(e);

    private async Task SyncAsync(UserDataSaveEventArgs e)
    {
        try
        {
            await _syncService.SyncUserDataAsync(e, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync user data for item {ItemId}.", e.Item?.Id);
        }
    }
}
