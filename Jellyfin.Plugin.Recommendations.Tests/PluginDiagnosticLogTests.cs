using Jellyfin.Plugin.Recommendations.Services;
using Jellyfin.Plugin.Recommendations.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class PluginDiagnosticLogTests
{
    [Fact]
    public async Task AppendAndReadRoundTripsLogMessages()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.log");
        var log = new PluginDiagnosticLog(logPath);

        await log.AppendAsync("RebuildIndex started.", CancellationToken.None);
        await log.AppendAsync("RebuildIndex completed: success=True; affected=12; message=\"Indexed 12 library items.\"", CancellationToken.None);

        var text = await log.ReadAsync(CancellationToken.None);

        Assert.Contains("RebuildIndex started.", text);
        Assert.Contains("Indexed 12 library items.", text);
        Assert.Equal(logPath, log.LogPath);
    }

    [Fact]
    public async Task LlmClientLogsSkipReasonWhenConfigurationIsIncomplete()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "jellyfin-recommendations-tests", $"{Guid.NewGuid():N}.log");
        var log = new PluginDiagnosticLog(logPath);
        var client = new OpenAiCompatibleLlmClient(NullLogger<OpenAiCompatibleLlmClient>.Instance, log);

        var recommendations = await client.RecommendAsync(new PluginConfiguration(), [], 5, CancellationToken.None);
        var text = await log.ReadAsync(CancellationToken.None);

        Assert.Empty(recommendations);
        Assert.Contains("LLM skipped", text);
        Assert.Contains("hasBaseUrl=False", text);
        Assert.Contains("hasApiKey=False", text);
        Assert.Contains("hasModel=False", text);
    }
}
