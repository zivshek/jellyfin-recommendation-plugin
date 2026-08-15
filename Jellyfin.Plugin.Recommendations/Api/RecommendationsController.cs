using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using Jellyfin.Plugin.Recommendations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Recommendations.Api;

/// <summary>
/// Admin API endpoints for recommendation plugin actions.
/// </summary>
[ApiController]
[Authorize]
[Route("Recommendations")]
public sealed class RecommendationsController : ControllerBase
{
    private readonly IRecommendationRepository _repository;
    private readonly LibraryIndexService _libraryIndexService;
    private readonly IDoubanImportService _doubanImportService;
    private readonly ItemMatchingService _itemMatchingService;
    private readonly RecommendationOrchestrator _orchestrator;
    private readonly ManagedCollectionService _collectionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationsController"/> class.
    /// </summary>
    public RecommendationsController(
        IRecommendationRepository repository,
        LibraryIndexService libraryIndexService,
        IDoubanImportService doubanImportService,
        ItemMatchingService itemMatchingService,
        RecommendationOrchestrator orchestrator,
        ManagedCollectionService collectionService)
    {
        _repository = repository;
        _libraryIndexService = libraryIndexService;
        _doubanImportService = doubanImportService;
        _itemMatchingService = itemMatchingService;
        _orchestrator = orchestrator;
        _collectionService = collectionService;
    }

    /// <summary>
    /// Gets plugin status.
    /// </summary>
    [HttpGet("Status")]
    public Task<PluginStatus> GetStatus(CancellationToken cancellationToken)
        => _repository.GetStatusAsync(cancellationToken);

    /// <summary>
    /// Rebuilds the local library candidate index.
    /// </summary>
    [HttpPost("RebuildIndex")]
    public Task<OperationResult> RebuildIndex(CancellationToken cancellationToken)
        => _libraryIndexService.RebuildAsync(cancellationToken);

    /// <summary>
    /// Imports the configured or requested Douban CSV path.
    /// </summary>
    [HttpPost("ImportDouban")]
    public Task<OperationResult> ImportDouban([FromBody] ImportDoubanRequest request, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var path = string.IsNullOrWhiteSpace(request.Path) ? configuration.DoubanExportPath : request.Path;
        var userId = ResolveUserId(request.UserId, configuration.TestUserId);
        return ImportAndMatchAsync(path, userId, cancellationToken);
    }

    /// <summary>
    /// Matches imported Douban items to Jellyfin items.
    /// </summary>
    [HttpPost("MatchDouban")]
    public Task<OperationResult> MatchDouban([FromBody] UserActionRequest request, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return _itemMatchingService.MatchDoubanAsync(ResolveUserId(request.UserId, configuration.TestUserId), cancellationToken);
    }

    /// <summary>
    /// Generates recommendations for a user.
    /// </summary>
    [HttpPost("Generate")]
    public Task<OperationResult> Generate([FromBody] UserActionRequest request, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var userId = ResolveUserId(request.UserId, configuration.TestUserId);
        return _orchestrator.GenerateAsync(userId, configuration, cancellationToken);
    }

    /// <summary>
    /// Updates the managed collection for a user.
    /// </summary>
    [HttpPost("UpdateCollection")]
    public Task<OperationResult> UpdateCollection([FromBody] UserActionRequest request, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var userId = ResolveUserId(request.UserId, configuration.TestUserId);
        return _collectionService.UpdateCollectionAsync(userId, configuration, cancellationToken);
    }

    /// <summary>
    /// Runs the full MVP refresh flow for one user.
    /// </summary>
    [HttpPost("Refresh")]
    public async Task<OperationResult> Refresh([FromBody] UserActionRequest request, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var userId = ResolveUserId(request.UserId, configuration.TestUserId);
        await _libraryIndexService.RebuildAsync(cancellationToken).ConfigureAwait(false);
        if (IsDoubanEnabled(configuration) && !string.IsNullOrWhiteSpace(configuration.DoubanExportPath))
        {
            await ImportAndMatchAsync(configuration.DoubanExportPath, userId, cancellationToken).ConfigureAwait(false);
        }

        var generated = await _orchestrator.GenerateAsync(userId, configuration, cancellationToken).ConfigureAwait(false);
        if (!generated.Success)
        {
            return generated;
        }

        return await _collectionService.UpdateCollectionAsync(userId, configuration, cancellationToken).ConfigureAwait(false);
    }

    private static Guid ResolveUserId(Guid? requestUserId, string configuredUserId)
    {
        if (requestUserId.HasValue && requestUserId.Value != Guid.Empty)
        {
            return requestUserId.Value;
        }

        return Guid.TryParse(configuredUserId, out var userId) ? userId : Guid.Empty;
    }

    private async Task<OperationResult> ImportAndMatchAsync(string path, Guid userId, CancellationToken cancellationToken)
    {
        var imported = await _doubanImportService.ImportAsync(path, userId, cancellationToken).ConfigureAwait(false);
        if (!imported.Success)
        {
            return imported;
        }

        await _itemMatchingService.MatchDoubanAsync(userId, cancellationToken).ConfigureAwait(false);
        return imported;
    }

    private static bool IsDoubanEnabled(Configuration.PluginConfiguration configuration)
        => !string.Equals(configuration.DoubanSyncProvider, "disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configuration.DoubanSyncProvider, "none", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Request body for Douban imports.
/// </summary>
public sealed record ImportDoubanRequest(Guid? UserId, string? Path);

/// <summary>
/// Request body for user-scoped manual actions.
/// </summary>
public sealed record UserActionRequest(Guid? UserId);
