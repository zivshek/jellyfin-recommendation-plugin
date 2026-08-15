using Jellyfin.Plugin.Recommendations.Configuration;

namespace Jellyfin.Plugin.Recommendations.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void RuntimeConfigurationDoesNotExposeSelfServerTestFields()
    {
        var properties = typeof(PluginConfiguration).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.DoesNotContain("JellyfinBaseUrl", properties);
        Assert.DoesNotContain("TestApiKey", properties);
        Assert.DoesNotContain("TestUserId", properties);
    }
}
