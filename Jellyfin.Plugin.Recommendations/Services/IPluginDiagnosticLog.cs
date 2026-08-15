using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// Writes and reads the plugin-specific diagnostic log.
/// </summary>
public interface IPluginDiagnosticLog
{
    /// <summary>
    /// Gets the absolute path to the plugin diagnostic log.
    /// </summary>
    string LogPath { get; }

    /// <summary>
    /// Appends one informational line.
    /// </summary>
    Task AppendAsync(string message, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current log text.
    /// </summary>
    Task<string> ReadAsync(CancellationToken cancellationToken);
}
