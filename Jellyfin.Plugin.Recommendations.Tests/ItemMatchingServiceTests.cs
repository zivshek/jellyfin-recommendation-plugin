using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using Jellyfin.Plugin.Recommendations.Services;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class ItemMatchingServiceTests
{
    [Fact]
    public async Task MatchDoubanAttachesExactTitleYearRatingToLibraryItem()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ItemMatchingService(repository);
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertLibraryItemAsync(
            new LibraryItem(itemId, "Farewell My Concubine", null, 1993, "Movie", [], [], [], null, 8.7, null, null, null, true, now),
            CancellationToken.None);
        await repository.UpsertDoubanItemAsync(
            new DoubanItem("1291546", "Farewell My Concubine", null, 1993, "Movie", "看过", 5, [], "classic", now, now, "test"),
            CancellationToken.None);

        var result = await service.MatchDoubanAsync(userId, CancellationToken.None);
        var matches = await repository.GetItemMatchesAsync("douban", CancellationToken.None);
        var ratings = await repository.GetExternalRatingsAsync(userId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Single(matches);
        Assert.False(matches[0].RequiresReview);
        Assert.Single(ratings);
        Assert.Equal(itemId, ratings[0].ItemId);
        Assert.Equal(10, ratings[0].Rating);
    }

    [Fact]
    public async Task MatchDoubanPrefersProviderIdExactMatch()
    {
        var repository = await CreateRepositoryAsync();
        var service = new ItemMatchingService(repository);
        var userId = Guid.NewGuid();
        var expectedItemId = Guid.NewGuid();
        var similarlyNamedItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertLibraryItemAsync(
            new LibraryItem(expectedItemId, "Different Local Title", null, 1994, "Movie", [], [], [], null, 9.0, "tt0111161", "278", null, true, now),
            CancellationToken.None);
        await repository.UpsertLibraryItemAsync(
            new LibraryItem(similarlyNamedItemId, "The Shawshank Redemption", null, 1994, "Movie", [], [], [], null, 8.0, null, null, null, true, now),
            CancellationToken.None);
        await repository.UpsertDoubanItemAsync(
            new DoubanItem("1292052", "The Shawshank Redemption", null, 1994, "Movie", "watched", 5, [], "classic", now, now, "test", "tt0111161", null, null),
            CancellationToken.None);

        var result = await service.MatchDoubanAsync(userId, CancellationToken.None);
        var matches = await repository.GetItemMatchesAsync("douban", CancellationToken.None);
        var ratings = await repository.GetExternalRatingsAsync(userId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Single(matches);
        Assert.Equal(expectedItemId, matches[0].ItemId);
        Assert.Equal("imdb-id", matches[0].MatchMethod);
        Assert.Single(ratings);
        Assert.Equal(expectedItemId, ratings[0].ItemId);
    }

    private static async Task<RecommendationRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);
        return repository;
    }
}
