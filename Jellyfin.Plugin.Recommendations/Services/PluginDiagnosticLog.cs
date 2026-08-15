using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Recommendations.Services;

/// <summary>
/// File-backed plugin diagnostic log.
/// </summary>
public sealed class PluginDiagnosticLog : IPluginDiagnosticLog
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDiagnosticLog"/> class.
    /// </summary>
    /// <param name="logPath">Absolute log file path.</param>
    public PluginDiagnosticLog(string logPath)
    {
        LogPath = logPath;
    }

    /// <inheritdoc />
    public string LogPath { get; }

    /// <inheritdoc />
    public async Task AppendAsync(string message, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(LogPath, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LogPath))
        {
            return "No recommendation plugin log entries yet.";
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await File.ReadAllTextAsync(LogPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
