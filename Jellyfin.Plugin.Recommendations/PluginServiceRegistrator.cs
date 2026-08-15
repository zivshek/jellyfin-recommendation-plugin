using System.IO;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Recommendations;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IRecommendationRepository>(serviceProvider =>
        {
            var paths = serviceProvider.GetRequiredService<IApplicationPaths>();
            var dataPath = Path.Combine(paths.DataPath, "recommendations");
            return new RecommendationRepository(Path.Combine(dataPath, "recommendations.db"));
        });

        serviceCollection.AddSingleton<PlaybackHistoryService>();
        serviceCollection.AddSingleton<DoubanCsvImportService>();
        serviceCollection.AddSingleton<IDoubanImportService>(serviceProvider => serviceProvider.GetRequiredService<DoubanCsvImportService>());
        serviceCollection.AddSingleton<RecommendationEngine>();
        serviceCollection.AddSingleton<RecommendationValidator>();
        serviceCollection.AddSingleton<LibraryIndexService>();
        serviceCollection.AddSingleton<ItemMatchingService>();
        serviceCollection.AddSingleton<UserDataSyncService>();
        serviceCollection.AddSingleton<RecommendationOrchestrator>();
        serviceCollection.AddSingleton<ManagedCollectionService>();
        serviceCollection.AddSingleton<ILlmClient, OpenAiCompatibleLlmClient>();
        serviceCollection.AddHostedService<DatabaseInitializationService>();
        serviceCollection.AddHostedService<JellyfinPlaybackMonitor>();
        serviceCollection.AddHostedService<UserDataMonitor>();
        serviceCollection.AddSingleton<IScheduledTask, RecommendationRefreshTask>();
    }
}
