using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using camonlinux.Models;
using GObject;
using Gst;
using GstApp;
using GstAudio;
using GstBase;
using GstVideo;
using SkiaSharp;
using Task = System.Threading.Tasks.Task;
using ValueTask = System.Threading.Tasks.ValueTask;

namespace camonlinux.Capture;

/// <summary>
/// GStreamer-backed camera service for Linux (V4L2).
///
/// Uses two separate pipelines that never run at the same time (a camera device
/// can only be opened once):
/// <list type="bullet">
///   <item>
///     <description><b>Preview</b>:
///     <c>v4l2src ! videoconvert ! videoflip ! video/x-raw,format=BGRx ! appsink</c>
///     — frames are pulled with TryPullSample and published via <see cref="FrameReady"/>.</description>
///   </item>
///   <item>
///     <description><b>Recording</b>:
///     <c>v4l2src ! videoconvert ! videoflip ! x264enc ! matroskamux ! filesink</c>
///     — the preview is paused while recording and resumes afterwards.</description>
///   </item>
/// </list>
///
/// NOTE: a live preview *while* recording (via a tee) is not used because any
/// buffer-dropping gate (valve / identity drop-probability) stalls the tee and
/// freezes the preview branch — see the git history for the investigation.
/// Requires GStreamer + plugins on the host; see the README.
/// </summary>
public sealed class GStreamerCaptureService : ICaptureService
{
    private static bool s_gstInitialized;

    private Element? _previewPipeline;
    private AppSink? _appSink;
    private Element? _recordPipeline;
    private CameraDevice? _currentDevice;
    private CameraFrame? _latestFrame;
    private Gst.Bus? _bus;
    private CancellationTokenSource? _busWatchCts;
    private CancellationTokenSource? _frameLoopCts;
    private readonly object _frameLock = new();
    private readonly List<CameraDevice> _devices = new();

    public event EventHandler<CameraFrame>? FrameReady;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsPreviewActive => _previewPipeline is not null;
    public bool IsRecording => _recordPipeline is not null;
    public bool Mirrored { get; set; } = true;
    public CameraDevice? CurrentDevice => _currentDevice;
    public IReadOnlyList<CameraDevice> Devices => _devices;

