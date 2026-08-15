using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Initializes the SQLite database at Jellyfin startup.
/// </summary>
public sealed class DatabaseInitializationService : IHostedService
{
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializationService"/> class.
    /// </summary>
    /// <param name="repository">Recommendation repository.</param>
    public DatabaseInitializationService(IRecommendationRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => _repository.InitializeAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
