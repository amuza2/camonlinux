using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using camonlinux.Capture;
using camonlinux.Models;
using camonlinux.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace camonlinux.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ICaptureService _capture;
    private readonly SettingsService _settings;
    private readonly MediaLibraryService _mediaLibrary;
    private readonly Stopwatch _recordingStopwatch = new();
    private Timer? _recordingTimer;
    private Timer? _burstTimer;
    private string? _sampleImagePath;
    private bool _generatingThumbnails;

    public ObservableCollection<CameraDevice> Devices { get; } = new();
    public ObservableCollection<MediaItem> GalleryItems { get; } = new();
    public ObservableCollection<EffectOption> Effects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private CameraDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private MediaItem? _selectedGalleryItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleBurstCommand))]
    private bool _isPreviewActive;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _recordingTime = "00:00";

    [ObservableProperty]
    private bool _mirrored;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private EffectOption? _selectedEffect;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleBurstCommand))]
    private bool _isBurstActive;

    [ObservableProperty] private string _burstButtonText = "Burst";
    [ObservableProperty] private bool _showEffectsPanel;

    public MainWindowViewModel(
        ICaptureService capture,
        SettingsService settings,
        MediaLibraryService mediaLibrary)
    {
        _capture = capture;
        _settings = settings;
        _mediaLibrary = mediaLibrary;
        _mirrored = settings.Settings.Mirrored;

        LoadEffects();
        SelectedEffect = Effects.FirstOrDefault();

        _capture.ErrorOccurred += (_, message) => StatusMessage = message;
    }

    private void LoadEffects()
    {
        Effects.Add(new EffectOption("none", "None", ""));
        Effects.Add(new EffectOption("bulge", "Bulge", "bulge"));
        Effects.Add(new EffectOption("dicetv", "Dice TV", "dicetv"));
        Effects.Add(new EffectOption("edgetv", "Edge TV", "edgetv"));
        Effects.Add(new EffectOption("kaleidoscope", "Kaleidoscope", "kaleidoscope"));
        Effects.Add(new EffectOption("optv", "Opt TV", "optv"));
        Effects.Add(new EffectOption("pinch", "Pinch", "pinch"));
        Effects.Add(new EffectOption("quarktv", "Quark TV", "quarktv"));
        Effects.Add(new EffectOption("radioactv", "Radioactive TV", "radioactv"));
        Effects.Add(new EffectOption("revtv", "Reverse TV", "revtv"));
        Effects.Add(new EffectOption("rippletv", "Ripple TV", "rippletv"));
        Effects.Add(new EffectOption("shagadelictv", "Shagadelic TV", "shagadelictv"));
        Effects.Add(new EffectOption("square", "Square", "square"));
        Effects.Add(new EffectOption("streaktv", "Streak TV", "streaktv"));
        Effects.Add(new EffectOption("stretch", "Stretch", "stretch"));
        Effects.Add(new EffectOption("twirl", "Twirl", "twirl"));
        Effects.Add(new EffectOption("vertigotv", "Vertigo TV", "vertigotv"));
        Effects.Add(new EffectOption("warptv", "Warp TV", "warptv"));
        Effects.Add(new EffectOption("mirror", "Mirror", "mirror"));
        Effects.Add(new EffectOption("heat", "Heat", "coloreffects preset=heat"));
        Effects.Add(new EffectOption("sepia", "Sepia", "coloreffects preset=sepia"));
        Effects.Add(new EffectOption("xray", "X-Ray", "coloreffects preset=xray"));
        Effects.Add(new EffectOption("grayscale", "Grayscale", "videobalance saturation=0"));
        Effects.Add(new EffectOption("vivid", "Vivid", "videobalance saturation=1.5"));
        Effects.Add(new EffectOption("agingtv", "Aging TV", "videobalance saturation=0 ! agingtv"));

        // frei0r filters (shipped by frei0r-plugins + gst-plugins-bad). They are
        // only added when the plugin is actually installed, so the gallery doesn't
        // show effects that would silently fall back.
        AddEffectsIfAvailable(
            new EffectOption("frei0r.cartoon", "Cartoon", "frei0r.cartoon"),
            new EffectOption("frei0r.edgeglow", "Edge Glow", "frei0r.edgeglow"),
            new EffectOption("frei0r.invert0r", "Invert", "frei0r.invert0r"),
            new EffectOption("frei0r.kmeans", "Posterize", "frei0r.k-means"),
            new EffectOption("frei0r.night", "Night Vision", "frei0r.night"),
            new EffectOption("frei0r.pixeliz0r", "Pixelate", "frei0r.pixeliz0r"),
            new EffectOption("frei0r.sketch0r", "Sketch", "frei0r.sketch0r"),
            new EffectOption("frei0r.vertigo", "Vertigo", "frei0r.vertigo"),
            new EffectOption("frei0r.vignette", "Vignette", "frei0r.vignette"),
            new EffectOption("frei0r.bigbrother", "CCTV", "frei0r.bigbrother"),
            new EffectOption("frei0r.bulge", "Bulge FX", "frei0r.bulge"),
            new EffectOption("frei0r.infrared", "Infrared", "frei0r.infrared"),
            new EffectOption("frei0r.emboss", "Emboss", "frei0r.emboss"),
            new EffectOption("frei0r.sobel", "Sobel", "frei0r.sobel"),
            new EffectOption("frei0r.solarize", "Solarize", "frei0r.solarize"));
    }

    /// <summary>Adds effects only if their first pipeline element is available on this system.</summary>
    private void AddEffectsIfAvailable(params EffectOption[] effects)
    {
        foreach (var effect in effects)
        {
            // The filter may be a compound chain; the first element is the gate.
            var elementName = effect.Filter.Split('!')[0].Trim().Split(' ')[0].Trim();
            if (_capture.IsElementAvailable(elementName))
                Effects.Add(effect);
        }
    }

    // ------------------------------------------------------------------ //
    // Startup
    // ------------------------------------------------------------------ //

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Looking for cameras…";

        await _capture.InitializeAsync();
        var devices = await _capture.RefreshDevicesAsync();

        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);

        if (Devices.Count == 0)
        {
            StatusMessage = "No camera found. Is it connected, and is your user in the 'video' group?";
            IsBusy = false;
            return;
        }

        SelectedDevice =
            Devices.FirstOrDefault(d => d.Id == _settings.Settings.LastDeviceId)
            ?? Devices[0];

        await StartPreviewAsync(SelectedDevice);
        RefreshGallery();
        IsBusy = false;
    }

    private async Task StartPreviewAsync(CameraDevice device)
    {
        try
        {
            await _capture.StartPreviewAsync(device);
            IsPreviewActive = true;

            // If a device switch happened mid-recording, the service tears the
            // record branch down — keep the VM state in sync.
            if (!_capture.IsRecording)
            {
                IsRecording = false;
                _recordingTimer?.Dispose();
                _recordingTimer = null;
                RecordingTime = "00:00";
            }

            StatusMessage = $"Using {device.Name}";
        }
        catch (Exception ex)
        {
            IsPreviewActive = false;
            StatusMessage = $"Could not open the camera: {ex.Message}";
        }
    }

    partial void OnSelectedDeviceChanged(CameraDevice? value)
    {
        if (value is null)
            return;

        _settings.Settings.LastDeviceId = value.Id;
        _settings.Save();
        _ = StartPreviewAsync(value);
    }

    partial void OnMirroredChanged(bool value)
    {
        _settings.Settings.Mirrored = value;
        _settings.Save();
        _capture.Mirrored = value;

        // The flip element is baked into the pipeline, so restart the preview.
        if (IsPreviewActive && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    partial void OnSelectedEffectChanged(EffectOption? value)
    {
        _capture.Effect = value?.Filter ?? "";

        // The effect is baked into the pipeline, so restart the preview.
        // (Not while recording — it applies when the preview resumes.)
        if (IsPreviewActive && !IsRecording && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    partial void OnShowEffectsPanelChanged(bool value)
    {
        if (value)
            _ = GenerateEffectThumbnailsAsync();
    }

    /// <summary>Renders a small live preview of every effect from a camera frame.</summary>
    private async Task GenerateEffectThumbnailsAsync()
    {
        // Grab a sample frame to render every effect through.
        if (_sampleImagePath is null || !File.Exists(_sampleImagePath))
        {
            try
            {
                _sampleImagePath = Path.Combine(Path.GetTempPath(), $"camonlinux_sample_{Guid.NewGuid():N}.jpg");
                await _capture.TakePhotoAsync(_sampleImagePath);
            }
            catch
            {
                return; // no frame available (e.g. preview not running)
            }
        }

        var thumbDir = Path.Combine(Path.GetTempPath(), "camonlinux_effects");
        Directory.CreateDirectory(thumbDir);

        foreach (var effect in Effects)
        {
            if (effect.Thumbnail is not null)
                continue; // already generated

            var output = Path.Combine(thumbDir, $"{effect.Id}.png");
            var rendered = await Task.Run(() =>
                _capture.RenderEffectThumbnailAsync(effect.Filter, _sampleImagePath!, output, 100, 56));

            if (rendered is not null)
            {
                var path = rendered;
                Dispatcher.UIThread.Post(() =>
                {
                    try { effect.Thumbnail = new Bitmap(path); } catch { /* ignore */ }
                });
            }
        }
    }

    // ------------------------------------------------------------------ //
    // Device management
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        var devices = await _capture.RefreshDevicesAsync();
        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);
        IsBusy = false;
    }

    // ------------------------------------------------------------------ //
    // Capture
    // ------------------------------------------------------------------ //

    [RelayCommand(CanExecute = nameof(CanTakePhoto))]
    private Task TakePhotoAsync() => CapturePhotoAsync();

    private async Task CapturePhotoAsync()
    {
        var path = NextPhotoPath(_settings.Settings.PhotoDirectory);

        try
        {
            await _capture.TakePhotoAsync(path);
            StatusMessage = $"Photo saved to {Path.GetFileName(path)}";
            NotificationService.Notify("Photo taken", Path.GetFileName(path));
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not take a photo: {ex.Message}";
        }
    }

    private bool CanTakePhoto() => IsPreviewActive && !IsRecording && !IsBurstActive;

    private static string NextPhotoPath(string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(directory, $"picture_{stamp}.jpg");
        var index = 1;
        while (File.Exists(path))
            path = Path.Combine(directory, $"picture_{stamp}_{index++}.jpg");
        return path;
    }

    [RelayCommand(CanExecute = nameof(CanToggleBurst))]
    private void ToggleBurst()
    {
        if (_burstTimer is null)
        {
            IsBurstActive = true;
            BurstButtonText = "Burst: On";
            StatusMessage = "Burst mode — taking a photo every 2.5 seconds.";
            _burstTimer = new Timer(
                _ => Dispatcher.UIThread.Post(async () => await CapturePhotoAsync()),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2.5));
        }
        else
        {
            StopBurst();
            StatusMessage = "Burst stopped.";
        }
    }

    private void StopBurst()
    {
        _burstTimer?.Dispose();
        _burstTimer = null;
        IsBurstActive = false;
        BurstButtonText = "Burst";
    }

    private bool CanToggleBurst() => IsPreviewActive && !IsRecording;

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (!IsRecording)
        {
            StopBurst();
            var directory = _settings.Settings.VideoDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"video_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");

            try
            {
                await _capture.StartRecordingAsync(path);
                IsRecording = true;
                _recordingStopwatch.Restart();
                _recordingTimer = new Timer(
                    _ => Dispatcher.UIThread.Post(() =>
                        RecordingTime = _recordingStopwatch.Elapsed.ToString(@"mm\:ss")),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1));
                StatusMessage = "Recording…";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not start recording: {ex.Message}";
            }
        }
        else
        {
            await _capture.StopRecordingAsync();
            IsRecording = false;
            _recordingTimer?.Dispose();
            _recordingTimer = null;
            RecordingTime = "00:00";
            StatusMessage = "Recording saved.";
            NotificationService.Notify("Video saved");
            RefreshGallery();
        }
    }

    private bool CanToggleRecording() => (IsPreviewActive || IsRecording) && !IsBurstActive;

    // ------------------------------------------------------------------ //
    // Gallery
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private void RefreshGallery()
    {
        var items = _mediaLibrary.LoadMedia(_settings.Settings.PhotoDirectory)
            .Concat(_mediaLibrary.LoadMedia(_settings.Settings.VideoDirectory))
            .OrderByDescending(item => item.ModifiedAt)
            .Take(60);

        GalleryItems.Clear();
        foreach (var item in items)
            GalleryItems.Add(item);

        _ = GenerateGalleryThumbnailsAsync();
    }

    /// <summary>Generates thumbnails for gallery items (photos and videos), cached on disk.</summary>
    private async Task GenerateGalleryThumbnailsAsync()
    {
        if (_generatingThumbnails)
            return;
        _generatingThumbnails = true;
        try
        {
            var items = GalleryItems.ToList();
            await Parallel.ForEachAsync(
                items,
                new ParallelOptions { MaxDegreeOfParallelism = 3 },
                async (item, _) => await EnsureMediaThumbnailAsync(item));
        }
        finally
        {
            _generatingThumbnails = false;
        }
    }

    private async Task EnsureMediaThumbnailAsync(MediaItem item)
    {
        if (item.Thumbnail is not null)
            return;

        var thumbPath = ThumbCachePath(item.Path);
        // Reuse the cache only if it's a valid, fresh, single-frame PNG. A broken
        // file (e.g. from an older buggy renderer that concatenated thousands of
        // frames into one huge file) must be re-rendered, not loaded.
        var needsRender = !IsValidCachedThumbnail(thumbPath)
                          || File.GetLastWriteTime(thumbPath) < item.ModifiedAt;

        if (needsRender)
        {
            // Reuse the one-shot GStreamer thumbnail pipeline for photos AND videos.
            var rendered = await Task.Run(() =>
                _capture.RenderEffectThumbnailAsync("", item.Path, thumbPath, 100, 56) is not null);
            if (!rendered)
                return;
        }

        var path = thumbPath;
        Dispatcher.UIThread.Post(() =>
        {
            try { item.Thumbnail = new Bitmap(path); } catch { /* ignore */ }
        });
    }

    /// <summary>
    /// True if the cached file is a plausible single-frame PNG: exists, starts
    /// with the PNG signature, and is small (a 100x56 thumbnail is a few KB — a
    /// multi-frame concatenation or other garbage is megabytes).
    /// </summary>
    private static bool IsValidCachedThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var info = new FileInfo(path);
            if (info.Length < 50 || info.Length > 1_000_000)
                return false;

            using var stream = File.OpenRead(path);
            var header = new byte[8];
            if (stream.Read(header, 0, 8) != 8)
                return false;
            // PNG magic: 89 50 4E 47 0D 0A 1A 0A
            return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E
                && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A
                && header[6] == 0x1A && header[7] == 0x0A;
        }
        catch
        {
            return false;
        }
    }

    private static string ThumbCachePath(string filePath)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(filePath)));
        return Path.Combine(Path.GetTempPath(), "camonlinux_gallery", hash + ".png");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync()
    {
        if (SelectedGalleryItem is null)
            return Task.CompletedTask;

        if (TrashService.Trash(SelectedGalleryItem.Path))
            GalleryItems.Remove(SelectedGalleryItem);

        return Task.CompletedTask;
    }

    private bool CanDeleteSelected() => SelectedGalleryItem is not null;

    [RelayCommand]
    private void OpenMediaFolder()
    {
        var directory = _settings.Settings.PhotoDirectory;
        if (Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo("xdg-open", directory) { UseShellExecute = true });
        }
    }
}