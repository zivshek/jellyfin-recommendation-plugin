using System.Text.Json;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void MetaJsonMatchesPluginIdentity()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Jellyfin.Plugin.Recommendations",
            "meta.json"));

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Equal(PluginIdentity.PluginId, root.GetProperty("guid").GetGuid());
        Assert.Equal("Recommendations", root.GetProperty("name").GetString());
        Assert.Equal("10.11.0.0", root.GetProperty("targetAbi").GetString());
        Assert.Equal(1, root.GetProperty("status").GetInt32());
        Assert.Contains(
            "Jellyfin.Plugin.Recommendations.dll",
            root.GetProperty("assemblies").EnumerateArray().Select(assembly => assembly.GetString()));
    }
}
