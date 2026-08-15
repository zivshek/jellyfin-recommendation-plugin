using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Imports optional external Douban taste data into the local cache.
/// </summary>
public interface IDoubanImportService
{
    /// <summary>
    /// Imports a Douban export from disk.
    /// </summary>
    /// <param name="path">CSV or JSON export path.</param>
    /// <param name="userId">Jellyfin user ID associated with the imported taste signals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult> ImportAsync(string path, Guid userId, CancellationToken cancellationToken);
}
