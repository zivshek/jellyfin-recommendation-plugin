using Jellyfin.Plugin.Recommendations.Configuration;
using Jellyfin.Plugin.Recommendations.Data;
using Jellyfin.Plugin.Recommendations.Domain;
using Jellyfin.Plugin.Recommendations.Services;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class RecommendationEngineTests
{
    [Fact]
    public void ValidatorRejectsInvalidAndDuplicateIds()
    {
        var valid = Guid.NewGuid();
        var invalid = Guid.NewGuid();
        var validator = new RecommendationValidator();

        var result = validator.Validate(
            [
                new ValidatedRecommendation(invalid, 1, "bad", 0.5, "llm"),
                new ValidatedRecommendation(valid, 2, "good", 1.5, "llm"),
                new ValidatedRecommendation(valid, 3, "duplicate", 0.2, "llm")
            ],
            new HashSet<Guid> { valid });

        Assert.Single(result);
        Assert.Equal(valid, result[0].ItemId);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(1, result[0].Confidence);
    }

    [Fact]
    public void DeterministicEngineExcludesWatchedItemsByDefault()
    {
        var watched = Guid.NewGuid();
        var unwatched = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var engine = new RecommendationEngine();

        var result = engine.Recommend(
            [
                new RecommendationCandidate(CreateItem(watched, "Watched", 9), new UserItemStats(userId, watched, "Movie", now, now, 1, 100, true, false, 10, true, true, true, now), []),
                new RecommendationCandidate(CreateItem(unwatched, "Unwatched", 8), null, [])
            ],
            10,
            includeWatched: false);

        Assert.Single(result);
        Assert.Equal(unwatched, result[0].ItemId);
    }

    [Fact]
    public void CollectionDiffRemovesOnlyStalePluginManagedItems()
    {
        var keep = Guid.NewGuid();
        var remove = Guid.NewGuid();
        var add = Guid.NewGuid();

        var diff = ManagedCollectionService.ComputeDiff([keep, remove], [keep, add]);

        Assert.Equal([add], diff.Add);
        Assert.Equal([remove], diff.Remove);
    }

    [Fact]
    public async Task OrchestratorRejectsWatchedLlmOutputWhenWatchedItemsAreDisabled()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.db");
        var repository = new RecommendationRepository(dbPath);
        await repository.InitializeAsync(CancellationToken.None);
        var watched = Guid.NewGuid();
        var unwatched = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertLibraryItemAsync(CreateItem(watched, "Watched", 9), CancellationToken.None);
        await repository.UpsertLibraryItemAsync(CreateItem(unwatched, "Unwatched", 8), CancellationToken.None);
        await repository.UpsertUserItemStatsAsync(
            new UserItemStats(userId, watched, "Movie", now, now, 1, 100, true, false, 10, true, true, true, now),
            CancellationToken.None);

        var orchestrator = new RecommendationOrchestrator(
            repository,
            new RecommendationEngine(),
            new RecommendationValidator(),
            new StaticLlmClient([
                new ValidatedRecommendation(watched, 1, "LLM tried watched item.", 0.9, "llm"),
                new ValidatedRecommendation(unwatched, 2, "Valid fallback candidate.", 0.8, "llm")
            ]));

        var result = await orchestrator.GenerateAsync(
            userId,
            new PluginConfiguration { LlmProvider = "openai-compatible", LlmBaseUrl = "http://localhost", LlmApiKey = "test", LlmModel = "test", IncludeWatchedItems = false },
            CancellationToken.None);
        var recommendations = await repository.GetLatestRecommendationItemsAsync(userId, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(recommendations);
        Assert.Equal(unwatched, recommendations[0].ItemId);
    }

    private static LibraryItem CreateItem(Guid id, string name, double rating)
        => new(id, name, null, 2000, "Movie", [], [], [], null, rating, null, null, null, true, DateTimeOffset.UtcNow);

    private sealed class StaticLlmClient : ILlmClient
    {
        private readonly IReadOnlyList<ValidatedRecommendation> _recommendations;

        public StaticLlmClient(IReadOnlyList<ValidatedRecommendation> recommendations)
        {
            _recommendations = recommendations;
        }

        public Task<IReadOnlyList<ValidatedRecommendation>> RecommendAsync(
            PluginConfiguration configuration,
            IReadOnlyList<RecommendationCandidate> candidates,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult(_recommendations);
    }
}
