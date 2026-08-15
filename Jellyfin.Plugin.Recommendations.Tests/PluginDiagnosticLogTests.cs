using Jellyfin.Plugin.Recommendations.Services;

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
}
