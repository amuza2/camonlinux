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

    private readonly List<FileSystemWatcher> _watchers = new();

    /// <summary>Raised when files are created/deleted/renamed in the watched folders.</summary>
    public event Action? CollectionChanged;

    public bool IsVideo(string extension) => VideoExtensions.Contains(extension);

    /// <summary>Watches one or more folders so the gallery can auto-refresh.</summary>
    public void Watch(params string[] directories)
    {
        StopWatching();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = false
            };
            watcher.Created += (_, _) => CollectionChanged?.Invoke();
            watcher.Deleted += (_, _) => CollectionChanged?.Invoke();
            watcher.Renamed += (_, _) => CollectionChanged?.Invoke();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
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

    public void Dispose() => StopWatching();
}
