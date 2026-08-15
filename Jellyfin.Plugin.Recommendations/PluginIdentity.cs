using System;

namespace Jellyfin.Plugin.Recommendations;

/// <summary>
/// Dependency-free constants shared by the plugin entry point, manifest, and tests.
/// </summary>
public static class PluginIdentity
{
    /// <summary>
    /// The stable Jellyfin plugin identifier.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("e72f3148-e8c8-4f3c-a3f1-3cbce4487b8f");
}
