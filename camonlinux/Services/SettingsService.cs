using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using camonlinux.Models;

namespace camonlinux.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// <c>~/.config/camonlinux/settings.json</c>.
///
/// Two properties matter here:
/// <list type="bullet">
///   <item><description><b>Atomic writes.</b> The JSON is written to a temp file and
///   then renamed over the real one, so a crash or power loss mid-write can never
///   leave a truncated file behind (which would silently reset every setting to its
///   default, since <see cref="Load"/> falls back to a fresh instance on failure).</description></item>
///   <item><description><b>Debounced writes.</b> <see cref="QueueSave"/> coalesces the
///   bursts of changes produced by dragging a slider — each tick used to rewrite the
///   whole file synchronously on the UI thread. <see cref="Save"/> stays immediate for
///   changes that must survive a crash: window close, settings dialog, app exit.</description></item>
/// </list>
/// </summary>
public sealed class SettingsService : IDisposable
{
    /// <summary>How long to wait after the last change before flushing a queued save.</summary>
    private const int DebounceMs = 500;

    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "camonlinux");

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly string _configDir;
    private readonly string _settingsPath;
    private readonly string _tempPath;
    private readonly object _saveLock = new();
    private readonly Timer _debounceTimer;
    private string? _pendingJson;
    private bool _disposed;

    public AppSettings Settings { get; }

    /// <summary>Full path of the settings file. Shown in the About diagnostics.</summary>
    public string SettingsPath => _settingsPath;

    /// <param name="configDirectory">
    /// Directory the settings file lives in. Defaults to <c>~/.config/camonlinux</c>;
    /// tests pass a temporary directory so they never touch the real user config.
    /// </param>
    public SettingsService(string? configDirectory = null)
    {
        _configDir = string.IsNullOrWhiteSpace(configDirectory) ? DefaultConfigDir : configDirectory;
        _settingsPath = Path.Combine(_configDir, "settings.json");
        _tempPath = _settingsPath + ".tmp";

        Settings = Load();
        if (string.IsNullOrWhiteSpace(Settings.PhotoDirectory))
            Settings.PhotoDirectory = DefaultDirectory("XDG_PICTURES_DIR", "Pictures");
        if (string.IsNullOrWhiteSpace(Settings.VideoDirectory))
            Settings.VideoDirectory = DefaultDirectory("XDG_VIDEOS_DIR", "Videos");

        _debounceTimer = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private static string DefaultDirectory(string xdgEnvVar, string fallbackName)
    {
        var xdg = Environment.GetEnvironmentVariable(xdgEnvVar);
        if (!string.IsNullOrWhiteSpace(xdg))
            return xdg;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            fallbackName);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
                if (loaded is not null)
                    return loaded;
            }
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }

        return new AppSettings();
    }

    /// <summary>
    /// Schedules a save shortly from now. Repeated calls within the debounce window
    /// collapse into a single write — use this for high-frequency changes such as
    /// slider drags.
    ///
    /// The object graph is serialised immediately on the calling thread (the UI thread,
    /// which is the only thread that mutates <see cref="Settings"/>); only the file
    /// write is deferred. Serialising in the timer callback instead would let a
    /// background flush observe a half-updated collection.
    /// </summary>
    public void QueueSave()
    {
        lock (_saveLock)
        {
            if (_disposed)
                return;
            _pendingJson = Serialize();
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Writes the settings to disk immediately (and cancels any pending queued save).
    /// Blocks until the file has been replaced, so the value survives process exit.
    /// </summary>
    public void Save()
    {
        lock (_saveLock)
        {
            if (_disposed)
                return;
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _pendingJson = null;
            WriteAtomic(Serialize());
        }
    }

    /// <summary>Called on the debounce timer thread; writes the snapshot taken by <see cref="QueueSave"/>.</summary>
    private void FlushPending()
    {
        lock (_saveLock)
        {
            if (_disposed)
                return;
            var json = _pendingJson;
            _pendingJson = null;
            if (json is not null)
                WriteAtomic(json);
        }
    }

    private string Serialize()
    {
        try
        {
            return JsonSerializer.Serialize(Settings, s_jsonOptions);
        }
        catch
        {
            // Never expected for this object graph; prefer losing a save over crashing.
            return string.Empty;
        }
    }

    private void WriteAtomic(string json)
    {
        if (json.Length == 0)
            return;

        try
        {
            Directory.CreateDirectory(_configDir);
            File.WriteAllText(_tempPath, json);
            // Rename over the real file: atomic on the same filesystem, so readers (and
            // the next launch) always see either the complete old or the complete new content.
            File.Move(_tempPath, _settingsPath, overwrite: true);
        }
        catch
        {
            // Settings are best-effort; a failure should never crash the app.
        }
    }

    /// <summary>Flushes the current settings and releases the debounce timer.</summary>
    public void Dispose()
    {
        lock (_saveLock)
        {
            if (_disposed)
                return;
            WriteAtomic(Serialize());
            _disposed = true;
            _pendingJson = null;
            _debounceTimer.Dispose();
        }
    }
}
