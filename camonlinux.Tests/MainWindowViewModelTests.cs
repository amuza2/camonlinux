using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using camonlinux.Services;
using camonlinux.ViewModels;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// The main view model exposes computed properties (e.g. <c>RecordButtonText</c>) that
/// the XAML binds to directly. Those only update when the source property raises a
/// change notification for them, which is easy to forget — and the failure is silent:
/// the button simply never changes. These tests pin that wiring down.
/// </summary>
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _configDir = Path.Combine(
        Path.GetTempPath(), "camonlinux_vm_" + Guid.NewGuid().ToString("N"));

    private readonly FakeCaptureService _capture = new();
    private readonly SettingsService _settings;
    private readonly MediaLibraryService _mediaLibrary = new();

    public MainWindowViewModelTests()
    {
        _settings = new SettingsService(_configDir);
    }

    public void Dispose()
    {
        _settings.Dispose();
        _mediaLibrary.Dispose();
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

    private MainWindowViewModel CreateViewModel() => new(_capture, _settings, _mediaLibrary);

    [Fact]
    public void RecordButtonText_StartsAsRecord()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsRecording);
        Assert.Equal("Record", vm.RecordButtonText);
    }

    [Fact]
    public void RecordButtonText_TurnsIntoStopWhileRecordingAndBackAgain()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsRecording = true;

        Assert.Equal("Stop", vm.RecordButtonText);
        Assert.Contains(nameof(MainWindowViewModel.RecordButtonText), changed);

        changed.Clear();
        vm.IsRecording = false;

        Assert.Equal("Record", vm.RecordButtonText);
        Assert.Contains(nameof(MainWindowViewModel.RecordButtonText), changed);
    }

    [Fact]
    public void IsRecording_ReevaluatesTheToggleRecordingCommand()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.ToggleRecordingCommand.CanExecuteChanged += (_, _) => raised = true;

        vm.IsRecording = true;

        Assert.True(raised, "The Record/Stop button state must be re-queried when recording starts or stops.");
    }

    [Fact]
    public void RecordButtonText_DoesNotChangeOnUnrelatedProperties()
    {
        // A cheap guard against over-notifying (which would cause needless re-renders).
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.StatusMessage = "Something happened";

        Assert.DoesNotContain(nameof(MainWindowViewModel.RecordButtonText), changed);
    }

    [Fact]
    public void Constructor_DoesNotNeedAGraphicalEnvironment()
    {
        // Guards the test suite itself: this view model must stay constructible headless.
        var vm = CreateViewModel();

        Assert.NotNull(vm.MaskPipeline);
        Assert.NotNull(vm.MaskEditor);
        Assert.Equal("Record", vm.RecordButtonText);
    }

    // ------------------------------------------------------------------ //
    // Right panel width (drag-to-resize)
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(0, MainWindowViewModel.MinPanelWidth)]
    [InlineData(-500, MainWindowViewModel.MinPanelWidth)]
    [InlineData(199, MainWindowViewModel.MinPanelWidth)]
    [InlineData(200, 200)]
    [InlineData(240, 240)]
    [InlineData(640, MainWindowViewModel.MaxPanelWidth)]
    [InlineData(5000, MainWindowViewModel.MaxPanelWidth)]
    // A hand-edited or corrupted settings file must not produce a broken layout.
    [InlineData(double.NaN, MainWindowViewModel.MinPanelWidth)]
    [InlineData(double.PositiveInfinity, MainWindowViewModel.MinPanelWidth)]
    [InlineData(double.NegativeInfinity, MainWindowViewModel.MinPanelWidth)]
    public void ClampPanelWidth_KeepsTheWidthInRange(double requested, double expected)
    {
        Assert.Equal(expected, MainWindowViewModel.ClampPanelWidth(requested));
    }

    [Fact]
    public void RightPanelWidth_DefaultsToTheDesignWidth()
    {
        var vm = CreateViewModel();

        Assert.Equal(240d, vm.RightPanelWidth);
    }

    [Fact]
    public void RightPanelWidth_RestoresTheSavedWidth()
    {
        _settings.Settings.RightPanelWidth = 384;
        _settings.Save();

        var vm = CreateViewModel();

        Assert.Equal(384d, vm.RightPanelWidth);
    }

    [Fact]
    public void RightPanelWidth_RestoresACorruptSavedWidthAsAUsableOne()
    {
        _settings.Settings.RightPanelWidth = 99999;
        _settings.Save();

        var vm = CreateViewModel();

        Assert.Equal(MainWindowViewModel.MaxPanelWidth, vm.RightPanelWidth);
    }

    [Fact]
    public void RightPanelWidth_IsPersistedWhenDragged()
    {
        var vm = CreateViewModel();

        vm.RightPanelWidth = 360;
        _settings.Save(); // flush the debounced write

        using var reloaded = new SettingsService(_configDir);
        Assert.Equal(360d, reloaded.Settings.RightPanelWidth);
    }

    [Fact]
    public void RightPanelWidth_NotifiesSoThePanelRebinds()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RightPanelWidth = 300;

        Assert.Contains(nameof(MainWindowViewModel.RightPanelWidth), changed);
    }

    [Theory]
    // Dragging the grip to the left (positive) widens the panel...
    [InlineData(300, 100, 1920, 400)]
    // ...and dragging it to the right narrows it.
    [InlineData(300, -100, 1920, 200)]
    // Clamped at both ends.
    [InlineData(300, -500, 1920, MainWindowViewModel.MinPanelWidth)]
    [InlineData(600, 500, 1920, MainWindowViewModel.MaxPanelWidth)]
    // On a narrow window the preview limit wins over the drag.
    [InlineData(300, 500, 700, 400)]
    public void PanelWidthForDrag_ResizesInTheExpectedDirection(
        double startWidth, double movedLeft, double windowWidth, double expected)
    {
        Assert.Equal(expected, MainWindowViewModel.PanelWidthForDrag(startWidth, movedLeft, windowWidth));
    }

    [Theory]
    [InlineData(1920, MainWindowViewModel.MaxPanelWidth)]
    [InlineData(940, 640)]
    [InlineData(800, 500)]
    [InlineData(640, 340)]
    // A window too small to fit the preview constraint still yields a usable panel.
    [InlineData(300, MainWindowViewModel.MinPanelWidth)]
    [InlineData(0, MainWindowViewModel.MinPanelWidth)]
    public void MaxPanelWidthForWindow_AlwaysLeavesRoomForThePreview(double windowWidth, double expected)
    {
        Assert.Equal(expected, MainWindowViewModel.MaxPanelWidthForWindow(windowWidth));
    }

    [Fact]
    public void DefaultPanelWidth_IsAUsableWidth()
    {
        // The double-click-to-reset target must always be representable.
        Assert.Equal(
            MainWindowViewModel.DefaultPanelWidth,
            MainWindowViewModel.ClampPanelWidth(MainWindowViewModel.DefaultPanelWidth));
    }

    // ------------------------------------------------------------------ //
    // Startup settings must reach the capture backend
    // ------------------------------------------------------------------ //

    [Fact]
    public void Mirrored_IsPushedToTheCaptureServiceOnStartup()
    {
        // The Mirror toggle was the visible symptom: the view model restored Mirrored
        // from settings but never pushed it to the capture service. The service kept its
        // own default (true), so the preview stayed mirrored while the toggle showed
        // "off" — and the first click re-applied true, changing nothing.
        _settings.Settings.Mirrored = false;

        var vm = CreateViewModel();

        Assert.False(vm.Mirrored);
        Assert.False(_capture.Mirrored);
    }

    [Fact]
    public void MicEnabled_IsPushedToTheCaptureServiceOnStartup()
    {
        // Same omission as Mirrored: MicMuted was only ever assigned from the toggle.
        _settings.Settings.MicEnabled = false;

        _ = CreateViewModel();

        Assert.True(_capture.MicMuted);
    }

    [Fact]
    public void TogglingMirror_UpdatesTheCaptureServiceBothWays()
    {
        var vm = CreateViewModel();

        vm.Mirrored = false;
        Assert.False(_capture.Mirrored);

        vm.Mirrored = true;
        Assert.True(_capture.Mirrored);
    }

    [Fact]
    public void Constructor_PushesEveryRestoredSettingToTheCaptureService()
    {
        // Exhaustive guard for the whole class of bug above: every persisted setting the
        // view model restores has to be handed to the backend at construction, or the UI
        // and the camera silently disagree until that control is touched.
        var s = _settings.Settings;
        s.Mirrored = false;
        s.MicEnabled = false;
        s.Rotation = "180";
        s.Zoom = 2;
        s.PhotoFormat = "png";
        s.ShowTimestamp = true;
        s.Brightness = 10;
        s.Contrast = 20;
        s.Saturation = 30;
        s.Sharpness = 40;
        s.Gain = 50;
        s.BacklightCompensation = 1;
        s.WhiteBalanceAuto = false;
        s.WhiteBalanceTemperature = 5000;
        s.ExposureAuto = false;
        s.ExposureValue = 300;
        s.FocusAuto = false;
        s.FocusValue = 77;
        s.AudioDevice = "alsa_input.usb";
        s.Resolution = "640×480 @ 30";
        s.RecordQuality = "high";
        s.MaxFileSizeMB = 1024;
        s.VirtualCamBackground = "Green";

        _ = CreateViewModel();

        Assert.False(_capture.Mirrored);
        Assert.True(_capture.MicMuted);
        Assert.Equal("180", _capture.Rotation);
        Assert.Equal(2d, _capture.Zoom);
        Assert.Equal("png", _capture.PhotoFormat);
        Assert.True(_capture.ShowTimestamp);
        Assert.Equal(10, _capture.Brightness);
        Assert.Equal(20, _capture.Contrast);
        Assert.Equal(30, _capture.Saturation);
        Assert.Equal(40, _capture.Sharpness);
        Assert.Equal(50, _capture.Gain);
        Assert.Equal(1, _capture.BacklightCompensation);
        Assert.False(_capture.WhiteBalanceAuto);
        Assert.Equal(5000, _capture.WhiteBalanceTemperature);
        Assert.False(_capture.ExposureAuto);
        Assert.Equal(300, _capture.ExposureValue);
        Assert.False(_capture.FocusAuto);
        Assert.Equal(77, _capture.FocusValue);
        Assert.Equal("alsa_input.usb", _capture.AudioDevice);
        Assert.Equal("640×480 @ 30", _capture.Resolution);
        Assert.Equal("high", _capture.RecordQuality);
        Assert.Equal(1024L, _capture.MaxFileSizeMB);
        // "Green" is the mask background, shared with the virtual webcam.
        Assert.Equal((byte)0, _capture.MaskBackground.R);
        Assert.Equal((byte)255, _capture.MaskBackground.G);
        Assert.Equal((byte)0, _capture.MaskBackground.B);
    }

    // ------------------------------------------------------------------ //
    // Gallery thumbnails track the panel width
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(200, 64, 36)]   // 56px requested, clamped up to the 64px minimum
    [InlineData(240, 67, 38)]   // the default width
    [InlineData(400, 112, 63)]
    [InlineData(640, 160, 90)]  // clamped at the rendered resolution
    public void GalleryThumbWidth_ScalesWithThePanelWidth(
        double panelWidth, double expectedWidth, double expectedHeight)
    {
        var vm = CreateViewModel();
        vm.RightPanelWidth = panelWidth;

        Assert.Equal(expectedWidth, vm.GalleryThumbWidth);
        Assert.Equal(expectedHeight, vm.GalleryThumbHeight);
    }

    [Fact]
    public void ChangingThePanelWidth_NotifiesTheThumbnailSize()
    {
        // Without these notifications the thumbnails keep their original size when the
        // panel is dragged — the panel grows but its content does not.
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RightPanelWidth = 400;

        Assert.Contains(nameof(MainWindowViewModel.GalleryThumbWidth), changed);
        Assert.Contains(nameof(MainWindowViewModel.GalleryThumbHeight), changed);
    }

    [Fact]
    public void GalleryThumbWidth_NeverShrinksAsThePanelGrows()
    {
        var vm = CreateViewModel();
        var previous = 0d;

        for (var width = MainWindowViewModel.MinPanelWidth;
             width <= MainWindowViewModel.MaxPanelWidth;
             width += 20)
        {
            vm.RightPanelWidth = width;

            Assert.True(
                vm.GalleryThumbWidth >= previous,
                $"Thumbnail width shrank from {previous} to {vm.GalleryThumbWidth} at panel width {width}.");
            previous = vm.GalleryThumbWidth;
        }
    }

    [Fact]
    public void GalleryThumbHeight_KeepsTheRenderedAspectRatio()
    {
        var vm = CreateViewModel();

        foreach (var width in new[] { 200d, 300d, 480d, 640d })
        {
            vm.RightPanelWidth = width;
            var ratio = vm.GalleryThumbWidth / vm.GalleryThumbHeight;

            Assert.InRange(ratio, 1.70, 1.85); // ~16:9, allowing for rounding
        }
    }
}
