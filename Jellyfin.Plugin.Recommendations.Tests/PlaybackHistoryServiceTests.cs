using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Services;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class PlaybackHistoryServiceTests
{
    [Fact]
    public async Task StopAtHighPercentageMarksFinishedAndIncrementsPlayCount()
    {
        var repository = await CreateRepositoryAsync();
        var service = new PlaybackHistoryService(repository);
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await service.RecordStopAsync(
            new PlaybackUpdate(userId, itemId, "Movie", "session", DateTimeOffset.UtcNow, 95, 100, false),
            CancellationToken.None);

        var stats = await repository.GetUserItemStatsAsync(userId, itemId, CancellationToken.None);

        Assert.NotNull(stats);
        Assert.True(stats.Finished);
        Assert.False(stats.Abandoned);
        Assert.Equal(1, stats.PlayCount);
        Assert.True(stats.Played);
    }

    [Fact]
    public async Task EarlyStopMarksAbandonedWithoutCompletion()
    {
        var repository = await CreateRepositoryAsync();
        var service = new PlaybackHistoryService(repository);
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await service.RecordStopAsync(
            new PlaybackUpdate(userId, itemId, "Episode", "session", DateTimeOffset.UtcNow, 5, 100, false),
            CancellationToken.None);

        var stats = await repository.GetUserItemStatsAsync(userId, itemId, CancellationToken.None);

        Assert.NotNull(stats);
        Assert.False(stats.Finished);
        Assert.True(stats.Abandoned);
        Assert.Equal(0, stats.PlayCount);
        Assert.False(stats.Played);
    }

    private static async Task<RecommendationRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }
}
