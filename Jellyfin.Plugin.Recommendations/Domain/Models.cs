using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Recommendations.Domain;

/// <summary>
/// Durable playback event captured from Jellyfin playback callbacks.
/// </summary>
public sealed record PlaybackEvent(
    long Id,
    Guid UserId,
    Guid ItemId,
    string MediaType,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? StoppedAt,
    long? LastPositionTicks,
    double? PlayedPercentage,
    bool Finished,
    bool Abandoned,
    string EventKind);

/// <summary>
/// Aggregated per-user taste and watch state for one Jellyfin item.
/// </summary>
public sealed record UserItemStats(
    Guid UserId,
    Guid ItemId,
    string MediaType,
    DateTimeOffset? FirstPlayedAt,
    DateTimeOffset? LastPlayedAt,
    int PlayCount,
    double PlayedPercentage,
    bool Finished,
    bool Abandoned,
    double? JellyfinRating,
    bool? Likes,
    bool IsFavorite,
    bool Played,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Cached metadata for a recommendable Jellyfin library item.
/// </summary>
public sealed record LibraryItem(
    Guid ItemId,
    string Name,
    string? OriginalTitle,
    int? Year,
    string MediaType,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> People,
    IReadOnlyList<string> Studios,
    string? Overview,
    double? CommunityRating,
    string? ImdbId,
    string? TmdbId,
    string? TvdbId,
    bool IsPlayable,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Douban item imported from a cache, CSV export, RSS feed, or future native adapter.
/// </summary>
public sealed record DoubanItem(
    string DoubanSubjectId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string MediaType,
    string UserStatus,
    int? UserRating,
    IReadOnlyList<string> UserTags,
    string? UserComment,
    DateTimeOffset? MarkedAt,
    DateTimeOffset UpdatedAt,
    string Source,
    string? ImdbId = null,
    string? TmdbId = null,
    string? TvdbId = null);

/// <summary>
/// External rating signal that can influence a Jellyfin item or a taste profile.
/// </summary>
public sealed record ExternalRating(
    long Id,
    Guid UserId,
    string Provider,
    string ExternalId,
    Guid? ItemId,
    double? Rating,
    string? Status,
    string? Comment,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Match between a Douban subject and a Jellyfin item.
/// </summary>
public sealed record ItemMatch(
    long Id,
    string Provider,
    string ExternalId,
    Guid ItemId,
    string MatchMethod,
    double Confidence,
    bool RequiresReview,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Recommendation generation run metadata.
/// </summary>
public sealed record RecommendationRun(
    long Id,
    Guid UserId,
    string InputHash,
    string Provider,
    string Model,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

/// <summary>
/// One ordered recommendation item within a run.
/// </summary>
public sealed record RecommendationItem(
    long Id,
    long RunId,
    Guid ItemId,
    int Rank,
    string Reason,
    double Confidence,
    string Source);

/// <summary>
/// Plugin-managed Jellyfin collection for one user.
/// </summary>
public sealed record ManagedCollection(
    Guid UserId,
    Guid CollectionId,
    string Name,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Compact Jellyfin user-data signal used by synchronization code.
/// </summary>
public sealed record JellyfinUserSignal(
    Guid UserId,
    Guid ItemId,
    string MediaType,
    double? Rating,
    bool? Likes,
    bool IsFavorite,
    bool Played,
    int PlayCount,
    double PlayedPercentage,
    DateTimeOffset? LastPlayedDate);

/// <summary>
/// Candidate item plus user state used by ranking.
/// </summary>
public sealed record RecommendationCandidate(
    LibraryItem Item,
    UserItemStats? UserStats,
    IReadOnlyList<ExternalRating> ExternalRatings);

/// <summary>
/// A generated recommendation after validation against known candidates.
/// </summary>
public sealed record ValidatedRecommendation(
    Guid ItemId,
    int Rank,
    string Reason,
    double Confidence,
    string Source);

/// <summary>
/// High-level status shown by the admin page.
/// </summary>
public sealed record PluginStatus(
    int LibraryItemCount,
    int DoubanItemCount,
    int RecommendationRunCount,
    DateTimeOffset? LastLibraryIndexedAt,
    DateTimeOffset? LastDoubanImportAt,
    DateTimeOffset? LastRecommendationRunAt,
    string? LastRecommendationStatus,
    string? LastRecommendationError);

/// <summary>
/// Result of an import or manual action.
/// </summary>
public sealed record OperationResult(bool Success, string Message, int AffectedCount = 0);
