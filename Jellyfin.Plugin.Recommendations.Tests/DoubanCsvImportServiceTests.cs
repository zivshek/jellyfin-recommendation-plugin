using System.Text;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Services;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class DoubanCsvImportServiceTests
{
    [Theory]
    [InlineData("★", 1)]
    [InlineData("★★★", 3)]
    [InlineData("★★★★★", 5)]
    [InlineData("4", 4)]
    [InlineData("", null)]
    public void ConvertStarRatingHandlesStarsAndNumbers(string value, int? expected)
    {
        Assert.Equal(expected, DoubanCsvImportService.ConvertStarRating(value));
    }

    [Fact]
    public void ExtractSubjectIdReadsMovieSubjectUrl()
    {
        Assert.Equal("1292052", DoubanCsvImportService.ExtractSubjectId("https://movie.douban.com/subject/1292052/"));
    }

    [Fact]
    public async Task ImportTextHandlesUtf8BomAndQuotedCsv()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);
        var service = new DoubanCsvImportService(repository);
        var userId = Guid.NewGuid();
        var csv = "\ufefftitle,url,date,rating,status,comment\r\n\"霸王别姬, Farewell My Concubine\",https://movie.douban.com/subject/1291546/,2024-01-02,★★★★★,看过,\"great, devastating\"\r\n";

        var result = await service.ImportTextAsync(csv, userId, CancellationToken.None);
        var items = await repository.GetDoubanItemsAsync(CancellationToken.None);
        var ratings = await repository.GetExternalRatingsAsync(userId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Single(items);
        Assert.Equal("1291546", items[0].DoubanSubjectId);
        Assert.Equal(5, items[0].UserRating);
        Assert.Single(ratings);
        Assert.Equal(10, ratings[0].Rating);
    }

    [Fact]
    public async Task ImportJsonTextCachesProviderIdsAndTags()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);
        var service = new DoubanCsvImportService(repository);
        var userId = Guid.NewGuid();
        var json = """
            {
              "items": [
                {
                  "subjectId": "1292052",
                  "title": "The Shawshank Redemption",
                  "originalTitle": "The Shawshank Redemption",
                  "year": 1994,
                  "mediaType": "Movie",
                  "status": "watched",
                  "rating": 5,
                  "tags": ["prison", "hope"],
                  "comment": "still lands",
                  "date": "2024-01-03",
                  "imdbId": "tt0111161",
                  "tmdbId": "278"
                }
              ]
            }
            """;

        var result = await service.ImportJsonTextAsync(json, userId, CancellationToken.None);
        var items = await repository.GetDoubanItemsAsync(CancellationToken.None);
        var ratings = await repository.GetExternalRatingsAsync(userId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.AffectedCount);
        Assert.Single(items);
        Assert.Equal("tt0111161", items[0].ImdbId);
        Assert.Equal("278", items[0].TmdbId);
        Assert.Equal(["prison", "hope"], items[0].UserTags);
        Assert.Single(ratings);
    }
}
