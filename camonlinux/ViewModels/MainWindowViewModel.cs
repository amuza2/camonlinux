using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using camonlinux.Capture;
using camonlinux.Masking;
using camonlinux.Masking.Effects;
using camonlinux.Models;
using camonlinux.Services;
using camonlinux.Views;
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
    private double _burstIntervalSeconds = 2.5;
    private int _burstCount;
    private int _burstTaken;
    private Timer? _photoCountdownTimer;
    private int _timerSeconds;
    private int _countdownRemaining;
    private string _photoFormat = "jpeg";
    private Timer? _cameraControlsDebounce;
    private Timer? _toastTimer;
    private Timer? _galleryRefreshDebounce;
    private bool _loadingIntensity;
    private string? _recordingPath;
    private string? _sampleImagePath;
    private bool _generatingThumbnails;
    private Timer? _devicesTimer;
    private bool _pollingDevices;
    private bool _suppressDeviceSwitch;

    public ObservableCollection<CameraDevice> Devices { get; } = new();
    public ObservableCollection<MediaItem> GalleryItems { get; } = new();
    public ObservableCollection<string> GalleryFilterOptions { get; } = new() { "All", "Photos", "Videos" };
    public ObservableCollection<string> GallerySortOptions { get; } = new() { "Newest", "Oldest", "Name" };
    public ObservableCollection<MediaItem> SelectedGalleryItems { get; } = new();
    public ObservableCollection<EffectOption> Effects { get; } = new();
    public ObservableCollection<string> Resolutions { get; } = new();
    public ObservableCollection<string> QualityOptions { get; } = new() { "Low", "Medium", "High" };
    public ObservableCollection<string> MaxSizeOptions { get; } = new() { "Unlimited", "500 MB", "1 GB", "2 GB" };
    public ObservableCollection<string> TimerOptions { get; } = new() { "Off", "3 s", "10 s" };
    public ObservableCollection<string> RotationOptions { get; } = new() { "0°", "90°", "180°", "270°" };
    public ObservableCollection<string> ZoomOptions { get; } = new() { "1×", "1.5×", "2×", "3×", "4×" };
    public ObservableCollection<string> PhotoFormatOptions { get; } = new() { "JPEG", "PNG" };
    public ObservableCollection<string> AudioDevices { get; } = new();
    public ObservableCollection<EffectOption> FilteredEffects { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private CameraDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlaySelectedCommand))]
    private MediaItem? _selectedGalleryItem;

    [ObservableProperty] private string _selectedGalleryFilter = "All";
    [ObservableProperty] private string _selectedGallerySort = "Newest";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleBurstCommand))]
    private bool _isPreviewActive;

    [ObservableProperty] private bool _isRecording;

    /// <summary>The Record button label: "Stop" while recording, "Record" otherwise.</summary>
    public string RecordButtonText => IsRecording ? "Stop" : "Record";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _recordingTime = "00:00";

    [ObservableProperty]
    private bool _mirrored;

    [ObservableProperty]
    private bool _micEnabled = true;

    [ObservableProperty]
    private string _selectedResolution = "";

    [ObservableProperty]
    private string _selectedQuality = "Medium";

    [ObservableProperty]
    private string _selectedMaxSize = "Unlimited";

    [ObservableProperty]
    private string _selectedTimer = "Off";

    [ObservableProperty]
    private bool _isCountingDown;

    [ObservableProperty]
    private string _countdownText = "";

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private string _selectedRotation = "0°";

    [ObservableProperty]
    private string _selectedZoom = "1×";

    [ObservableProperty]
    private string _selectedPhotoFormat = "JPEG";

        [ObservableProperty]
    private bool _showTimestamp;

    [ObservableProperty]
    private int _brightness = 128;

    [ObservableProperty]
    private int _contrast = 128;

    [ObservableProperty]
    private int _saturation = 128;

    [ObservableProperty]
    private int _sharpness = 128;

    [ObservableProperty]
    private int _gain;

    [ObservableProperty]
    private int _backlightCompensation;

    [ObservableProperty]
    private bool _whiteBalanceAuto = true;

    [ObservableProperty]
    private int _whiteBalanceTemperature = 4000;

    [ObservableProperty]
    private bool _exposureAuto = true;

    [ObservableProperty]
    private int _exposureValue = 250;

    [ObservableProperty]
    private bool _focusAuto = true;

    [ObservableProperty]
    private int _focusValue;

    [ObservableProperty]
    private bool _showAdvancedControls;

    [ObservableProperty] private bool _showSharpness;
    [ObservableProperty] private bool _showWhiteBalance;
    [ObservableProperty] private bool _showExposure;
    [ObservableProperty] private bool _showFocus;
    [ObservableProperty] private bool _showGain;
    [ObservableProperty] private bool _showBacklight;

    [ObservableProperty]
    private bool _showIntensitySlider;

    [ObservableProperty]
    private double _effectIntensity = 1.0;

    [ObservableProperty]
    private string _toastText = "";

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private bool _showGalleryEmptyHint;

    [ObservableProperty]
    private string _recordingSize = "";

    [ObservableProperty]
    private string _effectFilter = "";

    [ObservableProperty]
    private string _selectedAudioDevice = "Default";

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
    [ObservableProperty] private string _selectedBurstInterval = "2.5 s";
    [ObservableProperty] private string _selectedBurstCount = "Unlimited";
    [ObservableProperty] private string _busyText = "Working…";
    [ObservableProperty] private bool _isVideoMode;
    [ObservableProperty] private bool _isMaskEnabled;
    [ObservableProperty] private bool _isVirtualCamEnabled;

    public MainWindowViewModel(
        ICaptureService capture,
        SettingsService settings,
        MediaLibraryService mediaLibrary)
    {
        _capture = capture;
        _settings = settings;
        _mediaLibrary = mediaLibrary;
        _mirrored = settings.Settings.Mirrored;
        _micEnabled = settings.Settings.MicEnabled;
        // Seed both combos with a valid default item so they always display a
        // selection from the very first render (the item lists are otherwise
        // empty until the async population completes after the window opens).
        // The resolution list also pre-seeds the saved resolution so the combo
        // shows it immediately, before the device's supported modes are
        // enumerated asynchronously.
        Resolutions.Add("Default");
        if (!string.IsNullOrEmpty(settings.Settings.Resolution) &&
            !string.Equals(settings.Settings.Resolution, "Default", StringComparison.Ordinal))
            Resolutions.Add(settings.Settings.Resolution);
        AudioDevices.Add("Default");
        if (!string.IsNullOrEmpty(settings.Settings.AudioDevice) &&
            !string.Equals(settings.Settings.AudioDevice, "Default", StringComparison.Ordinal))
            AudioDevices.Add(settings.Settings.AudioDevice);
        _selectedResolution = string.IsNullOrEmpty(settings.Settings.Resolution) ? "Default" : settings.Settings.Resolution;
        _selectedQuality = MapQualityLabel(settings.Settings.RecordQuality);
        _selectedMaxSize = MapMaxSizeLabel(settings.Settings.MaxFileSizeMB);
        _timerSeconds = settings.Settings.TimerSeconds;
        _selectedTimer = MapTimerLabel(settings.Settings.TimerSeconds);
        _burstIntervalSeconds = settings.Settings.BurstIntervalSeconds;
        _burstCount = settings.Settings.BurstPhotoCount;
        _selectedBurstInterval = MapBurstIntervalLabel(_burstIntervalSeconds);
        _selectedBurstCount = _burstCount == 0 ? "Unlimited" : _burstCount.ToString();
        _isVideoMode = settings.Settings.IsVideoMode;
        _selectedRotation = MapRotationLabel(settings.Settings.Rotation);
        _selectedZoom = MapZoomLabel(settings.Settings.Zoom);
        _photoFormat = settings.Settings.PhotoFormat;
        _selectedPhotoFormat = _photoFormat == "png" ? "PNG" : "JPEG";
        _showTimestamp = settings.Settings.ShowTimestamp;
        _brightness = settings.Settings.Brightness;
        _contrast = settings.Settings.Contrast;
        _saturation = settings.Settings.Saturation;
        _sharpness = settings.Settings.Sharpness;
        _gain = settings.Settings.Gain;
        _backlightCompensation = settings.Settings.BacklightCompensation;
        _whiteBalanceAuto = settings.Settings.WhiteBalanceAuto;
        _whiteBalanceTemperature = settings.Settings.WhiteBalanceTemperature;
        _exposureAuto = settings.Settings.ExposureAuto;
        _exposureValue = settings.Settings.ExposureValue;
        _focusAuto = settings.Settings.FocusAuto;
        _focusValue = settings.Settings.FocusValue;
        _capture.Rotation = settings.Settings.Rotation;
        _capture.Zoom = settings.Settings.Zoom;
        _capture.PhotoFormat = settings.Settings.PhotoFormat;
        _capture.ShowTimestamp = settings.Settings.ShowTimestamp;
        _capture.Brightness = settings.Settings.Brightness;
        _capture.Contrast = settings.Settings.Contrast;
        _capture.Saturation = settings.Settings.Saturation;
        _capture.Sharpness = settings.Settings.Sharpness;
        _capture.Gain = settings.Settings.Gain;
        _capture.BacklightCompensation = settings.Settings.BacklightCompensation;
        _capture.WhiteBalanceAuto = settings.Settings.WhiteBalanceAuto;
        _capture.WhiteBalanceTemperature = settings.Settings.WhiteBalanceTemperature;
        _capture.ExposureAuto = settings.Settings.ExposureAuto;
        _capture.ExposureValue = settings.Settings.ExposureValue;
        _capture.FocusAuto = settings.Settings.FocusAuto;
        _capture.FocusValue = settings.Settings.FocusValue;
        _capture.AudioDevice = settings.Settings.AudioDevice;
        _capture.Resolution = settings.Settings.Resolution;
        _capture.RecordQuality = settings.Settings.RecordQuality;
        _capture.MaxFileSizeMB = settings.Settings.MaxFileSizeMB;

        // Effects are loaded in InitializeAsync (after GStreamer is initialized) —
        // ElementFactory.Find returns nothing before gst_init, so frei0r effects
        // would otherwise be filtered out as unavailable.

        _capture.ErrorOccurred += (_, message) => StatusMessage = message;

        // Mask pipeline: shape / gradient / chroma / feather / colour-adjustment.
        // Settings are shared with the Mask Editor so slider changes apply live.
        // (Effects run in list order; the feather blurs the mask built before it,
        // and the colour adjustment runs last in Adjustment mode.)
        var shape = new ShapeMaskSettings();
        var gradient = new GradientMaskSettings();
        var feather = new FeatherMaskSettings();
        var adjustment = new ColorAdjustmentMaskSettings();
        var chroma = new ChromaKeyMaskSettings();
        var pipeline = new MaskPipeline();
        var shapeEffect = new ShapeMaskEffect(shape);
        var gradientEffect = new GradientMaskEffect(gradient);
        var chromaEffect = new ChromaKeyMaskEffect(chroma);
        var featherEffect = new FeatherMaskEffect(feather);
        var adjustmentEffect = new ColorAdjustmentMaskEffect(adjustment);
        pipeline.Effects.Add(shapeEffect);
        pipeline.Effects.Add(gradientEffect);
        pipeline.Effects.Add(chromaEffect);
        pipeline.Effects.Add(featherEffect);
        pipeline.Effects.Add(adjustmentEffect);
        MaskPipeline = pipeline;
        MaskEditor = new MaskEditorViewModel(
            pipeline, shape, gradient, feather, adjustment, chroma,
            shapeEffect, gradientEffect, chromaEffect, featherEffect, adjustmentEffect);

        // Keep the toolbar Mask toggle and the editor's Enable toggle in sync; the
        // pipeline is only applied when enabled.
        MaskEditor.EnabledChanged += value => IsMaskEnabled = value;
    }

    /// <summary>The real-time masking pipeline (applied to each preview frame on the streaming thread).</summary>
    public MaskPipeline MaskPipeline { get; }

    /// <summary>View model for the Mask Editor window (shares the pipeline + settings).</summary>
    public MaskEditorViewModel MaskEditor { get; }

    partial void OnIsMaskEnabledChanged(bool value)
    {
        MaskPipeline.Enabled = value;
        MaskEditor.Enabled = value;
    }

    private void LoadEffects()
    {
        Effects.Clear();

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
        Effects.Add(new EffectOption("grayscale", "Grayscale", "videobalance saturation={I}", "saturation", 0, 1, 0));
        Effects.Add(new EffectOption("vivid", "Vivid", "videobalance saturation={I}", "saturation", 0, 2, 1.5));
        Effects.Add(new EffectOption("agingtv", "Aging TV", "videobalance saturation=0 ! agingtv"));

        // frei0r filters (shipped by frei0r-plugins + gst-plugins-bad). They are
        // only added when the plugin is actually installed, so the gallery doesn't
        // show effects that would silently fall back. Element names carry a
        // "frei0r-filter-" prefix (e.g. frei0r-filter-cartoon).
        AddEffectsIfAvailable(
            new EffectOption("frei0r-filter-cartoon", "Cartoon", "frei0r-filter-cartoon"),
            new EffectOption("frei0r-filter-edgeglow", "Edge Glow", "frei0r-filter-edgeglow"),
            new EffectOption("frei0r-filter-invert0r", "Invert", "frei0r-filter-invert0r"),
            new EffectOption("frei0r-filter-posterize", "Posterize", "frei0r-filter-posterize"),
            new EffectOption("frei0r-filter-k-means-clustering", "K-Means", "frei0r-filter-k-means-clustering"),
            new EffectOption("frei0r-filter-pixeliz0r", "Pixelate", "frei0r-filter-pixeliz0r"),
            new EffectOption("frei0r-filter-vertigo", "Vertigo", "frei0r-filter-vertigo"),
            new EffectOption("frei0r-filter-vignette", "Vignette", "frei0r-filter-vignette"),
            new EffectOption("frei0r-filter-emboss", "Emboss", "frei0r-filter-emboss"),
            new EffectOption("frei0r-filter-sobel", "Sobel", "frei0r-filter-sobel"),
            new EffectOption("frei0r-filter-glow", "Glow", "frei0r-filter-glow"),
            new EffectOption("frei0r-filter-softglow", "Soft Glow", "frei0r-filter-softglow"),
            new EffectOption("frei0r-filter-rgbsplit0r", "RGB Split", "frei0r-filter-rgbsplit0r"),
            new EffectOption("frei0r-filter-heatmap0r", "Heatmap", "frei0r-filter-heatmap0r"),
            new EffectOption("frei0r-filter-ntsc", "NTSC", "frei0r-filter-ntsc"),
            new EffectOption("frei0r-filter-water", "Water", "frei0r-filter-water"),
            new EffectOption("frei0r-filter-glitch0r", "Glitch", "frei0r-filter-glitch0r"));

        // Restore pinned favorites, then wire toggling -> persistence + reorder.
        var favorites = _settings.Settings.FavoriteEffects ?? new List<string>();
        foreach (var effect in Effects)
            effect.IsFavorite = favorites.Contains(effect.Id);
        foreach (var effect in Effects)
            effect.PropertyChanged += OnEffectPropertyChanged;

        RefreshFilteredEffects();
    }

    private void OnEffectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EffectOption.IsFavorite) || sender is not EffectOption effect)
            return;

        _settings.Settings.FavoriteEffects = Effects.Where(x => x.IsFavorite).Select(x => x.Id).ToList();
        _settings.Save();
        ReorderEffects();
    }

    /// <summary>Pinned favorites first, preserving relative order of the rest.</summary>
    private void ReorderEffects()
    {
        var selected = SelectedEffect;
        var ordered = Effects.Where(e => e.IsFavorite).Concat(Effects.Where(e => !e.IsFavorite)).ToList();
        Effects.Clear();
        foreach (var effect in ordered)
            Effects.Add(effect);
        SelectedEffect = selected;
        RefreshFilteredEffects();
    }

    partial void OnEffectFilterChanged(string value) => RefreshFilteredEffects();

    private void RefreshFilteredEffects()
    {
        var filter = EffectFilter?.Trim() ?? "";
        FilteredEffects.Clear();
        foreach (var effect in Effects)
        {
            if (string.IsNullOrEmpty(filter)
                || effect.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || effect.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredEffects.Add(effect);
        }
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
        BusyText = "Looking for cameras…";
        StatusMessage = "Looking for cameras…";

        await _capture.InitializeAsync();

        // GStreamer is initialized above — now the registry is populated, so
        // availability checks (frei0r etc.) reflect what's actually installed.
        LoadEffects();
        SelectedEffect = Effects.FirstOrDefault();
        await LoadAudioDevicesAsync();

        var devices = await _capture.RefreshDevicesAsync();

        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);

        // Keep scanning /dev/video* so plugging/unplugging a camera updates the
        // list live (also lets a camera plugged in later auto-start).
        StartDevicePolling();

        if (Devices.Count == 0)
        {
            StatusMessage = "No camera found. Is it connected, and is your user in the 'video' group?";
            IsBusy = false;
            return;
        }

        _suppressDeviceSwitch = true;
        try
        {
            SelectedDevice =
                Devices.FirstOrDefault(d => d.Id == _settings.Settings.LastDeviceId)
                ?? Devices[0];
        }
        finally
        {
            _suppressDeviceSwitch = false;
        }

        // Enumerates supported capture modes, then starts the preview.
        await OnDeviceSelectedAsync(SelectedDevice);

        // Auto-refresh the gallery when files change in the capture folders.
        _mediaLibrary.CollectionChanged -= OnMediaCollectionChanged;
        _mediaLibrary.CollectionChanged += OnMediaCollectionChanged;
        _mediaLibrary.Watch(_settings.Settings.PhotoDirectory, _settings.Settings.VideoDirectory);

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

    /// <summary>
    /// Enumerates the camera's supported capture modes (before the preview starts,
    /// so the caps query can open the device), then starts the preview.
    /// </summary>
    private async Task OnDeviceSelectedAsync(CameraDevice device)
    {
        BusyText = "Opening camera…";
        await LoadResolutionsAsync(device);
        UpdateSupportedControlVisibility(device);
        await StartPreviewAsync(device);
    }

    /// <summary>Shows only the advanced camera controls the current device exposes.</summary>
    private void UpdateSupportedControlVisibility(CameraDevice device)
    {
        var supported = _capture.GetSupportedControls(device);
        ShowSharpness = supported.Contains("sharpness");
        ShowWhiteBalance = supported.Contains("white_balance_automatic");
        ShowExposure = supported.Contains("auto_exposure");
        ShowFocus = supported.Contains("focus_automatic_continuous");
        ShowGain = supported.Contains("gain");
        ShowBacklight = supported.Contains("backlight_compensation");
    }

    private async Task LoadResolutionsAsync(CameraDevice device)
    {
        var modes = await Task.Run(() => _capture.GetSupportedModes(device));
        var saved = _settings.Settings.Resolution;
        var items = new List<string> { "Default" };
        items.AddRange(modes);

        // Merge the supported modes into the collection WITHOUT clearing it first.
        // Clear() resets the ComboBox's internal selection (SelectedIndex = -1) and,
        // because re-assigning the same bound value raises no change notification,
        // the combo is left blank. Only removing entries that are no longer
        // supported and appending new ones keeps a still-supported item selected.
        for (var i = Resolutions.Count - 1; i >= 0; i--)
        {
            if (!items.Contains(Resolutions[i]))
                Resolutions.RemoveAt(i);
        }
        foreach (var item in items)
        {
            if (!Resolutions.Contains(item))
                Resolutions.Add(item);
        }

        var target = items.Contains(saved) ? saved : "Default";
        if (!string.Equals(SelectedResolution, target, StringComparison.Ordinal))
            SelectedResolution = target;
    }

    partial void OnSelectedDeviceChanged(CameraDevice? value)
    {
        // Hot-plug may re-select the SAME camera (new instance, same id) — in that
        // case we don't want to restart the preview, so skip via the flag.
        if (value is null || _suppressDeviceSwitch)
            return;

        _settings.Settings.LastDeviceId = value.Id;
        _settings.Save();
        _ = OnDeviceSelectedAsync(value);
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

    partial void OnMicEnabledChanged(bool value)
    {
        _settings.Settings.MicEnabled = value;
        _settings.Save();
        _capture.MicMuted = !value;
    }

    partial void OnSelectedResolutionChanged(string value)
    {
        if (value is null)
            return;
        _settings.Settings.Resolution = value;
        _settings.Save();
        _capture.Resolution = value;
        // The caps are baked into the pipeline, so restart the preview.
        if (IsPreviewActive && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    partial void OnSelectedQualityChanged(string value)
    {
        var key = value.ToLowerInvariant() switch { "low" => "low", "high" => "high", _ => "medium" };
        _settings.Settings.RecordQuality = key;
        _settings.Save();
        _capture.RecordQuality = key;
    }

    partial void OnSelectedMaxSizeChanged(string value)
    {
        var mb = value switch { "500 MB" => 500L, "1 GB" => 1024L, "2 GB" => 2048L, _ => 0L };
        _settings.Settings.MaxFileSizeMB = mb;
        _settings.Save();
        _capture.MaxFileSizeMB = mb;
    }

    partial void OnSelectedTimerChanged(string value)
    {
        _timerSeconds = value switch { "3 s" => 3, "10 s" => 10, _ => 0 };
        _settings.Settings.TimerSeconds = _timerSeconds;
        _settings.Save();
        CancelPhotoCountdown();
    }

    partial void OnSelectedBurstIntervalChanged(string value)
    {
        _burstIntervalSeconds = value switch { "1 s" => 1.0, "5 s" => 5.0, _ => 2.5 };
        _settings.Settings.BurstIntervalSeconds = _burstIntervalSeconds;
        _settings.Save();
    }

    partial void OnSelectedBurstCountChanged(string value)
    {
        _burstCount = value switch { "5" => 5, "10" => 10, "20" => 20, _ => 0 };
        _settings.Settings.BurstPhotoCount = _burstCount;
        _settings.Save();
    }

    partial void OnIsVideoModeChanged(bool value)
    {
        _settings.Settings.IsVideoMode = value;
        _settings.Save();
    }

    private static string MapBurstIntervalLabel(double seconds) => seconds switch
    {
        <= 1.2 => "1 s",
        >= 4.5 => "5 s",
        _ => "2.5 s"
    };

    private const long LowDiskThresholdBytes = 300L * 1024 * 1024;

    /// <summary>True if the drive hosting the directory has enough free space; warns otherwise.</summary>
    private bool HasEnoughDiskSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? directory;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free > LowDiskThresholdBytes)
                return true;
            var mb = free / 1024.0 / 1024.0;
            StatusMessage = $"Low disk space — only {mb:0.#} MB free.";
            ShowToast($"Low disk space ({mb:0.#} MB free)");
            return false;
        }
        catch
        {
            return true; // can't determine the size — allow
        }
    }

    private static string MapTimerLabel(int seconds) => seconds switch { 3 => "3 s", 10 => "10 s", _ => "Off" };

    partial void OnSelectedRotationChanged(string value)
    {
        var dir = value switch { "90°" => "90r", "180°" => "180", "270°" => "90l", _ => "auto" };
        _settings.Settings.Rotation = dir;
        _settings.Save();
        _capture.Rotation = dir;
        // Rotation is baked into the pipeline, so restart the preview.
        if (IsPreviewActive && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    partial void OnSelectedZoomChanged(string value)
    {
        var zoom = value switch { "1.5×" => 1.5, "2×" => 2.0, "3×" => 3.0, "4×" => 4.0, _ => 1.0 };
        _settings.Settings.Zoom = zoom;
        _settings.Save();
        _capture.Zoom = zoom;
        // Zoom is baked into the pipeline, so restart the preview.
        if (IsPreviewActive && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    private static string MapRotationLabel(string dir) => dir switch { "90r" => "90°", "180" => "180°", "90l" => "270°", _ => "0°" };

    private static string MapZoomLabel(double zoom) => zoom switch { >= 4.0 => "4×", >= 3.0 => "3×", >= 2.0 => "2×", >= 1.5 => "1.5×", _ => "1×" };

    partial void OnSelectedPhotoFormatChanged(string value)
    {
        _photoFormat = value == "PNG" ? "png" : "jpeg";
        _settings.Settings.PhotoFormat = _photoFormat;
        _settings.Save();
        _capture.PhotoFormat = _photoFormat;
    }

    partial void OnShowTimestampChanged(bool value)
    {
        _settings.Settings.ShowTimestamp = value;
        _settings.Save();
        _capture.ShowTimestamp = value;
    }

    partial void OnBrightnessChanged(int value) => CameraControlsChanged();

    partial void OnContrastChanged(int value) => CameraControlsChanged();

    partial void OnSaturationChanged(int value) => CameraControlsChanged();

    partial void OnSharpnessChanged(int value) => CameraControlsChanged();

    partial void OnGainChanged(int value) => CameraControlsChanged();

    partial void OnBacklightCompensationChanged(int value) => CameraControlsChanged();

    partial void OnWhiteBalanceAutoChanged(bool value) => CameraControlsChanged();

    partial void OnWhiteBalanceTemperatureChanged(int value) => CameraControlsChanged();

    partial void OnExposureAutoChanged(bool value) => CameraControlsChanged();

    partial void OnExposureValueChanged(int value) => CameraControlsChanged();

    partial void OnFocusAutoChanged(bool value) => CameraControlsChanged();

    partial void OnFocusValueChanged(int value) => CameraControlsChanged();

    /// <summary>
    /// Persists the camera controls and applies them live (debounced so dragging a
    /// slider doesn't spawn a v4l2-ctl per tick).
    /// </summary>
    private void CameraControlsChanged()
    {
        _settings.Settings.Brightness = Brightness;
        _settings.Settings.Contrast = Contrast;
        _settings.Settings.Saturation = Saturation;
        _settings.Settings.Sharpness = Sharpness;
        _settings.Settings.Gain = Gain;
        _settings.Settings.BacklightCompensation = BacklightCompensation;
        _settings.Settings.WhiteBalanceAuto = WhiteBalanceAuto;
        _settings.Settings.WhiteBalanceTemperature = WhiteBalanceTemperature;
        _settings.Settings.ExposureAuto = ExposureAuto;
        _settings.Settings.ExposureValue = ExposureValue;
        _settings.Settings.FocusAuto = FocusAuto;
        _settings.Settings.FocusValue = FocusValue;
        _settings.Save();

        _capture.Brightness = Brightness;
        _capture.Contrast = Contrast;
        _capture.Saturation = Saturation;
        _capture.Sharpness = Sharpness;
        _capture.Gain = Gain;
        _capture.BacklightCompensation = BacklightCompensation;
        _capture.WhiteBalanceAuto = WhiteBalanceAuto;
        _capture.WhiteBalanceTemperature = WhiteBalanceTemperature;
        _capture.ExposureAuto = ExposureAuto;
        _capture.ExposureValue = ExposureValue;
        _capture.FocusAuto = FocusAuto;
        _capture.FocusValue = FocusValue;

        _cameraControlsDebounce?.Dispose();
        _cameraControlsDebounce = new Timer(
            _ => Dispatcher.UIThread.Post(() => _capture.ApplyCameraControls()),
            null,
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromMilliseconds(-1));
    }

    [RelayCommand]
    private void ResetCameraControls()
    {
        Brightness = 128;
        Contrast = 128;
        Saturation = 128;
        Sharpness = 128;
        Gain = 0;
        BacklightCompensation = 0;
        WhiteBalanceAuto = true;
        WhiteBalanceTemperature = 4000;
        ExposureAuto = true;
        ExposureValue = 250;
        FocusAuto = true;
        FocusValue = 0;
        _capture.ApplyCameraControls();
        StatusMessage = "Camera controls reset.";
    }

    private static string MapQualityLabel(string key) => key switch { "low" => "Low", "high" => "High", _ => "Medium" };

    private static string MapMaxSizeLabel(long mb) => mb switch { 500 => "500 MB", 1024 => "1 GB", 2048 => "2 GB", _ => "Unlimited" };

    partial void OnSelectedEffectChanged(EffectOption? value)
    {
        if (value is null)
        {
            ShowIntensitySlider = false;
            _capture.Effect = "";
            return;
        }

        if (value.HasIntensity)
        {
            // Load the remembered intensity (or the effect's default) without
            // triggering a rebuild via the intensity changed handler.
            _loadingIntensity = true;
            try
            {
                EffectIntensity = _settings.Settings.EffectIntensities.TryGetValue(value.Id, out var saved)
                    ? saved
                    : value.IntensityDefault;
            }
            finally
            {
                _loadingIntensity = false;
            }
            ShowIntensitySlider = true;
        }
        else
        {
            ShowIntensitySlider = false;
        }

        var filter = BuildEffectFilter(value, EffectIntensity);
        if (_capture.Effect == filter)
            return; // same effect re-selected (e.g. after a reorder) — no restart

        _capture.Effect = filter;

        // The effect is baked into the pipeline, so restart the preview.
        // (Not while recording — it applies when the preview resumes.)
        if (IsPreviewActive && !IsRecording && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    partial void OnEffectIntensityChanged(double value)
    {
        if (_loadingIntensity)
            return;

        var effect = SelectedEffect;
        if (effect is null || !effect.HasIntensity)
            return;

        _settings.Settings.EffectIntensities[effect.Id] = value;
        _settings.Save();

        _capture.Effect = BuildEffectFilter(effect, value);
        // videobalance-based effects are adjustable live — push the new value to the
        // running element instead of rebuilding the pipeline, so dragging the
        // intensity slider is smooth (and works even mid-recording).
        if (_capture.ApplyEffectIntensity(value))
            return;

        if (IsPreviewActive && !IsRecording && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    private static string BuildEffectFilter(EffectOption effect, double intensity)
    {
        if (!effect.HasIntensity)
            return effect.Filter;
        return effect.Filter.Replace("{I}", intensity.ToString("0.##", CultureInfo.InvariantCulture));
    }

    partial void OnShowEffectsPanelChanged(bool value)
    {
        if (value)
            _ = GenerateEffectThumbnailsAsync();
        UpdateGalleryEmptyHint();
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
            var filter = BuildEffectFilter(effect, effect.IntensityDefault);
            var rendered = await Task.Run(() =>
                _capture.RenderEffectThumbnailAsync(filter, _sampleImagePath!, output, 100, 56));

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
    // Hot-plug detection
    // ------------------------------------------------------------------ //

    /// <summary>Scans /dev/video* every 2s and updates the device list live.</summary>
    private void StartDevicePolling()
    {
        _devicesTimer?.Dispose();
        _devicesTimer = new Timer(
            _ => _ = PollDevicesAsync(),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(2));
    }

    private async Task PollDevicesAsync()
    {
        if (_pollingDevices || IsBusy || IsRecording)
            return;

        _pollingDevices = true;
        try
        {
            var devices = await _capture.RefreshDevicesAsync();
            await Dispatcher.UIThread.InvokeAsync(() => ApplyDeviceChanges(devices));
        }
        catch
        {
            // Transient errors (hot-unplug race, dispatcher shutting down during
            // app exit, …) are ignored — the next poll will simply retry.
        }
        finally
        {
            _pollingDevices = false;
        }
    }

    /// <summary>
    /// Applies a device-list change on the UI thread. Keeps the current selection
    /// (without restarting the preview) when the same camera is still present, and
    /// switches to another camera — or stops the preview — when it disappears.
    /// </summary>
    private void ApplyDeviceChanges(IReadOnlyList<CameraDevice> devices)
    {
        var currentIds = Devices.Select(d => d.Id).ToHashSet();
        var nextIds = devices.Select(d => d.Id).ToHashSet();
        if (currentIds.SetEquals(nextIds))
            return; // nothing changed

        var wasSelectedId = SelectedDevice?.Id;

        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);

        // Same camera still present — keep the selection, don't restart preview.
        var sameDevice = devices.FirstOrDefault(d => d.Id == wasSelectedId);
        if (sameDevice is not null)
        {
            _suppressDeviceSwitch = true;
            try { SelectedDevice = sameDevice; }
            finally { _suppressDeviceSwitch = false; }
            return;
        }

        // Selected camera disappeared — fall back to the first available one.
        var fallback = devices.FirstOrDefault();
        if (fallback is not null)
        {
            if (IsPreviewActive && wasSelectedId is not null)
                StatusMessage = $"Camera disconnected — switched to {fallback.Name}";
            SelectedDevice = fallback; // OnSelectedDeviceChanged restarts the preview
            return;
        }

        // No cameras left.
        if (IsPreviewActive)
        {
            _capture.StopPreviewAsync();
            IsPreviewActive = false;
            StatusMessage = "No camera connected.";
        }
        SelectedDevice = null;
    }

    // ------------------------------------------------------------------ //
    // Capture
    // ------------------------------------------------------------------ //

    [RelayCommand(CanExecute = nameof(CanTakePhoto))]
    private void TakePhoto()
    {
        // A second click while a countdown is running cancels it.
        if (_photoCountdownTimer is not null)
        {
            CancelPhotoCountdown();
            return;
        }

        if (_timerSeconds > 0)
            StartPhotoCountdown();
        else
            _ = CapturePhotoAsync();
    }

    private void StartPhotoCountdown()
    {
        _countdownRemaining = _timerSeconds;
        IsCountingDown = true;
        UpdateCountdownText();
        _photoCountdownTimer = new Timer(
            _ => Dispatcher.UIThread.Post(OnPhotoCountdownTick),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void OnPhotoCountdownTick()
    {
        if (_photoCountdownTimer is null)
            return;

        _countdownRemaining--;
        if (_countdownRemaining <= 0)
        {
            StopPhotoCountdown();
            _ = CapturePhotoAsync();
        }
        else
        {
            UpdateCountdownText();
        }
    }

    private void UpdateCountdownText()
    {
        CountdownText = _countdownRemaining.ToString();
        StatusMessage = $"Taking photo in {_countdownRemaining}…";
    }

    private void CancelPhotoCountdown()
    {
        if (_photoCountdownTimer is null && !IsCountingDown)
            return;
        StopPhotoCountdown();
        StatusMessage = "Timer cancelled.";
    }

    private void StopPhotoCountdown()
    {
        _photoCountdownTimer?.Dispose();
        _photoCountdownTimer = null;
        IsCountingDown = false;
        CountdownText = "";
    }

    private async Task CapturePhotoAsync()
    {
        var path = NextPhotoPath(_settings.Settings.PhotoDirectory);

        // White flash as visual confirmation of the capture.
        IsFlashing = true;
        try
        {
            await _capture.TakePhotoAsync(path);
            ShutterSound.Play(); // camera-like shutter click (Kamoso-style)
            StatusMessage = $"Photo saved to {Path.GetFileName(path)}";
            ShowToast($"Photo saved to {Path.GetFileName(path)}");
            NotificationService.Notify("Photo taken", Path.GetFileName(path));
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not take a photo: {ex.Message}";
        }
        finally
        {
            await Task.Delay(80);
            IsFlashing = false;
        }
    }

    private bool CanTakePhoto() => IsPreviewActive && !IsRecording && !IsBurstActive;

    private string NextPhotoPath(string directory)
    {
        Directory.CreateDirectory(directory);
        var ext = _photoFormat == "png" ? "png" : "jpg";
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(directory, $"picture_{stamp}.{ext}");
        var index = 1;
        while (File.Exists(path))
            path = Path.Combine(directory, $"picture_{stamp}_{index++}.{ext}");
        return path;
    }

    [RelayCommand(CanExecute = nameof(CanToggleBurst))]
    private void ToggleBurst()
    {
        if (_burstTimer is null)
        {
            if (!HasEnoughDiskSpace(_settings.Settings.PhotoDirectory))
                return;
            CancelPhotoCountdown();
            _burstTaken = 0;
            IsBurstActive = true;
            BurstButtonText = "Burst: On";
            StatusMessage = _burstCount > 0
                ? $"Burst mode — up to {_burstCount} photos, every {_burstIntervalSeconds:0.#}s."
                : $"Burst mode — a photo every {_burstIntervalSeconds:0.#}s.";
            _burstTimer = new Timer(
                _ => Dispatcher.UIThread.Post(() =>
                {
                    if (_burstCount > 0 && _burstTaken >= _burstCount)
                    {
                        StopBurst();
                        StatusMessage = $"Burst finished — {_burstTaken} photos taken.";
                        return;
                    }
                    _burstTaken++;
                    _ = CapturePhotoAsync();
                }),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(_burstIntervalSeconds * 1000));
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
            if (!HasEnoughDiskSpace(_settings.Settings.VideoDirectory))
                return;
            StopBurst();
            CancelPhotoCountdown();
            var directory = _settings.Settings.VideoDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"video_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");

            try
            {
                await _capture.StartRecordingAsync(path);
                IsRecording = true;
                _recordingPath = path;
                _recordingStopwatch.Restart();
                _recordingTimer = new Timer(
                    _ => Dispatcher.UIThread.Post(() =>
                    {
                        RecordingTime = _recordingStopwatch.Elapsed.ToString(@"mm\:ss");
                        RecordingSize = FormatFileSize(_recordingPath);
                    }),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1));
                StatusMessage = "Recording…";
                ShowToast("Recording started");
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
            RecordingSize = "";
            _recordingPath = null;
            StatusMessage = "Recording saved.";
            ShowToast("Recording saved");
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
        var all = _mediaLibrary.LoadMedia(_settings.Settings.PhotoDirectory)
            .Concat(_mediaLibrary.LoadMedia(_settings.Settings.VideoDirectory));

        // Filter: all, photos or videos.
        var filtered = SelectedGalleryFilter switch
        {
            "Photos" => all.Where(i => !i.IsVideo),
            "Videos" => all.Where(i => i.IsVideo),
            _ => all
        };

        // Sort: newest, oldest or by name.
        var sorted = SelectedGallerySort switch
        {
            "Oldest" => filtered.OrderBy(i => i.ModifiedAt),
            "Name" => filtered.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(i => i.ModifiedAt)
        };

        GalleryItems.Clear();
        foreach (var item in sorted.Take(60))
            GalleryItems.Add(item);

        UpdateGalleryEmptyHint();
        _ = GenerateGalleryThumbnailsAsync();
    }

    partial void OnSelectedGalleryFilterChanged(string value) => RefreshGallery();

    partial void OnSelectedGallerySortChanged(string value) => RefreshGallery();

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

    [RelayCommand(CanExecute = nameof(CanPlaySelected))]
    private void PlaySelected()
    {
        if (SelectedGalleryItem is not null)
            OpenWithDefaultApp(SelectedGalleryItem.Path);
    }

    private bool CanPlaySelected() => SelectedGalleryItem is { IsVideo: true };

    /// <summary>Opens the selected gallery item: photos in the in-app viewer, videos with the system player.</summary>
    [RelayCommand]
    private void OpenSelected()
    {
        var item = SelectedGalleryItem;
        if (item is null)
            return;
        if (item.IsVideo)
        {
            OpenWithDefaultApp(item.Path);
            return;
        }
        OpenPhotoViewer(item.Path);
    }

    /// <summary>Opens a photo in the in-app full-size viewer (zoom/pan).</summary>
    private void OpenPhotoViewer(string path)
    {
        var owner = MainWindow;
        if (owner is null)
            return;
        try
        {
            new PhotoViewerWindow(path).Show(owner);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open photo: {ex.Message}";
        }
    }

    /// <summary>Opens a file or folder with the system default application (xdg-open).</summary>
    private void OpenWithDefaultApp(string path)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open: {ex.Message}";
        }
    }

    /// <summary>Shows a brief overlay toast that auto-hides.</summary>
    private void ShowToast(string message)
    {
        ToastText = message;
        IsToastVisible = true;
        _toastTimer?.Dispose();
        _toastTimer = new Timer(
            _ => Dispatcher.UIThread.Post(() => IsToastVisible = false),
            null,
            TimeSpan.FromMilliseconds(2500),
            TimeSpan.FromMilliseconds(-1));
    }

    [RelayCommand]
    private void ToggleMirror() => Mirrored = !Mirrored;

    [RelayCommand]
    private void ToggleEffectsPanel() => ShowEffectsPanel = !ShowEffectsPanel;

    [RelayCommand]
    private void ToggleFullScreen()
    {
        var w = MainWindow;
        if (w is null)
            return;
        w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Maximized : WindowState.FullScreen;
    }

    /// <summary>Adjusts the digital zoom by half a step (used by the mouse wheel).</summary>
    public void AdjustZoom(double wheelDelta)
    {
        var current = _capture.Zoom;
        var next = Math.Clamp(current + (wheelDelta > 0 ? 0.5 : -0.5), 1.0, 4.0);
        if (Math.Abs(next - current) < 0.001)
            return;
        SelectedZoom = MapZoomLabel(next); // OnSelectedZoomChanged rebuilds + persists
    }

    private async Task LoadAudioDevicesAsync()
    {
        var devices = await Task.Run(() => _capture.GetAudioDevices());
        var items = new List<string> { "Default" };
        items.AddRange(devices);

        // Same in-place merge as the resolution list — never clear the collection,
        // so a still-present device keeps its selection in the ComboBox.
        for (var i = AudioDevices.Count - 1; i >= 0; i--)
        {
            if (!items.Contains(AudioDevices[i]))
                AudioDevices.RemoveAt(i);
        }
        foreach (var item in items)
        {
            if (!AudioDevices.Contains(item))
                AudioDevices.Add(item);
        }

        var saved = _settings.Settings.AudioDevice;
        var target = string.IsNullOrEmpty(saved) ? "Default" : (items.Contains(saved) ? saved : "Default");
        if (!string.Equals(SelectedAudioDevice, target, StringComparison.Ordinal))
            SelectedAudioDevice = target;
    }

    partial void OnSelectedAudioDeviceChanged(string value)
    {
        var device = value == "Default" ? "" : value;
        _settings.Settings.AudioDevice = device;
        _settings.Save();
        _capture.AudioDevice = device;
    }

    private static string FormatFileSize(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        var bytes = new FileInfo(path).Length;
        return bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.#} MB"
            : $"{bytes / 1024.0:0.#} KB";
    }

    /// <summary>Debounces rapid file events (e.g. burst captures) and refreshes the gallery.</summary>
    private void OnMediaCollectionChanged()
    {
        _galleryRefreshDebounce?.Dispose();
        _galleryRefreshDebounce = new Timer(
            _ => Dispatcher.UIThread.Post(RefreshGallery),
            null,
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(-1));
    }

    private void UpdateGalleryEmptyHint() => ShowGalleryEmptyHint = GalleryItems.Count == 0 && !ShowEffectsPanel;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var items = SelectedItems();
        if (items.Count == 0)
            return;

        var owner = MainWindow;
        if (owner is null)
            return;

        var confirmed = await new ConfirmDialog("Move to trash",
            items.Count == 1 ? $"Move \"{items[0].Name}\" to the trash?" : $"Move {items.Count} items to the trash?").ShowDialog<bool>(owner);
        if (!confirmed)
            return;

        var trashed = 0;
        foreach (var item in items)
        {
            if (TrashService.Trash(item.Path))
            {
                GalleryItems.Remove(item);
                trashed++;
            }
        }
        SelectedGalleryItems.Clear();
        UpdateGalleryEmptyHint();
        ShowToast(trashed == 1 ? "Moved to trash" : $"Moved {trashed} items to trash");
    }

    private bool CanDeleteSelected() => SelectedGalleryItem is not null || SelectedGalleryItems.Count > 0;

    /// <summary>All selected items (multi-selection), falling back to the single selection.</summary>
    private List<MediaItem> SelectedItems()
    {
        if (SelectedGalleryItems.Count > 0)
            return SelectedGalleryItems.ToList();
        return SelectedGalleryItem is null ? new List<MediaItem>() : new List<MediaItem> { SelectedGalleryItem };
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task RenameSelectedAsync()
    {
        var item = SelectedGalleryItem;
        if (item is null)
            return;

        var owner = MainWindow;
        if (owner is null)
            return;

        var dialog = new RenameDialog(Path.GetFileNameWithoutExtension(item.Name));
        var newBase = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(newBase))
            return;

        var dir = Path.GetDirectoryName(item.Path) ?? "";
        var newPath = Path.Combine(dir, newBase + item.Extension);
        if (string.Equals(newPath, item.Path, StringComparison.Ordinal))
            return;

        try
        {
            if (File.Exists(newPath))
            {
                StatusMessage = "A file with that name already exists.";
                return;
            }
            File.Move(item.Path, newPath);
            StatusMessage = $"Renamed to {Path.GetFileName(newPath)}";
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not rename: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task CopySelectedAsync()
    {
        var items = SelectedItems();
        if (items.Count == 0)
            return;

        var dest = await PickFolderAsync();
        if (dest is null)
            return;

        try
        {
            foreach (var item in items)
            {
                var target = UniquePath(Path.Combine(dest, item.Name));
                File.Copy(item.Path, target);
            }
            StatusMessage = items.Count == 1 ? $"Copied to {Path.GetFileName(dest)}" : $"Copied {items.Count} items";
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not copy: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task MoveSelectedAsync()
    {
        var items = SelectedItems();
        if (items.Count == 0)
            return;

        var dest = await PickFolderAsync();
        if (dest is null)
            return;

        try
        {
            foreach (var item in items)
            {
                var target = UniquePath(Path.Combine(dest, item.Name));
                File.Move(item.Path, target);
            }
            SelectedGalleryItems.Clear();
            StatusMessage = items.Count == 1 ? $"Moved to {Path.GetFileName(dest)}" : $"Moved {items.Count} items";
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not move: {ex.Message}";
        }
    }

    private static Window? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    private static async Task<string?> PickFolderAsync()
    {
        var owner = MainWindow;
        if (owner is null)
            return null;

        var dirs = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose a folder" });
        return dirs.Count > 0 ? dirs[0].Path.LocalPath : null;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        while (File.Exists(Path.Combine(dir, $"{name}-{i}{ext}")))
            i++;
        return Path.Combine(dir, $"{name}-{i}{ext}");
    }

    [RelayCommand]
    private void OpenMediaFolder()
    {
        var directory = _settings.Settings.PhotoDirectory;
        if (Directory.Exists(directory))
            OpenWithDefaultApp(directory);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var owner = MainWindow;
        if (owner is null)
            return;

        var dialog = new SettingsWindow(_settings.Settings.PhotoDirectory, _settings.Settings.VideoDirectory,
            SelectedBurstInterval, SelectedBurstCount);
        var saved = await dialog.ShowDialog<bool>(owner);
        if (!saved)
            return;

        _settings.Settings.PhotoDirectory = dialog.PhotoDirectory;
        _settings.Settings.VideoDirectory = dialog.VideoDirectory;
        SelectedBurstInterval = dialog.BurstInterval; // persists via handler
        SelectedBurstCount = dialog.BurstCount;       // persists via handler
        _settings.Save();
        _mediaLibrary.Watch(_settings.Settings.PhotoDirectory, _settings.Settings.VideoDirectory);
        RefreshGallery();
        ShowToast("Settings saved");
        StatusMessage = "Settings saved.";
    }
}