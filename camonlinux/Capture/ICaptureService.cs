using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using camonlinux.Models;

namespace camonlinux.Capture;

/// <summary>
/// Abstraction over the camera capture backend. The current implementation is
/// GStreamer-based (<see cref="GStreamerCaptureService"/>), but isolating the
/// contract here keeps the UI/MVVM layer backend-agnostic.
/// </summary>
public interface ICaptureService : IAsyncDisposable
{
    /// <summary>Raised on the streaming thread whenever a new preview frame is available.</summary>
    event EventHandler<CameraFrame>? FrameReady;

    /// <summary>Raised when the capture backend encounters a recoverable error.</summary>
    event EventHandler<string>? ErrorOccurred;

    bool IsPreviewActive { get; }
    bool IsRecording { get; }
    bool Mirrored { get; set; }

    /// <summary>Mutes the microphone during recordings. Applies live while recording.</summary>
    bool MicMuted { get; set; }

    /// <summary>Selected capture mode label ("" / "Default" = camera default).</summary>
    string Resolution { get; set; }

    /// <summary>Recording quality key: "low", "medium" or "high".</summary>
    string RecordQuality { get; set; }

    /// <summary>Split recordings at this size (MB); 0 = no limit.</summary>
    long MaxFileSizeMB { get; set; }

    /// <summary>Image rotation as a videoflip direction: "auto", "90r", "180", "90l".</summary>
    string Rotation { get; set; }

    /// <summary>Digital zoom factor (1.0 = none).</summary>
    double Zoom { get; set; }

    /// <summary>Photo file format: "jpeg" or "png".</summary>
    string PhotoFormat { get; set; }

    /// <summary>Stamp photos and recordings with the date &amp; time.</summary>
    bool ShowTimestamp { get; set; }

    /// <summary>v4l2 brightness control (0-255, 128 = default).</summary>
    int Brightness { get; set; }

    /// <summary>v4l2 contrast control (0-255, 128 = default).</summary>
    int Contrast { get; set; }

    /// <summary>v4l2 saturation control (0-255, 128 = default).</summary>
    int Saturation { get; set; }

    /// <summary>v4l2 sharpness control (0-255, 128 = default).</summary>
    int Sharpness { get; set; }

    /// <summary>v4l2 gain control (0-255, 0 = default).</summary>
    int Gain { get; set; }

    /// <summary>v4l2 backlight compensation (0-1).</summary>
    int BacklightCompensation { get; set; }

    /// <summary>Automatic white balance (true) or manual temperature (false).</summary>
    bool WhiteBalanceAuto { get; set; }

    /// <summary>Manual white balance temperature in Kelvin (2000-7500).</summary>
    int WhiteBalanceTemperature { get; set; }

    /// <summary>Automatic exposure (true) or manual exposure time (false).</summary>
    bool ExposureAuto { get; set; }

    /// <summary>Manual exposure time (3-2047).</summary>
    int ExposureValue { get; set; }

    /// <summary>Continuous auto-focus (true) or manual focus distance (false).</summary>
    bool FocusAuto { get; set; }

    /// <summary>Manual focus position (0-255).</summary>
    int FocusValue { get; set; }

    /// <summary>PulseAudio source name to record from ("" = default).</summary>
    string AudioDevice { get; set; }

    /// <summary>Lists available PulseAudio/PipeWire sources (excluding monitors).</summary>
    IReadOnlyList<string> GetAudioDevices();

    /// <summary>Applies the current camera controls live via v4l2-ctl.</summary>
    void ApplyCameraControls();

    /// <summary>
    /// Names of the v4l2 controls the given device exposes (e.g. "sharpness",
    /// "white_balance_automatic"). Used to show only the controls a camera has.
    /// </summary>
    IReadOnlySet<string> GetSupportedControls(CameraDevice device);

    /// <summary>GStreamer filter chain applied to preview, photos and recordings (empty = none).</summary>
    string Effect { get; set; }

    /// <summary>
    /// Live-adjusts the current effect's intensity on the running pipeline
    /// (e.g. videobalance saturation) without rebuilding it. Returns true if applied.
    /// </summary>
    bool ApplyEffectIntensity(double value);

    CameraDevice? CurrentDevice { get; }
    IReadOnlyList<CameraDevice> Devices { get; }

    Task InitializeAsync();
    Task<IReadOnlyList<CameraDevice>> RefreshDevicesAsync();
    Task StartPreviewAsync(CameraDevice device);
    Task StopPreviewAsync();
    Task TakePhotoAsync(string path);
    Task StartRecordingAsync(string path);
    Task StopRecordingAsync();

    /// <summary>
    /// Renders a small preview image of the given GStreamer effect applied to
    /// <paramref name="sampleImagePath"/>. Returns the output path on success, or
    /// null if the effect could not be built (e.g. missing plugin).
    /// </summary>
    Task<string?> RenderEffectThumbnailAsync(string effect, string sampleImagePath, string outputPath, int width, int height);

    /// <summary>
    /// True if the named GStreamer element (e.g. <c>frei0r.cartoon</c>) is
    /// registered on this system. Used to hide effects whose plugin is missing.
    /// </summary>
    bool IsElementAvailable(string elementName);

    /// <summary>
    /// Labels of the capture modes (e.g. "1920×1080 @ 30") the device supports.
    /// </summary>
    IReadOnlyList<string> GetSupportedModes(CameraDevice device);
}
