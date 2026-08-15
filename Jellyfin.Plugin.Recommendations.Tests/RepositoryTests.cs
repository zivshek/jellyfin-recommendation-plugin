using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public async Task InitializeCreatesSchemaAndRoundTripsCoreEntities()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);

        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertLibraryItemAsync(
            new LibraryItem(
                itemId,
                "In the Mood for Love",
                "Fa yeung nin wah",
                2000,
                "Movie",
                ["Drama", "Romance"],
                ["Wong Kar-wai", "Tony Leung"],
                ["Block 2 Pictures"],
                "Two neighbors form a connection.",
                8.1,
                "tt0118694",
                "843",
                null,
                true,
                now),
            CancellationToken.None);

        await repository.UpsertUserItemStatsAsync(
            new UserItemStats(
                userId,
                itemId,
                "Movie",
                now.AddDays(-1),
                now,
                2,
                98.5,
                true,
                false,
                9,
                true,
                true,
                true,
                now),
            CancellationToken.None);

        await repository.UpsertDoubanItemAsync(
            new DoubanItem(
                "1291557",
                "花样年华",
                "In the Mood for Love",
                2000,
                "Movie",
                "看过",
                5,
                ["王家卫"],
                "loved it",
                now,
                now,
                "douban-skill-csv"),
            CancellationToken.None);

        await repository.UpsertExternalRatingAsync(
            new ExternalRating(0, userId, "douban", "1291557", itemId, 10, "看过", "loved it", now),
            CancellationToken.None);
        await repository.UpsertManagedCollectionAsync(new ManagedCollection(userId, Guid.NewGuid(), "Recommended For You", now), CancellationToken.None);
        await repository.ReplaceManagedCollectionItemIdsAsync(userId, [itemId], CancellationToken.None);

        var runId = await repository.AddRecommendationRunAsync(
            new RecommendationRun(0, userId, "hash", "deterministic", "fallback", "Succeeded", null, now),
            CancellationToken.None);

        await repository.AddRecommendationItemsAsync(
            runId,
            [new ValidatedRecommendation(itemId, 1, "Matches your high-rated romantic dramas.", 0.91, "deterministic")],
            CancellationToken.None);

        var libraryItem = await repository.GetLibraryItemAsync(itemId, CancellationToken.None);
        var stats = await repository.GetUserItemStatsAsync(userId, itemId, CancellationToken.None);
        var doubanItems = await repository.GetDoubanItemsAsync(CancellationToken.None);
        var ratings = await repository.GetExternalRatingsAsync(userId, CancellationToken.None);
        var recommendations = await repository.GetLatestRecommendationItemsAsync(userId, CancellationToken.None);
        var status = await repository.GetStatusAsync(CancellationToken.None);
        var managedItemIds = await repository.GetManagedCollectionItemIdsAsync(userId, CancellationToken.None);

        Assert.NotNull(libraryItem);
        Assert.Equal("tt0118694", libraryItem.ImdbId);
        Assert.Equal(["Drama", "Romance"], libraryItem.Genres);
        Assert.NotNull(stats);
        Assert.True(stats.Finished);
        Assert.Single(doubanItems);
        Assert.Single(ratings);
        Assert.Single(recommendations);
        Assert.Equal([itemId], managedItemIds);
        Assert.Equal("Succeeded", status.LastRecommendationStatus);
        Assert.Equal(1, status.LibraryItemCount);
        Assert.Equal(1, status.DoubanItemCount);
        Assert.NotNull(status.LastLibraryIndexedAt);
        Assert.NotNull(status.LastDoubanImportAt);
        Assert.NotNull(status.LastRecommendationRunAt);
    }

    [Fact]
    public async Task InitializeEnsuresDoubanProviderIdColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);

        await repository.InitializeAsync(CancellationToken.None);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(DoubanItems);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        Assert.Contains("ImdbId", columns);
        Assert.Contains("TmdbId", columns);
        Assert.Contains("TvdbId", columns);
    }

    [Fact]
    public async Task InitializeMigratesVersionOneDoubanTableBeforeCreatingProviderIndex()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE DoubanItems (
                    DoubanSubjectId TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    OriginalTitle TEXT NULL,
                    Year INTEGER NULL,
                    MediaType TEXT NOT NULL,
                    UserStatus TEXT NOT NULL,
                    UserRating INTEGER NULL,
                    UserTags TEXT NOT NULL,
                    UserComment TEXT NULL,
                    MarkedAt TEXT NULL,
                    UpdatedAt TEXT NOT NULL,
                    Source TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);

        await using var verifyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'IX_DoubanItems_ProviderIds';";

        Assert.Equal("IX_DoubanItems_ProviderIds", await verifyCommand.ExecuteScalarAsync());
    }
}