    public Task InitializeAsync()
    {
        if (!s_gstInitialized)
        {
            try
            {
                // Each GirCore module must be initialized so its types are
                // registered — otherwise `GetByName(...) as AppSink` returns null
                // (the element is wrapped as a plain Element).
                //
                // Order matters: set up the DllImportResolver (GstBase + Gst),
                // call gst_init, THEN register the extension modules — GstVideo /
                // GstAudio emit GLib-CRITICALs if registered before gst_init.
                GstBase.Module.Initialize();
                Gst.Module.Initialize();

                var args = Array.Empty<string>();
                Gst.Functions.Init(ref args);

                GstApp.Module.Initialize();
                GstVideo.Module.Initialize();
                GstAudio.Module.Initialize();
                s_gstInitialized = true;
            }
            catch (Exception ex)
            {
                // GStreamer native libs missing — surface the message, the app
                // still launches and shows a friendly error instead of crashing.
                ErrorOccurred?.Invoke(this, $"GStreamer is not available: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<IReadOnlyList<CameraDevice>> RefreshDevicesAsync()
    {
        _devices.Clear();

        // Each UVC camera usually exposes TWO /dev/videoN nodes (a capture node and
        // a metadata node). Read the friendly name from sysfs and keep the lowest-
        // numbered node per name so each physical camera appears exactly once.
        var byName = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.GetFiles("/dev", "video*"))
            {
                var name = ReadFriendlyName(path);
                if (!byName.TryGetValue(name, out var existing) || NodeNumber(path) < NodeNumber(existing))
                    byName[name] = path;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enumerate cameras: {ex.Message}");
        }

        foreach (var (name, path) in byName)
            _devices.Add(new CameraDevice(path, name, path));

        return Task.FromResult<IReadOnlyList<CameraDevice>>(_devices);
    }

    private static string ReadFriendlyName(string path)
    {
        try
        {
            var sysfsName = Path.Combine("/sys/class/video4linux", Path.GetFileName(path), "name");
            if (File.Exists(sysfsName))
            {
                var name = File.ReadAllText(sysfsName).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // Fall through to the device node name.
        }

        return Path.GetFileName(path);
    }

    private static int NodeNumber(string path)
    {
        var fileName = Path.GetFileName(path);
        return int.TryParse(fileName.AsSpan(5), out var n) ? n : int.MaxValue;
    }

    // ------------------------------------------------------------------ //
    // Preview
    // ------------------------------------------------------------------ //

    public Task StartPreviewAsync(CameraDevice device)
    {
        StartPreviewInternal(device);
        return Task.CompletedTask;
    }

    public Task StopPreviewAsync()
    {
        StopPreviewInternal();
        return Task.CompletedTask;
    }

    private void StartPreviewInternal(CameraDevice device)
    {
        _currentDevice = device;
        StopPreviewInternal();
        BuildPreviewPipeline();
        _previewPipeline?.SetState(Gst.State.Playing);
    }

    private void StopPreviewInternal()
    {
        StopBusWatch();
        StopFrameLoop();

        _previewPipeline?.SetState(Gst.State.Null);
        _previewPipeline?.Dispose();
        _previewPipeline = null;
        _appSink = null;

        DisposeBus();

        lock (_frameLock)
            _latestFrame = null;
    }

    private void BuildPreviewPipeline()
    {
        if (_currentDevice is null)
            return;

        var flip = Mirrored ? "videoflip video-direction=horiz" : "videoflip video-direction=auto";
        var description =
            $"v4l2src device={_currentDevice.Path} ! videoconvert ! {flip} " +
            "! video/x-raw,format=BGRx ! appsink name=sink max-buffers=1 drop=true";

        try
        {
            _previewPipeline = Gst.Functions.ParseLaunch(description);
            var bin = (Gst.Bin)_previewPipeline;
            _appSink = bin.GetByName("sink") as AppSink
                ?? throw new InvalidOperationException("Failed to create the preview appsink.");

            _bus = _previewPipeline.GetBus();
            StartBusWatch();
            StartFrameLoop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Could not start the camera pipeline: {ex.Message}");
            StopPreviewInternal();
        }
    }

    public Task TakePhotoAsync(string path)
    {
        CameraFrame frame;
        lock (_frameLock)
        {
            frame = _latestFrame
                ?? throw new InvalidOperationException("No camera frame available yet.");
        }

        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(frame.Data, 0, bitmap.GetPixels(), frame.Data.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ //
    // Recording
    // ------------------------------------------------------------------ //

    public Task StartRecordingAsync(string path)
    {
        if (_recordPipeline is not null)
            return Task.CompletedTask; // already recording

        if (_currentDevice is null)
            throw new InvalidOperationException("No camera selected.");

        // A camera device can only be opened once, so stop the preview first.
        StopPreviewInternal();

        var flip = Mirrored ? "videoflip video-direction=horiz" : "videoflip video-direction=auto";
        var description =
            $"v4l2src device={_currentDevice.Path} ! videoconvert ! {flip} " +
            "! x264enc speed-preset=veryfast tune=zerolatency ! matroskamux " +
            $"! filesink name=filesink location=\"{path}\"";

        try
        {
            _recordPipeline = Gst.Functions.ParseLaunch(description);
            _bus = _recordPipeline.GetBus();
            StartBusWatch();
            _recordPipeline.SetState(Gst.State.Playing);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Could not start recording: {ex.Message}");
            StopRecordingPipeline();
        }

        return Task.CompletedTask;
    }

    public async Task StopRecordingAsync()
    {
        var pipeline = _recordPipeline;
        _recordPipeline = null;
        if (pipeline is not null)
        {
            StopBusWatch();

            // EOS lets x264enc flush and matroskamux write the final container.
            pipeline.SendEvent(Gst.Event.NewEos());
            using var bus = pipeline.GetBus();
            if (bus is not null)
            {
                using var _ = bus.TimedPopFiltered(
                    (Gst.ClockTime)2_000_000_000UL,
                    Gst.MessageType.Eos | Gst.MessageType.Error);
            }
            pipeline.SetState(Gst.State.Null);
            pipeline.Dispose();
        }

        DisposeBus();

        // Resume the live preview.
        if (_currentDevice is not null)
            StartPreviewInternal(_currentDevice);

        await Task.CompletedTask;
    }

    private void StopRecordingPipeline()
    {
        _recordPipeline?.SetState(Gst.State.Null);
        _recordPipeline?.Dispose();
        _recordPipeline = null;
        DisposeBus();
    }

    // ------------------------------------------------------------------ //
    // Frame loop
    // ------------------------------------------------------------------ //

    private void StartFrameLoop()
    {
        StopFrameLoop();
        _frameLoopCts = new CancellationTokenSource();
        var token = _frameLoopCts.Token;
        _ = Task.Run(() => FrameLoop(token), token);
    }

    private void StopFrameLoop()
    {
        _frameLoopCts?.Cancel();
        _frameLoopCts?.Dispose();
        _frameLoopCts = null;
    }

    // NOTE: GirCore's GStreamer signal events (e.g. appsink's `new-sample`) do not
    // fire reliably, so frames are pulled with TryPullSample on a background thread.
    private void FrameLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var sink = _appSink;
            if (sink is null)
                break;

            // Returns immediately when a sample is queued (max-buffers=1, drop=true),
            // otherwise blocks up to the 20ms timeout.
            using var sample = sink.TryPullSample((Gst.ClockTime)20_000_000UL);
            if (sample is null)
                continue;

            ProcessSample(sample);
        }
    }

    private void ProcessSample(Gst.Sample sample)
    {
        using var buffer = sample.GetBuffer();
        if (buffer is null)
            return;

        using var caps = sample.GetCaps();
        var width = 0;
        var height = 0;
        if (caps is not null)
        {
            var structure = caps.GetStructure(0);
            if (structure is not null)
            {
                structure.GetInt("width", out width);
                structure.GetInt("height", out height);
            }
        }

        var size = (int)buffer.GetSize();
        if (width <= 0 || height <= 0 || size <= 0)
            return;

        var data = new byte[size];
        buffer.Extract(0, data);

        // GStreamer BGRx has an unused alpha byte; force it opaque so the
        // bitmap renders correctly (and so SkiaSharp can treat it as opaque).
        for (var i = 3; i < size; i += 4)
            data[i] = 0xFF;

        var frame = new CameraFrame(data, width, height);
        lock (_frameLock)
            _latestFrame = frame;

        FrameReady?.Invoke(this, frame);
    }

    // ------------------------------------------------------------------ //
    // Bus / error handling
    // ------------------------------------------------------------------ //

    private void StartBusWatch()
    {
        StopBusWatch();
        _busWatchCts = new CancellationTokenSource();
        var token = _busWatchCts.Token;
        _ = Task.Run(() => BusLoop(token), token);
    }

    private void StopBusWatch()
    {
        _busWatchCts?.Cancel();
        _busWatchCts?.Dispose();
        _busWatchCts = null;
    }

    private void DisposeBus()
    {
        _bus?.Dispose();
        _bus = null;
    }

    private void BusLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var bus = _bus;
            if (bus is null)
                break;

            using var message = bus.Pop();
            if (message is null)
            {
                Thread.Sleep(100);
                continue;
            }

            if (message.Type == Gst.MessageType.Error)
            {
                var structure = message.GetStructure();
                // GStreamer error messages carry the text in the "debug" field
                // (plus a boxed GError); there is no plain "message" field.
                var text = structure?.GetString("debug")
                           ?? structure?.GetString("message")
                           ?? "Unknown GStreamer error";
                if (text.Contains("ermission", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("annot open", StringComparison.OrdinalIgnoreCase))
                {
                    text += " — add your user to the 'video' group, then log out and back in: sudo usermod -aG video $USER";
                }
                ErrorOccurred?.Invoke(this, text);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_recordPipeline is not null)
        {
            StopBusWatch();
            _recordPipeline?.SendEvent(Gst.Event.NewEos());
            _recordPipeline?.SetState(Gst.State.Null);
            _recordPipeline?.Dispose();
            _recordPipeline = null;
            DisposeBus();
        }

        StopPreviewInternal();
        await Task.CompletedTask;
    }
}
