using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Recommendations.Data;

/// <summary>
/// SQLite-backed repository for plugin state.
/// </summary>
public sealed class RecommendationRepository : IRecommendationRepository
{
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _databasePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationRepository"/> class.
    /// </summary>
    /// <param name="databasePath">SQLite database path.</param>
    public RecommendationRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, SchemaSql, cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "DoubanItems", "ImdbId", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "DoubanItems", "TmdbId", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "DoubanItems", "TvdbId", "TEXT NULL", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DoubanItems_ProviderIds ON DoubanItems (ImdbId, TmdbId, TvdbId);", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            "INSERT OR REPLACE INTO SchemaMigrations (Version, AppliedAt) VALUES ($version, $appliedAt);",
            cancellationToken,
            ("$version", CurrentSchemaVersion),
            ("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> AddPlaybackEventAsync(PlaybackEvent playbackEvent, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PlaybackEvents
                (UserId, ItemId, MediaType, SessionId, StartedAt, StoppedAt, LastPositionTicks, PlayedPercentage, Finished, Abandoned, EventKind)
            VALUES
                ($userId, $itemId, $mediaType, $sessionId, $startedAt, $stoppedAt, $lastPositionTicks, $playedPercentage, $finished, $abandoned, $eventKind);
            SELECT last_insert_rowid();
            """;
        AddCommonParameters(command, playbackEvent.UserId, playbackEvent.ItemId, playbackEvent.MediaType);
        command.Parameters.AddWithValue("$sessionId", playbackEvent.SessionId);
        command.Parameters.AddWithValue("$startedAt", ToDb(playbackEvent.StartedAt));
        command.Parameters.AddWithValue("$stoppedAt", ToDb(playbackEvent.StoppedAt));
        command.Parameters.AddWithValue("$lastPositionTicks", ToDb(playbackEvent.LastPositionTicks));
        command.Parameters.AddWithValue("$playedPercentage", ToDb(playbackEvent.PlayedPercentage));
        command.Parameters.AddWithValue("$finished", playbackEvent.Finished ? 1 : 0);
        command.Parameters.AddWithValue("$abandoned", playbackEvent.Abandoned ? 1 : 0);
        command.Parameters.AddWithValue("$eventKind", playbackEvent.EventKind);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task UpsertUserItemStatsAsync(UserItemStats stats, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UserItemStats
                (UserId, ItemId, MediaType, FirstPlayedAt, LastPlayedAt, PlayCount, PlayedPercentage, Finished, Abandoned,
                 JellyfinRating, Likes, IsFavorite, Played, UpdatedAt)
            VALUES
                ($userId, $itemId, $mediaType, $firstPlayedAt, $lastPlayedAt, $playCount, $playedPercentage, $finished, $abandoned,
                 $jellyfinRating, $likes, $isFavorite, $played, $updatedAt)
            ON CONFLICT(UserId, ItemId) DO UPDATE SET
                MediaType = excluded.MediaType,
                FirstPlayedAt = COALESCE(UserItemStats.FirstPlayedAt, excluded.FirstPlayedAt),
                LastPlayedAt = excluded.LastPlayedAt,
                PlayCount = excluded.PlayCount,
                PlayedPercentage = excluded.PlayedPercentage,
                Finished = excluded.Finished,
                Abandoned = excluded.Abandoned,
                JellyfinRating = excluded.JellyfinRating,
                Likes = excluded.Likes,
                IsFavorite = excluded.IsFavorite,
                Played = excluded.Played,
                UpdatedAt = excluded.UpdatedAt;
            """;
        AddStatsParameters(command, stats);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserItemStats?> GetUserItemStatsAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UserItemStats WHERE UserId = $userId AND ItemId = $itemId;";
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        command.Parameters.AddWithValue("$itemId", itemId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadUserItemStats(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserItemStats>> GetUserItemStatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM UserItemStats WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        return await ReadListAsync(command, ReadUserItemStats, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertLibraryItemAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LibraryItems
                (ItemId, Name, OriginalTitle, Year, MediaType, Genres, People, Studios, Overview, CommunityRating, ImdbId, TmdbId, TvdbId, IsPlayable, UpdatedAt)
            VALUES
                ($itemId, $name, $originalTitle, $year, $mediaType, $genres, $people, $studios, $overview, $communityRating, $imdbId, $tmdbId, $tvdbId, $isPlayable, $updatedAt)
            ON CONFLICT(ItemId) DO UPDATE SET
                Name = excluded.Name,
                OriginalTitle = excluded.OriginalTitle,
                Year = excluded.Year,
                MediaType = excluded.MediaType,
                Genres = excluded.Genres,
                People = excluded.People,
                Studios = excluded.Studios,
                Overview = excluded.Overview,
                CommunityRating = excluded.CommunityRating,
                ImdbId = excluded.ImdbId,
                TmdbId = excluded.TmdbId,
                TvdbId = excluded.TvdbId,
                IsPlayable = excluded.IsPlayable,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$itemId", item.ItemId.ToString("N"));
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$originalTitle", ToDb(item.OriginalTitle));
        command.Parameters.AddWithValue("$year", ToDb(item.Year));
        command.Parameters.AddWithValue("$mediaType", item.MediaType);
        command.Parameters.AddWithValue("$genres", Serialize(item.Genres));
        command.Parameters.AddWithValue("$people", Serialize(item.People));
        command.Parameters.AddWithValue("$studios", Serialize(item.Studios));
        command.Parameters.AddWithValue("$overview", ToDb(item.Overview));
        command.Parameters.AddWithValue("$communityRating", ToDb(item.CommunityRating));
        command.Parameters.AddWithValue("$imdbId", ToDb(item.ImdbId));
        command.Parameters.AddWithValue("$tmdbId", ToDb(item.TmdbId));
        command.Parameters.AddWithValue("$tvdbId", ToDb(item.TvdbId));
        command.Parameters.AddWithValue("$isPlayable", item.IsPlayable ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToDb(item.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItem>> GetLibraryItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM LibraryItems WHERE IsPlayable = 1 ORDER BY Name COLLATE NOCASE;";
        return await ReadListAsync(command, ReadLibraryItem, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LibraryItem?> GetLibraryItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM LibraryItems WHERE ItemId = $itemId;";
        command.Parameters.AddWithValue("$itemId", itemId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadLibraryItem(reader) : null;
    }

    /// <inheritdoc />
    public async Task UpsertDoubanItemAsync(DoubanItem item, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DoubanItems
                (DoubanSubjectId, Title, OriginalTitle, Year, MediaType, UserStatus, UserRating, UserTags, UserComment, MarkedAt, UpdatedAt, Source, ImdbId, TmdbId, TvdbId)
            VALUES
                ($doubanSubjectId, $title, $originalTitle, $year, $mediaType, $userStatus, $userRating, $userTags, $userComment, $markedAt, $updatedAt, $source, $imdbId, $tmdbId, $tvdbId)
            ON CONFLICT(DoubanSubjectId) DO UPDATE SET
                Title = excluded.Title,
                OriginalTitle = excluded.OriginalTitle,
                Year = excluded.Year,
                MediaType = excluded.MediaType,
                UserStatus = excluded.UserStatus,
                UserRating = excluded.UserRating,
                UserTags = excluded.UserTags,
                UserComment = excluded.UserComment,
                MarkedAt = excluded.MarkedAt,
                UpdatedAt = excluded.UpdatedAt,
                Source = excluded.Source,
                ImdbId = excluded.ImdbId,
                TmdbId = excluded.TmdbId,
                TvdbId = excluded.TvdbId;
            """;
        command.Parameters.AddWithValue("$doubanSubjectId", item.DoubanSubjectId);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$originalTitle", ToDb(item.OriginalTitle));
        command.Parameters.AddWithValue("$year", ToDb(item.Year));
        command.Parameters.AddWithValue("$mediaType", item.MediaType);
        command.Parameters.AddWithValue("$userStatus", item.UserStatus);
        command.Parameters.AddWithValue("$userRating", ToDb(item.UserRating));
        command.Parameters.AddWithValue("$userTags", Serialize(item.UserTags));
        command.Parameters.AddWithValue("$userComment", ToDb(item.UserComment));
        command.Parameters.AddWithValue("$markedAt", ToDb(item.MarkedAt));
        command.Parameters.AddWithValue("$updatedAt", ToDb(item.UpdatedAt));
        command.Parameters.AddWithValue("$source", item.Source);
        command.Parameters.AddWithValue("$imdbId", ToDb(item.ImdbId));
        command.Parameters.AddWithValue("$tmdbId", ToDb(item.TmdbId));
        command.Parameters.AddWithValue("$tvdbId", ToDb(item.TvdbId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoubanItem>> GetDoubanItemsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DoubanItems ORDER BY UpdatedAt DESC;";
        return await ReadListAsync(command, ReadDoubanItem, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertExternalRatingAsync(ExternalRating rating, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExternalRatings
                (UserId, Provider, ExternalId, ItemId, Rating, Status, Comment, UpdatedAt)
            VALUES
                ($userId, $provider, $externalId, $itemId, $rating, $status, $comment, $updatedAt)
            ON CONFLICT(UserId, Provider, ExternalId) DO UPDATE SET
                ItemId = excluded.ItemId,
                Rating = excluded.Rating,
                Status = excluded.Status,
                Comment = excluded.Comment,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$userId", rating.UserId.ToString("N"));
        command.Parameters.AddWithValue("$provider", rating.Provider);
        command.Parameters.AddWithValue("$externalId", rating.ExternalId);
        command.Parameters.AddWithValue("$itemId", ToDb(rating.ItemId));
        command.Parameters.AddWithValue("$rating", ToDb(rating.Rating));
        command.Parameters.AddWithValue("$status", ToDb(rating.Status));
        command.Parameters.AddWithValue("$comment", ToDb(rating.Comment));
        command.Parameters.AddWithValue("$updatedAt", ToDb(rating.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalRating>> GetExternalRatingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ExternalRatings WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        return await ReadListAsync(command, ReadExternalRating, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertItemMatchAsync(ItemMatch match, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ItemMatches
                (Provider, ExternalId, ItemId, MatchMethod, Confidence, RequiresReview, UpdatedAt)
            VALUES
                ($provider, $externalId, $itemId, $matchMethod, $confidence, $requiresReview, $updatedAt)
            ON CONFLICT(Provider, ExternalId, ItemId) DO UPDATE SET
                MatchMethod = excluded.MatchMethod,
                Confidence = excluded.Confidence,
                RequiresReview = excluded.RequiresReview,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$provider", match.Provider);
        command.Parameters.AddWithValue("$externalId", match.ExternalId);
        command.Parameters.AddWithValue("$itemId", match.ItemId.ToString("N"));
        command.Parameters.AddWithValue("$matchMethod", match.MatchMethod);
        command.Parameters.AddWithValue("$confidence", match.Confidence);
        command.Parameters.AddWithValue("$requiresReview", match.RequiresReview ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToDb(match.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMatch>> GetItemMatchesAsync(string provider, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ItemMatches WHERE Provider = $provider;";
        command.Parameters.AddWithValue("$provider", provider);
        return await ReadListAsync(command, ReadItemMatch, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> AddRecommendationRunAsync(RecommendationRun run, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RecommendationRuns
                (UserId, InputHash, Provider, Model, Status, ErrorMessage, CreatedAt)
            VALUES
                ($userId, $inputHash, $provider, $model, $status, $errorMessage, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$userId", run.UserId.ToString("N"));
        command.Parameters.AddWithValue("$inputHash", run.InputHash);
        command.Parameters.AddWithValue("$provider", run.Provider);
        command.Parameters.AddWithValue("$model", run.Model);
        command.Parameters.AddWithValue("$status", run.Status);
        command.Parameters.AddWithValue("$errorMessage", ToDb(run.ErrorMessage));
        command.Parameters.AddWithValue("$createdAt", ToDb(run.CreatedAt));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task AddRecommendationItemsAsync(long runId, IReadOnlyList<ValidatedRecommendation> items, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO RecommendationItems
                    (RunId, ItemId, Rank, Reason, Confidence, Source)
                VALUES
                    ($runId, $itemId, $rank, $reason, $confidence, $source);
                """;
            command.Parameters.AddWithValue("$runId", runId);
            command.Parameters.AddWithValue("$itemId", item.ItemId.ToString("N"));
            command.Parameters.AddWithValue("$rank", item.Rank);
            command.Parameters.AddWithValue("$reason", item.Reason);
            command.Parameters.AddWithValue("$confidence", item.Confidence);
            command.Parameters.AddWithValue("$source", item.Source);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecommendationItem>> GetLatestRecommendationItemsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.*
            FROM RecommendationItems i
            JOIN RecommendationRuns r ON r.Id = i.RunId
            WHERE r.UserId = $userId
              AND r.Id = (SELECT Id FROM RecommendationRuns WHERE UserId = $userId AND Status = 'Succeeded' ORDER BY CreatedAt DESC LIMIT 1)
            ORDER BY i.Rank;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        return await ReadListAsync(command, ReadRecommendationItem, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertManagedCollectionAsync(ManagedCollection collection, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ManagedCollections (UserId, CollectionId, Name, UpdatedAt)
            VALUES ($userId, $collectionId, $name, $updatedAt)
            ON CONFLICT(UserId) DO UPDATE SET
                CollectionId = excluded.CollectionId,
                Name = excluded.Name,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$userId", collection.UserId.ToString("N"));
        command.Parameters.AddWithValue("$collectionId", collection.CollectionId.ToString("N"));
        command.Parameters.AddWithValue("$name", collection.Name);
        command.Parameters.AddWithValue("$updatedAt", ToDb(collection.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ManagedCollection?> GetManagedCollectionAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ManagedCollections WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadManagedCollection(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetManagedCollectionItemIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemId FROM ManagedCollectionItems WHERE UserId = $userId ORDER BY Rank;";
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        var items = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ParseGuid(reader["ItemId"]));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task ReplaceManagedCollectionItemIdsAsync(Guid userId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ManagedCollectionItems WHERE UserId = $userId;";
            deleteCommand.Parameters.AddWithValue("$userId", userId.ToString("N"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < itemIds.Count; i++)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO ManagedCollectionItems (UserId, ItemId, Rank, UpdatedAt) VALUES ($userId, $itemId, $rank, $updatedAt);";
            insertCommand.Parameters.AddWithValue("$userId", userId.ToString("N"));
            insertCommand.Parameters.AddWithValue("$itemId", itemIds[i].ToString("N"));
            insertCommand.Parameters.AddWithValue("$rank", i + 1);
            insertCommand.Parameters.AddWithValue("$updatedAt", ToDb(DateTimeOffset.UtcNow));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PluginStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var libraryCount = await CountAsync(connection, "LibraryItems", cancellationToken).ConfigureAwait(false);
        var doubanCount = await CountAsync(connection, "DoubanItems", cancellationToken).ConfigureAwait(false);
        var runCount = await CountAsync(connection, "RecommendationRuns", cancellationToken).ConfigureAwait(false);
        var lastLibraryIndexedAt = await MaxDateAsync(connection, "LibraryItems", "UpdatedAt", cancellationToken).ConfigureAwait(false);
        var lastDoubanImportAt = await MaxDateAsync(connection, "DoubanItems", "UpdatedAt", cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status, ErrorMessage, CreatedAt FROM RecommendationRuns ORDER BY CreatedAt DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new PluginStatus(libraryCount, doubanCount, runCount, lastLibraryIndexedAt, lastDoubanImportAt, null, null, null);
        }

        return new PluginStatus(
            libraryCount,
            doubanCount,
            runCount,
            lastLibraryIndexedAt,
            lastDoubanImportAt,
            FromDbDate(reader["CreatedAt"]),
            reader.GetString(reader.GetOrdinal("Status")),
            FromDbString(reader["ErrorMessage"]));
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string definition, CancellationToken cancellationToken)
    {
        await using var tableInfoCommand = connection.CreateCommand();
        tableInfoCommand.CommandText = string.Create(CultureInfo.InvariantCulture, $"PRAGMA table_info({tableName});");
        await using var reader = await tableInfoCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(Convert.ToString(reader["name"], CultureInfo.InvariantCulture), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await ExecuteAsync(
            connection,
            string.Create(CultureInfo.InvariantCulture, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};"),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM {tableName};");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<DateTimeOffset?> MaxDateAsync(SqliteConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Create(CultureInfo.InvariantCulture, $"SELECT MAX({columnName}) FROM {tableName};");
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : FromDbDate(value);
    }

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(SqliteCommand command, Func<SqliteDataReader, T> read, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(read(reader));
        }

        return items;
    }

    private static void AddCommonParameters(SqliteCommand command, Guid userId, Guid itemId, string mediaType)
    {
        command.Parameters.AddWithValue("$userId", userId.ToString("N"));
        command.Parameters.AddWithValue("$itemId", itemId.ToString("N"));
        command.Parameters.AddWithValue("$mediaType", mediaType);
    }

    private static void AddStatsParameters(SqliteCommand command, UserItemStats stats)
    {
        AddCommonParameters(command, stats.UserId, stats.ItemId, stats.MediaType);
        command.Parameters.AddWithValue("$firstPlayedAt", ToDb(stats.FirstPlayedAt));
        command.Parameters.AddWithValue("$lastPlayedAt", ToDb(stats.LastPlayedAt));
        command.Parameters.AddWithValue("$playCount", stats.PlayCount);
        command.Parameters.AddWithValue("$playedPercentage", stats.PlayedPercentage);
        command.Parameters.AddWithValue("$finished", stats.Finished ? 1 : 0);
        command.Parameters.AddWithValue("$abandoned", stats.Abandoned ? 1 : 0);
        command.Parameters.AddWithValue("$jellyfinRating", ToDb(stats.JellyfinRating));
        command.Parameters.AddWithValue("$likes", ToDb(stats.Likes));
        command.Parameters.AddWithValue("$isFavorite", stats.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$played", stats.Played ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", ToDb(stats.UpdatedAt));
    }

    private static UserItemStats ReadUserItemStats(SqliteDataReader reader)
    {
        return new UserItemStats(
            ParseGuid(reader["UserId"]),
            ParseGuid(reader["ItemId"]),
            reader.GetString(reader.GetOrdinal("MediaType")),
            FromDbDate(reader["FirstPlayedAt"]),
            FromDbDate(reader["LastPlayedAt"]),
            reader.GetInt32(reader.GetOrdinal("PlayCount")),
            reader.GetDouble(reader.GetOrdinal("PlayedPercentage")),
            reader.GetInt32(reader.GetOrdinal("Finished")) == 1,
            reader.GetInt32(reader.GetOrdinal("Abandoned")) == 1,
            FromDbDouble(reader["JellyfinRating"]),
            FromDbBool(reader["Likes"]),
            reader.GetInt32(reader.GetOrdinal("IsFavorite")) == 1,
            reader.GetInt32(reader.GetOrdinal("Played")) == 1,
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch);
    }

    private static LibraryItem ReadLibraryItem(SqliteDataReader reader)
    {
        return new LibraryItem(
            ParseGuid(reader["ItemId"]),
            reader.GetString(reader.GetOrdinal("Name")),
            FromDbString(reader["OriginalTitle"]),
            FromDbInt(reader["Year"]),
            reader.GetString(reader.GetOrdinal("MediaType")),
            DeserializeList(reader.GetString(reader.GetOrdinal("Genres"))),
            DeserializeList(reader.GetString(reader.GetOrdinal("People"))),
            DeserializeList(reader.GetString(reader.GetOrdinal("Studios"))),
            FromDbString(reader["Overview"]),
            FromDbDouble(reader["CommunityRating"]),
            FromDbString(reader["ImdbId"]),
            FromDbString(reader["TmdbId"]),
            FromDbString(reader["TvdbId"]),
            reader.GetInt32(reader.GetOrdinal("IsPlayable")) == 1,
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch);
    }

    private static DoubanItem ReadDoubanItem(SqliteDataReader reader)
    {
        return new DoubanItem(
            reader.GetString(reader.GetOrdinal("DoubanSubjectId")),
            reader.GetString(reader.GetOrdinal("Title")),
            FromDbString(reader["OriginalTitle"]),
            FromDbInt(reader["Year"]),
            reader.GetString(reader.GetOrdinal("MediaType")),
            reader.GetString(reader.GetOrdinal("UserStatus")),
            FromDbInt(reader["UserRating"]),
            DeserializeList(reader.GetString(reader.GetOrdinal("UserTags"))),
            FromDbString(reader["UserComment"]),
            FromDbDate(reader["MarkedAt"]),
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch,
            reader.GetString(reader.GetOrdinal("Source")),
            FromDbString(reader["ImdbId"]),
            FromDbString(reader["TmdbId"]),
            FromDbString(reader["TvdbId"]));
    }

    private static ExternalRating ReadExternalRating(SqliteDataReader reader)
    {
        return new ExternalRating(
            reader.GetInt64(reader.GetOrdinal("Id")),
            ParseGuid(reader["UserId"]),
            reader.GetString(reader.GetOrdinal("Provider")),
            reader.GetString(reader.GetOrdinal("ExternalId")),
            FromDbGuid(reader["ItemId"]),
            FromDbDouble(reader["Rating"]),
            FromDbString(reader["Status"]),
            FromDbString(reader["Comment"]),
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch);
    }

    private static ItemMatch ReadItemMatch(SqliteDataReader reader)
    {
        return new ItemMatch(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetString(reader.GetOrdinal("Provider")),
            reader.GetString(reader.GetOrdinal("ExternalId")),
            ParseGuid(reader["ItemId"]),
            reader.GetString(reader.GetOrdinal("MatchMethod")),
            reader.GetDouble(reader.GetOrdinal("Confidence")),
            reader.GetInt32(reader.GetOrdinal("RequiresReview")) == 1,
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch);
    }

    private static RecommendationItem ReadRecommendationItem(SqliteDataReader reader)
    {
        return new RecommendationItem(
            reader.GetInt64(reader.GetOrdinal("Id")),
            reader.GetInt64(reader.GetOrdinal("RunId")),
            ParseGuid(reader["ItemId"]),
            reader.GetInt32(reader.GetOrdinal("Rank")),
            reader.GetString(reader.GetOrdinal("Reason")),
            reader.GetDouble(reader.GetOrdinal("Confidence")),
            reader.GetString(reader.GetOrdinal("Source")));
    }

    private static ManagedCollection ReadManagedCollection(SqliteDataReader reader)
    {
        return new ManagedCollection(
            ParseGuid(reader["UserId"]),
            ParseGuid(reader["CollectionId"]),
            reader.GetString(reader.GetOrdinal("Name")),
            FromDbDate(reader["UpdatedAt"]) ?? DateTimeOffset.UnixEpoch);
    }

    private static string Serialize(IReadOnlyList<string> value) => JsonSerializer.Serialize(value, JsonOptions);

    private static IReadOnlyList<string> DeserializeList(string value)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<string>>(value, JsonOptions) ?? [];
    }

    private static object ToDb(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object ToDb(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDb(long? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDb(double? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDb(bool? value) => value.HasValue ? value.Value ? 1 : 0 : DBNull.Value;

    private static object ToDb(Guid? value) => value.HasValue ? value.Value.ToString("N") : DBNull.Value;

    private static object ToDb(DateTimeOffset? value) => value.HasValue ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;

    private static string? FromDbString(object value) => value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static int? FromDbInt(object value) => value is DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static double? FromDbDouble(object value) => value is DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static bool? FromDbBool(object value) => value is DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;

    private static DateTimeOffset? FromDbDate(object value)
    {
        var text = FromDbString(value);
        return string.IsNullOrWhiteSpace(text) ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static Guid ParseGuid(object value) => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

    private static Guid? FromDbGuid(object value)
    {
        var text = FromDbString(value);
        return string.IsNullOrWhiteSpace(text) ? null : Guid.Parse(text);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS SchemaMigrations (
            Version INTEGER PRIMARY KEY,
            AppliedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS PlaybackEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NOT NULL,
            ItemId TEXT NOT NULL,
            MediaType TEXT NOT NULL,
            SessionId TEXT NOT NULL,
            StartedAt TEXT NOT NULL,
            StoppedAt TEXT NULL,
            LastPositionTicks INTEGER NULL,
            PlayedPercentage REAL NULL,
            Finished INTEGER NOT NULL DEFAULT 0,
            Abandoned INTEGER NOT NULL DEFAULT 0,
            EventKind TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_PlaybackEvents_UserItem ON PlaybackEvents (UserId, ItemId);
        CREATE INDEX IF NOT EXISTS IX_PlaybackEvents_Session ON PlaybackEvents (SessionId);

        CREATE TABLE IF NOT EXISTS UserItemStats (
            UserId TEXT NOT NULL,
            ItemId TEXT NOT NULL,
            MediaType TEXT NOT NULL,
            FirstPlayedAt TEXT NULL,
            LastPlayedAt TEXT NULL,
            PlayCount INTEGER NOT NULL DEFAULT 0,
            PlayedPercentage REAL NOT NULL DEFAULT 0,
            Finished INTEGER NOT NULL DEFAULT 0,
            Abandoned INTEGER NOT NULL DEFAULT 0,
            JellyfinRating REAL NULL,
            Likes INTEGER NULL,
            IsFavorite INTEGER NOT NULL DEFAULT 0,
            Played INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (UserId, ItemId)
        );

        CREATE TABLE IF NOT EXISTS LibraryItems (
            ItemId TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            OriginalTitle TEXT NULL,
            Year INTEGER NULL,
            MediaType TEXT NOT NULL,
            Genres TEXT NOT NULL,
            People TEXT NOT NULL,
            Studios TEXT NOT NULL,
            Overview TEXT NULL,
            CommunityRating REAL NULL,
            ImdbId TEXT NULL,
            TmdbId TEXT NULL,
            TvdbId TEXT NULL,
            IsPlayable INTEGER NOT NULL DEFAULT 1,
            UpdatedAt TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_LibraryItems_ProviderIds ON LibraryItems (ImdbId, TmdbId, TvdbId);
        CREATE INDEX IF NOT EXISTS IX_LibraryItems_Title ON LibraryItems (Name, Year, MediaType);

        CREATE TABLE IF NOT EXISTS DoubanItems (
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
            Source TEXT NOT NULL,
            ImdbId TEXT NULL,
            TmdbId TEXT NULL,
            TvdbId TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS ExternalRatings (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NOT NULL,
            Provider TEXT NOT NULL,
            ExternalId TEXT NOT NULL,
            ItemId TEXT NULL,
            Rating REAL NULL,
            Status TEXT NULL,
            Comment TEXT NULL,
            UpdatedAt TEXT NOT NULL,
            UNIQUE (UserId, Provider, ExternalId)
        );

        CREATE TABLE IF NOT EXISTS ItemMatches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Provider TEXT NOT NULL,
            ExternalId TEXT NOT NULL,
            ItemId TEXT NOT NULL,
            MatchMethod TEXT NOT NULL,
            Confidence REAL NOT NULL,
            RequiresReview INTEGER NOT NULL DEFAULT 0,
            UpdatedAt TEXT NOT NULL,
            UNIQUE (Provider, ExternalId, ItemId)
        );

        CREATE TABLE IF NOT EXISTS RecommendationRuns (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NOT NULL,
            InputHash TEXT NOT NULL,
            Provider TEXT NOT NULL,
            Model TEXT NOT NULL,
            Status TEXT NOT NULL,
            ErrorMessage TEXT NULL,
            CreatedAt TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_RecommendationRuns_UserCreated ON RecommendationRuns (UserId, CreatedAt);

        CREATE TABLE IF NOT EXISTS RecommendationItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            RunId INTEGER NOT NULL,
            ItemId TEXT NOT NULL,
            Rank INTEGER NOT NULL,
            Reason TEXT NOT NULL,
            Confidence REAL NOT NULL,
            Source TEXT NOT NULL,
            FOREIGN KEY (RunId) REFERENCES RecommendationRuns (Id) ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS IX_RecommendationItems_RunItem ON RecommendationItems (RunId, ItemId);

        CREATE TABLE IF NOT EXISTS ManagedCollections (
            UserId TEXT PRIMARY KEY,
            CollectionId TEXT NOT NULL,
            Name TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ManagedCollectionItems (
            UserId TEXT NOT NULL,
            ItemId TEXT NOT NULL,
            Rank INTEGER NOT NULL,
            UpdatedAt TEXT NOT NULL,
            PRIMARY KEY (UserId, ItemId)
        );
        """;
}
