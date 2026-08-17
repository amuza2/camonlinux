using System;
using System.IO;
using System.Text.Json;
using camonlinux.Models;

namespace camonlinux.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// <c>~/.config/camonlinux/settings.json</c>.
/// </summary>
public sealed class SettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "camonlinux");

    private static readonly string SettingsPath = Path.Combine(ConfigDir, "settings.json");

    public AppSettings Settings { get; }

    public SettingsService()
    {
        Settings = Load();
        if (string.IsNullOrWhiteSpace(Settings.PhotoDirectory))
            Settings.PhotoDirectory = DefaultDirectory("XDG_PICTURES_DIR", "Pictures");
        if (string.IsNullOrWhiteSpace(Settings.VideoDirectory))
            Settings.VideoDirectory = DefaultDirectory("XDG_VIDEOS_DIR", "Videos");
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

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
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

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are best-effort; a failure should never crash the app.
        }
    }
}
