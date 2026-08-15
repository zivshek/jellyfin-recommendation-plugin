using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Recommendations.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        JellyfinBaseUrl = string.Empty;
        TestApiKey = string.Empty;
        TestUserId = string.Empty;
        LlmProvider = "openai-compatible";
        LlmBaseUrl = string.Empty;
        LlmApiKey = string.Empty;
        LlmModel = string.Empty;
        DoubanUserId = string.Empty;
        DoubanExportPath = string.Empty;
        DoubanSyncProvider = "csv";
        DoubanSyncIntervalHours = 24;
        RecommendationCollectionName = "Recommended For You";
        RecommendationLimit = 20;
        IncludeWatchedItems = false;
        ScheduledRefreshIntervalHours = 24;
    }

    /// <summary>
    /// Gets or sets the Jellyfin base URL used by local test commands.
    /// </summary>
    public string JellyfinBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets a Jellyfin API key used only for local/manual test flows.
    /// </summary>
    public string TestApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin user ID used by local/manual test flows.
    /// </summary>
    public string TestUserId { get; set; }

    /// <summary>
    /// Gets or sets the LLM provider identifier.
    /// </summary>
    public string LlmProvider { get; set; }

    /// <summary>
    /// Gets or sets the OpenAI-compatible LLM base URL.
    /// </summary>
    public string LlmBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the LLM API key.
    /// </summary>
    public string LlmApiKey { get; set; }

    /// <summary>
    /// Gets or sets the LLM model name.
    /// </summary>
    public string LlmModel { get; set; }

    /// <summary>
    /// Gets or sets the optional Douban user ID.
    /// </summary>
    public string DoubanUserId { get; set; }

    /// <summary>
    /// Gets or sets the optional Douban CSV or JSON export path.
    /// </summary>
    public string DoubanExportPath { get; set; }

    /// <summary>
    /// Gets or sets the Douban sync provider identifier.
    /// </summary>
    public string DoubanSyncProvider { get; set; }

    /// <summary>
    /// Gets or sets the Douban sync interval in hours.
    /// </summary>
    public int DoubanSyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin collection name used for recommendations.
    /// </summary>
    public string RecommendationCollectionName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of recommendations to keep.
    /// </summary>
    public int RecommendationLimit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether already watched items can be recommended.
    /// </summary>
    public bool IncludeWatchedItems { get; set; }

    /// <summary>
    /// Gets or sets the scheduled refresh interval in hours.
    /// </summary>
    public int ScheduledRefreshIntervalHours { get; set; }
}
