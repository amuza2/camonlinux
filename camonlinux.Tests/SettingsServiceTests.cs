using System;
using System.IO;
using camonlinux.Services;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// Settings persistence: round-tripping, the atomic write (temp file + rename) and the
/// debounced <see cref="SettingsService.QueueSave"/> path.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _configDir = Path.Combine(
        Path.GetTempPath(), "camonlinux_settings_" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_configDir, "settings.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_configDir))
                Directory.Delete(_configDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void MissingFile_StartsFromDefaults()
    {
        using var service = new SettingsService(_configDir);

        Assert.Equal(128, service.Settings.Brightness);
        Assert.Equal("medium", service.Settings.RecordQuality);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save_ThenReload_RoundTripsTheValues()
    {
        using (var service = new SettingsService(_configDir))
        {
            service.Settings.Brightness = 42;
            service.Settings.Mirrored = false;
            service.Settings.PhotoFormat = "png";
            service.Save();
        }

        using var reloaded = new SettingsService(_configDir);

        Assert.Equal(42, reloaded.Settings.Brightness);
        Assert.False(reloaded.Settings.Mirrored);
        Assert.Equal("png", reloaded.Settings.PhotoFormat);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        using var service = new SettingsService(_configDir);

        service.Settings.Contrast = 7;
        service.Save();

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save_OverwritesPreviousContentCompletely()
    {
        using var service = new SettingsService(_configDir);

        service.Settings.FavoriteEffects.Add("a");
        service.Settings.FavoriteEffects.Add("b");
        service.Save();

        service.Settings.FavoriteEffects.Clear();
        service.Settings.FavoriteEffects.Add("c");
        service.Save();

        using var reloaded = new SettingsService(_configDir);
        Assert.Equal(new[] { "c" }, reloaded.Settings.FavoriteEffects);
    }

    [Fact]
    public void QueueSave_IsFlushedByDispose()
    {
        var service = new SettingsService(_configDir);
        service.Settings.Saturation = 200;
        service.QueueSave();

        // No waiting: Dispose must flush the pending debounced write.
        service.Dispose();

        using var reloaded = new SettingsService(_configDir);
        Assert.Equal(200, reloaded.Settings.Saturation);
    }

    [Fact]
    public void QueueSave_EventuallyWritesWithoutAnExplicitSave()
    {
        using var service = new SettingsService(_configDir);
        service.Settings.Sharpness = 99;
        service.QueueSave();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains("\"Sharpness\": 99"))
                return;
            System.Threading.Thread.Sleep(25);
        }

        Assert.Fail("The debounced save never reached disk.");
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaultsInsteadOfThrowing()
    {
        Directory.CreateDirectory(_configDir);
        File.WriteAllText(SettingsPath, "{ this is not json");

        using var service = new SettingsService(_configDir);

        Assert.Equal(128, service.Settings.Brightness);
    }

    [Fact]
    public void SaveAfterDispose_IsIgnoredRatherThanThrowing()
    {
        var service = new SettingsService(_configDir);
        service.Dispose();

        service.Settings.Brightness = 1;
        service.Save();
        service.QueueSave();
    }
}
