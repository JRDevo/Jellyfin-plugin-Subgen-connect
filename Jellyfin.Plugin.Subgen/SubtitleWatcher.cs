using System.Collections.Concurrent;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Subgen;

/// <summary>
/// After the CC button sends videos to Subgen, this watches for each new .srt file.
/// As soon as one appears, it asks Jellyfin to refresh only that item, so the
/// subtitle shows up without a manual "Refresh metadata".
/// Subgen itself only tells Jellyfin about jobs that came from Jellyfin's webhook.
/// </summary>
public sealed class SubtitleWatcher : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(3);

    private readonly ConcurrentDictionary<string, (Guid ItemId, DateTime Deadline)> _pending = new(StringComparer.Ordinal);
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SubtitleWatcher> _logger;
    private readonly object _loopLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SubtitleWatcher(IProviderManager providerManager, IFileSystem fileSystem, ILogger<SubtitleWatcher> logger)
    {
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>Start watching these videos. Already-watched paths get a new deadline.</summary>
    public void Watch(IEnumerable<(Guid ItemId, string Path)> items)
    {
        var deadline = DateTime.UtcNow + GiveUpAfter;
        foreach (var (id, path) in items)
        {
            _pending[path] = (id, deadline);
        }

        lock (_loopLock)
        {
            if (_loop is null || _loop.IsCompleted)
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _loop = Task.Run(() => RunAsync(token), token);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        try
        {
            while (!_pending.IsEmpty && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;
                foreach (var (path, entry) in _pending)
                {
                    if (SubtitleFiles.HasEnglishSubtitle(path))
                    {
                        _pending.TryRemove(path, out _);
                        Refresh(entry.ItemId, path);
                    }
                    else if (now > entry.Deadline)
                    {
                        _pending.TryRemove(path, out _);
                        _logger.LogWarning("Subgen: no subtitle appeared for {Path}; stopped waiting", path);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Jellyfin is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subgen: subtitle watcher stopped");
        }
    }

    private void Refresh(Guid itemId, string path)
    {
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            // Same as "Scan for new and updated files" for one item: finds the new .srt,
            // does not replace metadata or download anything.
            MetadataRefreshMode = MetadataRefreshMode.Default,
            ImageRefreshMode = MetadataRefreshMode.Default,
            ReplaceAllMetadata = false,
            ReplaceAllImages = false
        };
        _providerManager.QueueRefresh(itemId, options, RefreshPriority.High);
        _logger.LogInformation("Subgen: subtitle ready, refreshing {Path}", path);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
