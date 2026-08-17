using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using camonlinux.Models;

namespace camonlinux.Services;

/// <summary>
/// Enumerates captured photos/videos and watches a directory for changes so the
/// gallery can auto-refresh.
/// </summary>
public sealed class MediaLibraryService : IDisposable
{
    private static readonly HashSet<string> PhotoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".webm", ".avi", ".mov" };

    private FileSystemWatcher? _watcher;

    /// <summary>Raised when files are created/deleted/renamed in the watched folder.</summary>
    public event Action? CollectionChanged;

    public bool IsVideo(string extension) => VideoExtensions.Contains(extension);

    public void Watch(string directory)
    {
        StopWatching();

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false
        };
        _watcher.Created += (_, _) => CollectionChanged?.Invoke();
        _watcher.Deleted += (_, _) => CollectionChanged?.Invoke();
        _watcher.Renamed += (_, _) => CollectionChanged?.Invoke();
        _watcher.EnableRaisingEvents = true;
    }

    public List<MediaItem> LoadMedia(string directory)
    {
        if (!Directory.Exists(directory))
            return new List<MediaItem>();

        return Directory.EnumerateFiles(directory)
            .Select(f => new FileInfo(f))
            .Where(fi =>
            {
                if (PhotoExtensions.Contains(fi.Extension)) return true;
                return VideoExtensions.Contains(fi.Extension);
            })
            .OrderByDescending(fi => fi.LastWriteTime)
            .Take(200)
            .Select(fi => new MediaItem(
                fi.FullName,
                fi.Name,
                fi.Extension,
                VideoExtensions.Contains(fi.Extension),
                fi.LastWriteTime,
                fi.Length))
            .ToList();
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose() => StopWatching();
}
