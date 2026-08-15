using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Recommendations.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Recommendations;

/// <summary>
/// Main entry point for the recommendations plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The stable Jellyfin plugin identifier.
    /// </summary>
    public static readonly Guid PluginId = PluginIdentity.PluginId;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Recommendations";

    /// <inheritdoc />
    public override string Description => "Generates per-user recommendations from local Jellyfin signals, optional Douban ratings, and validated LLM output.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the active plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = GetVersionedPageName(),
                DisplayName = "Recommendations",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "plugins",
                MenuIcon = "auto_awesome"
            }
        ];
    }

    private string GetVersionedPageName()
    {
        var version = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        return string.Format(CultureInfo.InvariantCulture, "{0}_{1}", Name, version.Replace('.', '_'));
    }
}
