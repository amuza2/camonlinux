using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using camonlinux.Capture;
using camonlinux.Models;

namespace camonlinux.Tests;

/// <summary>
/// A no-op <see cref="ICaptureService"/> so the view models can be tested without a
/// camera, GStreamer or a display. Members the tests care about are settable; the rest
/// just record what was asked of them.
/// </summary>
internal sealed class FakeCaptureService : ICaptureService
{
    public event EventHandler<CameraFrame>? FrameReady;
    public event EventHandler<string>? ErrorOccurred;

    public IFrameProcessor? FrameProcessor { get; set; }
    public bool IsPreviewActive { get; set; }
    public bool IsRecording { get; set; }
    public bool Mirrored { get; set; }
    public bool MicMuted { get; set; }
    public string Resolution { get; set; } = "";
    public string RecordQuality { get; set; } = "medium";
    public long MaxFileSizeMB { get; set; }
    public string Rotation { get; set; } = "auto";
    public double Zoom { get; set; } = 1.0;
    public string PhotoFormat { get; set; } = "jpeg";
    public bool ShowTimestamp { get; set; }
    public (byte R, byte G, byte B) MaskBackground { get; set; }
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Saturation { get; set; }
    public int Sharpness { get; set; }
    public int Gain { get; set; }
    public int BacklightCompensation { get; set; }
    public bool WhiteBalanceAuto { get; set; }
    public int WhiteBalanceTemperature { get; set; }
    public bool ExposureAuto { get; set; }
    public int ExposureValue { get; set; }
    public bool FocusAuto { get; set; }
    public int FocusValue { get; set; }
    public string AudioDevice { get; set; } = "";
    public string Effect { get; set; } = "";
    public CameraDevice? CurrentDevice { get; set; }
    public IReadOnlyList<CameraDevice> Devices { get; set; } = Array.Empty<CameraDevice>();

    /// <summary>Paths passed to <see cref="TakePhotoAsync"/>, in order.</summary>
    public List<string> TakenPhotoPaths { get; } = new();

    public int ApplyCameraControlsCount { get; private set; }

    public IReadOnlyList<string> GetAudioDevices() => Array.Empty<string>();

    public void ApplyCameraControls() => ApplyCameraControlsCount++;

    public IReadOnlySet<string> GetSupportedControls(CameraDevice device)
        => new HashSet<string>(StringComparer.Ordinal);

    public bool ApplyEffectIntensity(double value) => false;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<CameraDevice>> RefreshDevicesAsync()
        => Task.FromResult(Devices);

    public Task StartPreviewAsync(CameraDevice device)
    {
        CurrentDevice = device;
        IsPreviewActive = true;
        return Task.CompletedTask;
    }

    public Task StopPreviewAsync()
    {
        IsPreviewActive = false;
        return Task.CompletedTask;
    }

    public Task TakePhotoAsync(string path)
    {
        TakenPhotoPaths.Add(path);
        return Task.CompletedTask;
    }

    public Task StartRecordingAsync(string path)
    {
        IsRecording = true;
        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        IsRecording = false;
        return Task.CompletedTask;
    }

    public Task<string?> RenderEffectThumbnailAsync(
        string effect, string sampleImagePath, string outputPath, int width, int height)
        => Task.FromResult<string?>(null);

    public bool IsElementAvailable(string elementName) => true;

    public IReadOnlyList<string> GetSupportedModes(CameraDevice device)
        => Array.Empty<string>();

    /// <summary>Raises <see cref="FrameReady"/> — lets a test drive frames into the view.</summary>
    public void EmitFrame(CameraFrame frame) => FrameReady?.Invoke(this, frame);

    /// <summary>Raises <see cref="ErrorOccurred"/>.</summary>
    public void EmitError(string message) => ErrorOccurred?.Invoke(this, message);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
