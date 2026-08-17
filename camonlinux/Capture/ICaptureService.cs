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

    /// <summary>GStreamer filter chain applied to preview, photos and recordings (empty = none).</summary>
    string Effect { get; set; }

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
