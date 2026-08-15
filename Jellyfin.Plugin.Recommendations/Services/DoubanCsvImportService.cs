using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Imports Douban movie/TV CSV exports produced by douban-skill and compatible JSON exports.
/// </summary>
public sealed partial class DoubanCsvImportService : IDoubanImportService
{
    private const string SourceName = "douban-skill-csv";
    private const string JsonSourceName = "douban-json";
    private readonly IRecommendationRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubanCsvImportService"/> class.
    /// </summary>
    /// <param name="repository">Recommendation repository.</param>
    public DoubanCsvImportService(IRecommendationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Imports a Douban CSV or JSON export from disk.
    /// </summary>
    /// <param name="path">CSV or JSON path.</param>
    /// <param name="userId">Jellyfin user ID associated with the imported taste signals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OperationResult> ImportAsync(string path, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new OperationResult(false, "Douban export path does not exist.");
        }

        var text = await File.ReadAllTextAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken).ConfigureAwait(false);
        return Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || LooksLikeJson(text)
            ? await ImportJsonTextAsync(text, userId, cancellationToken).ConfigureAwait(false)
            : await ImportTextAsync(text, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports Douban CSV content.
    /// </summary>
    /// <param name="csv">CSV text.</param>
    /// <param name="userId">Jellyfin user ID associated with the imported taste signals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OperationResult> ImportTextAsync(string csv, Guid userId, CancellationToken cancellationToken)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0)
        {
            return new OperationResult(false, "Douban CSV is empty.");
        }

        var headers = rows[0].Select(NormalizeHeader).ToArray();
        var items = new List<ImportedDoubanItem>();
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = ToDictionary(headers, row);
            var title = values.GetValueOrDefault("title");
            var url = values.GetValueOrDefault("url");
            var subjectId = ExtractSubjectId(url);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(subjectId))
            {
                continue;
            }

            items.Add(new ImportedDoubanItem(
                subjectId,
                title,
                FirstValue(values, "originaltitle", "original_title"),
                ParseInt(FirstValue(values, "year")) ?? TryExtractYear(title),
                FirstValue(values, "mediatype", "media_type", "type") ?? "Movie",
                values.GetValueOrDefault("status") ?? string.Empty,
                ConvertStarRating(values.GetValueOrDefault("rating")),
                ParseTags(FirstValue(values, "tags", "usertags", "user_tags")),
                values.GetValueOrDefault("comment"),
                ParseDate(values.GetValueOrDefault("date")),
                SourceName,
                FirstValue(values, "imdbid", "imdb_id", "imdb"),
                FirstValue(values, "tmdbid", "tmdb_id", "tmdb"),
                FirstValue(values, "tvdbid", "tvdb_id", "tvdb")));
        }

        return await ImportItemsAsync(items, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports Douban JSON content.
    /// </summary>
    /// <param name="json">JSON text.</param>
    /// <param name="userId">Jellyfin user ID associated with the imported taste signals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OperationResult> ImportJsonTextAsync(string json, Guid userId, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var rows = EnumerateJsonItems(document.RootElement)
            .Select(ReadJsonItem)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .ToArray();

        return rows.Length == 0
            ? new OperationResult(false, "Douban JSON did not contain importable movie or TV items.")
            : await ImportItemsAsync(rows, userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> ImportItemsAsync(IReadOnlyList<ImportedDoubanItem> items, Guid userId, CancellationToken cancellationToken)
    {
        var imported = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;

            await _repository.UpsertDoubanItemAsync(
                new DoubanItem(
                    item.SubjectId,
                    item.Title,
                    item.OriginalTitle,
                    item.Year,
                    item.MediaType,
                    item.Status,
                    item.Rating,
                    item.Tags,
                    item.Comment,
                    item.MarkedAt,
                    now,
                    item.Source,
                    item.ImdbId,
                    item.TmdbId,
                    item.TvdbId),
                cancellationToken).ConfigureAwait(false);

            if (userId != Guid.Empty)
            {
                await _repository.UpsertExternalRatingAsync(
                    new ExternalRating(0, userId, "douban", item.SubjectId, null, item.Rating * 2, item.Status, item.Comment, now),
                    cancellationToken).ConfigureAwait(false);
            }

            imported++;
        }

        return new OperationResult(true, string.Create(CultureInfo.InvariantCulture, $"Imported {imported} Douban items."), imported);
    }

    /// <summary>
    /// Converts Douban star text to a 1-5 integer.
    /// </summary>
    /// <param name="rating">Rating text.</param>
    public static int? ConvertStarRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating))
        {
            return null;
        }

        var stars = rating.Count(static c => c == '★' || c == '*');
        if (stars > 0)
        {
            return Math.Clamp(stars, 1, 5);
        }

        return int.TryParse(rating, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, 5)
            : null;
    }

    /// <summary>
    /// Extracts a Douban subject ID from a movie subject URL.
    /// </summary>
    /// <param name="url">Douban URL.</param>
    public static string? ExtractSubjectId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = SubjectUrlRegex().Match(url);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string csv)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '\ufeff' && rows.Count == 0 && row.Count == 0 && value.Length == 0)
            {
                continue;
            }

            if (c == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                row.Add(value.ToString());
                value.Clear();
                continue;
            }

            if ((c == '\r' || c == '\n') && !inQuotes)
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(value.ToString());
                value.Clear();
                if (row.Count > 1 || row.Any(static cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(row);
                }

                row = [];
                continue;
            }

            value.Append(c);
        }

        row.Add(value.ToString());
        if (row.Count > 1 || row.Any(static cell => !string.IsNullOrWhiteSpace(cell)))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static Dictionary<string, string?> ToDictionary(IReadOnlyList<string> headers, IReadOnlyList<string> row)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            values[headers[i]] = i < row.Count ? row[i].Trim() : null;
        }

        return values;
    }

    private static string NormalizeHeader(string header) => header.Trim().TrimStart('\ufeff').ToLowerInvariant();

    private static string? FirstValue(IReadOnlyDictionary<string, string?> values, params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart('\ufeff', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static IEnumerable<JsonElement> EnumerateJsonItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        foreach (var propertyName in new[] { "items", "data", "interests", "subjects" })
        {
            if (root.TryGetProperty(propertyName, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray();
            }
        }

        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static ImportedDoubanItem? ReadJsonItem(JsonElement item)
    {
        var subjectId = JsonString(item, "doubanSubjectId", "subjectId", "id")
            ?? ExtractSubjectId(JsonString(item, "url", "subjectUrl"));
        var title = JsonString(item, "title", "name");
        if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var mediaType = JsonString(item, "mediaType", "type", "category") ?? "Movie";
        return new ImportedDoubanItem(
            subjectId,
            title,
            JsonString(item, "originalTitle", "original_title"),
            JsonInt(item, "year") ?? TryExtractYear(title),
            NormalizeMediaType(mediaType),
            JsonString(item, "userStatus", "status") ?? string.Empty,
            JsonInt(item, "userRating", "rating") ?? ConvertStarRating(JsonString(item, "ratingText", "rating")),
            JsonStringArray(item, "userTags", "tags"),
            JsonString(item, "userComment", "comment"),
            JsonDate(item, "markedAt", "date"),
            JsonSourceName,
            JsonString(item, "imdbId", "imdb"),
            JsonString(item, "tmdbId", "tmdb"),
            JsonString(item, "tvdbId", "tvdb"));
    }

    private static string? JsonString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                return property.GetRawText();
            }
        }

        return null;
    }

    private static int? JsonInt(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return ParseInt(property.GetString());
            }
        }

        return null;
    }

    private static IReadOnlyList<string> JsonStringArray(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .ToArray();
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return ParseTags(property.GetString());
            }
        }

        return [];
    }

    private static DateTimeOffset? JsonDate(JsonElement item, params string[] names)
        => ParseDate(JsonString(item, names));

    private static IReadOnlyList<string> ParseTags(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result)
            ? result
            : null;
    }

    private static int? TryExtractYear(string title)
    {
        var match = YearRegex().Match(title);
        return match.Success && int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out var year) ? year : null;
    }

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string NormalizeMediaType(string mediaType)
        => string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "TV", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "Show", StringComparison.OrdinalIgnoreCase)
            ? "Series"
            : "Movie";

    private sealed record ImportedDoubanItem(
        string SubjectId,
        string Title,
        string? OriginalTitle,
        int? Year,
        string MediaType,
        string Status,
        int? Rating,
        IReadOnlyList<string> Tags,
        string? Comment,
        DateTimeOffset? MarkedAt,
        string Source,
        string? ImdbId,
        string? TmdbId,
        string? TvdbId);

    [GeneratedRegex(@"douban\.com/subject/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubjectUrlRegex();

    [GeneratedRegex(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();
}
