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
/// The live preview and recording are driven by a single pipeline:
/// <list type="bullet">
///   <item>
///     <description><b>Preview (idle)</b>:
///     <c>v4l2src ! videoconvert ! videoflip ! {effect} ! video/x-raw,format=BGRx ! appsink</c>
///     — frames are pulled with TryPullSample and published via <see cref="FrameReady"/>.</description>
///   </item>
///   <item>
///     <description><b>Recording</b>: the pipeline is rebuilt to add a tee + record branch
///     (<c>x264enc ! matroskamux ! filesink</c>) so the <b>live preview keeps running</b> while
///     recording. Stopping sends EOS down the record branch (finalizing the MKV) and rebuilds
///     back to the plain preview.</description>
///   </item>
/// </list>
///
/// NOTE: the record branch deliberately has NO buffer-dropping gate — a valve or
/// identity in drop mode stalls the tee and freezes the preview (see git history).
/// Requires GStreamer + plugins on the host; see the README.
/// </summary>
public sealed class GStreamerCaptureService : ICaptureService
{
    private static bool s_gstInitialized;

    // Capture modes offered in the UI. High-res modes must use MJPEG (many UVC
    // cams — e.g. the Logitech C930e — cannot stream raw 720p/1080p), so those
    // insert a jpegdec after v4l2src. CheckType/W/H are used to test whether the
    // device supports the mode via a fast caps query (no streaming).
    private static readonly (string Label, string Caps, string CheckType, int W, int H, bool Jpeg)[] s_modes =
    {
        ("1920×1080 @ 30", "image/jpeg,width=1920,height=1080,framerate=30/1", "image/jpeg", 1920, 1080, true),
        ("1280×720 @ 30", "image/jpeg,width=1280,height=720,framerate=30/1", "image/jpeg", 1280, 720, true),
        ("960×540 @ 30", "image/jpeg,width=960,height=540,framerate=30/1", "image/jpeg", 960, 540, true),
        ("640×480 @ 30", "video/x-raw,width=640,height=480,framerate=30/1", "video/x-raw", 640, 480, false),
        ("640×360 @ 30", "video/x-raw,width=640,height=360,framerate=30/1", "video/x-raw", 640, 360, false),
    };

    private Element? _previewPipeline;
    private AppSink? _appSink;
    private Element? _recordQueue;
    private Element? _recordFileSink;
    private Element? _audioQueue;
    private Element? _micVolume;
    private string? _audioSource;
    private bool _micMuted;
    private string _resolution = "";
    private string _recordQuality = "medium";
    private long _maxFileSizeMB;
    private readonly Dictionary<string, IReadOnlyList<string>> _modeCache = new();
    private readonly Stopwatch _recSizeStopwatch = new();
    private readonly SemaphoreSlim _recordingGate = new(1, 1);
    private string? _recordPath;
    private bool _splitting;    private int _sourceWidth;
    private int _sourceHeight;
    private bool _zoomPending;
    private bool _rebuildingPreview;    private CameraDevice? _currentDevice;
    private CameraFrame? _latestFrame;
    private Gst.Bus? _bus;
    private CancellationTokenSource? _busWatchCts;
    private CancellationTokenSource? _frameLoopCts;
    private readonly object _frameLock = new();
    private readonly List<CameraDevice> _devices = new();

    public event EventHandler<CameraFrame>? FrameReady;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsPreviewActive => _previewPipeline is not null;
    public bool IsRecording => _recordQueue is not null;
    public bool Mirrored { get; set; } = true;
    public string Effect { get; set; } = "";

    /// <summary>
    /// Mutes the microphone. Applies live while recording (via the volume
    /// element) and is baked into the pipeline when a recording starts.
    /// </summary>
    public bool MicMuted
    {
        get => _micMuted;
        set
        {
            if (_micMuted == value)
                return;
            _micMuted = value;
            if (_micVolume is not null)
            {
                try { _micVolume.SetProperty("mute", new GObject.Value(_micMuted)); }
                catch { /* recording may be tearing down */ }
            }
        }
    }

    /// <summary>Selected capture mode label ("" or "Default" = camera default).</summary>
    public string Resolution { get => _resolution; set => _resolution = value ?? ""; }

    /// <summary>Recording quality key: "low", "medium" or "high" (maps to x264 bitrate).</summary>
    public string RecordQuality { get => _recordQuality; set => _recordQuality = value ?? "medium"; }

    /// <summary>Split recordings at this size (MB); 0 = no limit.</summary>
    public long MaxFileSizeMB { get => _maxFileSizeMB; set => _maxFileSizeMB = value; }

    /// <summary>Image rotation as a videoflip direction: "auto", "90r", "180", "90l".</summary>
    public string Rotation { get; set; } = "auto";

    /// <summary>Digital zoom factor (1.0 = none).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Photo file format: "jpeg" or "png".</summary>
    public string PhotoFormat { get; set; } = "jpeg";

    /// <summary>Stamp photos and recordings with the date &amp; time.</summary>
    public bool ShowTimestamp { get; set; }
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

                // Record audio through the default input (PipeWire/Pulse/ALSA).
                _audioSource = IsElementAvailable("autoaudiosrc") ? "autoaudiosrc" : null;
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

    public bool IsElementAvailable(string elementName)
    {
        if (string.IsNullOrWhiteSpace(elementName))
            return true;

        try
        {
            return Gst.ElementFactory.Find(elementName) is not null;
        }
        catch
        {
            return false;
        }
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
    // Capture modes (resolution / framerate)
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns the labels of the capture modes the given device actually supports.
    /// Determined with a fast caps query (device opened at READY, no streaming) so
    /// it can run even while another pipeline is using the camera. Cached per device.
    /// </summary>
    public IReadOnlyList<string> GetSupportedModes(CameraDevice device)
    {
        if (_modeCache.TryGetValue(device.Id, out var cached))
            return cached;

        var list = new List<string>();
        foreach (var (_, _, checkType, w, h, _) in s_modes)
        {
            if (ModeNegotiates(device, checkType, w, h))
                list.Add(LabelFor(checkType, w, h));
        }
        _modeCache[device.Id] = list;
        return list;
    }

    private static string LabelFor(string type, int w, int h)
    {
        foreach (var (label, caps, checkType, cw, ch, _) in s_modes)
            if (checkType == type && cw == w && ch == h)
                return label;
        return $"{w}×{h} @ 30";
    }

    /// <summary>True if the device reports a caps structure with the given type/size.</summary>
    private static bool ModeNegotiates(CameraDevice device, string mediaType, int width, int height)
    {
        try
        {
            using var src = Gst.ElementFactory.Make("v4l2src", null);
            if (src is null)
                return false;
            src.SetProperty("device", new GObject.Value(device.Path));
            src.SetState(Gst.State.Ready);
            using var caps = src.GetStaticPad("src")?.QueryCaps(null);
            if (caps is null)
                return false;
            for (uint i = 0; i < caps.GetSize(); i++)
            {
                using var s = caps.GetStructure(i);
                if (s is null || s.GetName() != mediaType)
                    continue;
                s.GetInt("width", out var w);
                s.GetInt("height", out var h);
                if (w == width && h == height)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The v4l2src prefix for the current capture mode: optionally adds the mode's
    /// caps (and a jpegdec for MJPEG high-res modes). Empty resolution = default.
    /// </summary>
    private string SourcePrefix()
    {
        var mode = CurrentMode();
        if (mode is null)
            return $"v4l2src device={_currentDevice!.Path}";
        return mode.Value.Jpeg
            ? $"v4l2src device={_currentDevice!.Path} ! {mode.Value.Caps} ! jpegdec"
            : $"v4l2src device={_currentDevice!.Path} ! {mode.Value.Caps}";
    }

    private (string Caps, bool Jpeg, int W, int H)? CurrentMode()
    {
        if (string.IsNullOrEmpty(_resolution) || _resolution == "Default")
            return null;
        foreach (var (label, caps, _, w, h, jpeg) in s_modes)
            if (label == _resolution)
                return (caps, jpeg, w, h);
        return null;
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
        _recordQueue = null;
        _recordFileSink = null;
        _audioQueue = null;
        _micVolume = null;
        _recordPath = null;

        DisposeBus();

        lock (_frameLock)
            _latestFrame = null;
    }

    private void BuildPreviewPipeline()
    {
        if (_currentDevice is null)
            return;

        if (TryBuildPipeline(PreviewDescription(Effect)))
            return;

        // The effect failed to parse (e.g. missing plugin) — fall back to no effect.
        if (!string.IsNullOrWhiteSpace(Effect))
            ErrorOccurred?.Invoke(this, $"The effect '{Effect}' is not available on this system; continuing without it.");
        TryBuildPipeline(PreviewDescription(""));
    }

    /// <summary>
    /// A digital-zoom stage (crop the center, scale back to full size) inserted after
    /// the source. Requires the source resolution: the selected mode's size, or the
    /// size of the last received frame. Returns "" when zoom is 1x or the size isn't
    /// known yet (the preview is rebuilt once a frame establishes it).
    /// </summary>
    private string ZoomStage()
    {
        if (Zoom <= 1.0)
            return "";

        int w, h;
        var mode = CurrentMode();
        if (mode is not null)
        {
            w = mode.Value.W;
            h = mode.Value.H;
        }
        else
        {
            w = _sourceWidth;
            h = _sourceHeight;
        }

        if (w <= 0 || h <= 0)
        {
            _zoomPending = true; // retry once a frame gives us the source size
            return "";
        }

        _zoomPending = false;
        var cropX = ((int)((w - w / Zoom) / 2)) & ~1;
        var cropY = ((int)((h - h / Zoom) / 2)) & ~1;
        if (cropX <= 0 && cropY <= 0)
            return "";

        return $"videocrop left={cropX} right={cropX} top={cropY} bottom={cropY} ! " +
               $"videoscale ! video/x-raw,width={w},height={h} ! videoconvert ! ";
    }

    private string PreviewDescription(string effect)
    {
        var rotation = Rotation == "auto" ? "" : $"videoflip video-direction={Rotation} ! ";
        var mirror = Mirrored ? "videoflip video-direction=horiz ! " : "";
        var effectChain = string.IsNullOrWhiteSpace(effect) ? "" : $"{effect} ! ";
        // A trailing videoconvert lets effects (e.g. frei0r filters) negotiate their
        // preferred format and still bridge to the BGRx caps the appsink needs.
        return
            $"{SourcePrefix()} ! videoconvert ! {ZoomStage()}{rotation}{mirror}{effectChain}" +
            "videoconvert ! video/x-raw,format=BGRx ! appsink name=sink max-buffers=1 drop=true";
    }

    private string RecordingDescription(string effect, string path, bool includeAudio)
    {
        var rotation = Rotation == "auto" ? "" : $"videoflip video-direction={Rotation} ! ";
        var mirror = Mirrored ? "videoflip video-direction=horiz ! " : "";
        var effectChain = string.IsNullOrWhiteSpace(effect) ? "" : $"{effect} ! ";
        var overlay = ShowTimestamp
            ? $"textoverlay text=\"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\" valignment=bottom halignment=right font-desc=\"Sans 24\" ! "
            : "";
        // Audio branch: default mic -> optional live mute -> AAC, muxed into the MKV.
        var audio = includeAudio && _audioSource is not null
            ? $" {_audioSource} ! volume name=micvolume mute={(_micMuted ? "true" : "false")} ! audioconvert " +
              "! audioresample ! audio/x-raw,rate=48000,channels=2 ! fdkaacenc bitrate=128000 " +
              "! queue name=audq ! mux. "
            : "";
        // x264enc's bitrate is in kbit/s.
        var bitrate = _recordQuality switch { "low" => 500, "high" => 5_000, _ => 1_500 };
        return
            $"{SourcePrefix()} ! videoconvert ! {ZoomStage()}{rotation}{mirror}{effectChain}{overlay}tee name=t " +
            "t. ! queue ! videoconvert ! video/x-raw,format=BGRx ! appsink name=sink max-buffers=1 drop=true " +
            $"t. ! queue name=recq ! videoconvert ! x264enc speed-preset=veryfast tune=zerolatency bitrate={bitrate} " +
            $"! matroskamux name=mux ! filesink name=filesink location=\"{path}\"{audio}";
    }

    private bool TryBuildPipeline(string description)
    {
        try
        {
            _previewPipeline = Gst.Functions.ParseLaunch(description);
            if (_previewPipeline is null)
                return false;

            var bin = (Gst.Bin)_previewPipeline;
            _appSink = bin.GetByName("sink") as AppSink
                ?? throw new InvalidOperationException("Failed to create the preview appsink.");
            _recordQueue = bin.GetByName("recq");
            _recordFileSink = bin.GetByName("filesink");
            _audioQueue = bin.GetByName("audq");
            _micVolume = bin.GetByName("micvolume");

            _bus = _previewPipeline.GetBus();
            StartBusWatch();
            StartFrameLoop();
            return true;
        }
        catch
        {
            StopPreviewInternal();
            return false;
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

        if (ShowTimestamp)
        {
            using var canvas = new SKCanvas(bitmap);
            var fontSize = Math.Max(16f, frame.Height / 24f);
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = fontSize,
                IsAntialias = true,
                FakeBoldText = true,
                Typeface = SKTypeface.FromFamilyName("monospace")
            };
            var text = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var x = frame.Width - 8 - textPaint.MeasureText(text);
            var y = frame.Height - 8;
            using var outline = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = fontSize,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f,
                Typeface = textPaint.Typeface
            };
            canvas.DrawText(text, x, y, outline);
            canvas.DrawText(text, x, y, textPaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        var format = PhotoFormat == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var encoded = image.Encode(format, 92);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);

        return Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<string?> RenderEffectThumbnailAsync(string effect, string sampleImagePath, string outputPath, int width, int height)
    {
        try
        {
            // One-shot pipeline that decodes the source and outputs a scaled BGRx frame.
            // We pull a SINGLE frame via appsink and stop immediately — otherwise
            // decodebin would decode the entire (possibly huge) video.
            var effectChain = string.IsNullOrWhiteSpace(effect) ? "" : $" ! {effect}";
            var description =
                $"filesrc location=\"{sampleImagePath}\" ! decodebin ! videoconvert{effectChain} " +
                $"! videoconvert ! videoscale ! video/x-raw,width={width},height={height} " +
                "! videoconvert ! video/x-raw,format=BGRx ! appsink name=sink max-buffers=1 drop=true";

            var pipeline = Gst.Functions.ParseLaunch(description);
            if (pipeline is null)
                return Task.FromResult<string?>(null);

            var bin = (Gst.Bin)pipeline;
            var appsink = bin.GetByName("sink") as AppSink
                ?? throw new InvalidOperationException("Failed to create the thumbnail appsink.");

            pipeline.SetState(Gst.State.Playing);

            // Wait (up to 8s) for the first decoded frame, then stop decoding.
            using var sample = appsink.TryPullSample((Gst.ClockTime)8_000_000_000UL);
            byte[]? data = null;
            var w = 0;
            var h = 0;
            if (sample is not null)
            {
                using var buffer = sample.GetBuffer();
                if (buffer is not null)
                {
                    var size = (int)buffer.GetSize();
                    data = new byte[size];
                    buffer.Extract(0, data);

                    using var caps = sample.GetCaps();
                    if (caps is not null)
                    {
                        var structure = caps.GetStructure(0);
                        if (structure is not null)
                        {
                            structure.GetInt("width", out w);
                            structure.GetInt("height", out h);
                        }
                    }
                }
            }

            pipeline.SetState(Gst.State.Null);
            pipeline.Dispose();

            if (data is null || w <= 0 || h <= 0)
                return Task.FromResult<string?>(null);

            // BGRx has an unused alpha byte; force it opaque, then encode a PNG.
            for (var i = 3; i < data.Length; i += 4)
                data[i] = 0xFF;

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var bitmap = new SKBitmap(info);
            Marshal.Copy(data, 0, bitmap.GetPixels(), data.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

            // The caller may pass a path in a cache directory that doesn't exist
            // yet (e.g. /tmp/camonlinux_gallery) — create it before writing.
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = File.Create(outputPath);
            encoded.SaveTo(stream);

            return Task.FromResult<string?>(outputPath);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    // ------------------------------------------------------------------ //
    // Recording
    // ------------------------------------------------------------------ //

    public async Task StartRecordingAsync(string path)
    {
        await _recordingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_recordQueue is not null)
                return; // already recording

            if (_currentDevice is null)
                throw new InvalidOperationException("No camera selected.");

            _recordPath = null;
            _recSizeStopwatch.Restart();
            _splitting = false;

            // Make sure the target folder exists (filesink fails otherwise).
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Rebuild the pipeline to add the recording branch — the preview stays live.
            StopPreviewInternal();

            // Set AFTER StopPreviewInternal (it clears _recordPath).
            _recordPath = path;

            var withAudio = _audioSource is not null;
            if (!TryBuildPipeline(RecordingDescription(Effect, path, withAudio)) && withAudio)
            {
                // Audio branch failed (no sound device reachable) — retry without sound.
                ErrorOccurred?.Invoke(this, "No audio input available; recording without sound.");
                TryBuildPipeline(RecordingDescription(Effect, path, false));
            }

            if (_previewPipeline is not null)
            {
                _previewPipeline.SetState(Gst.State.Playing);
            }
            else
            {
                // Recording branch failed (e.g. missing encoder) — resume plain preview.
                ErrorOccurred?.Invoke(this, "Could not start recording (missing encoder?); preview continues.");
                BuildPreviewPipeline();
                _previewPipeline?.SetState(Gst.State.Playing);
            }
        }
        finally
        {
            _recordingGate.Release();
        }
    }

    public async Task StopRecordingAsync()
    {
        await _recordingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var recordQueue = _recordQueue;
            if (recordQueue is null)
                return; // not recording

            StopBusWatch();

            // EOS down both record branches lets x264enc/fdkaacenc flush and
            // matroskamux finalize the container (the preview branch runs until teardown).
            recordQueue.GetStaticPad("sink")?.SendEvent(Gst.Event.NewEos());
            _audioQueue?.GetStaticPad("sink")?.SendEvent(Gst.Event.NewEos());

            // Wait for the muxer to finalize the file.
            if (_bus is not null)
            {
                using var _ = _bus.TimedPopFiltered(
                    (Gst.ClockTime)3_000_000_000UL,
                    Gst.MessageType.Eos | Gst.MessageType.Error);
            }

            // Tear down and resume the plain (idle) preview.
            StopPreviewInternal();
            if (_currentDevice is not null)
            {
                BuildPreviewPipeline();
                _previewPipeline?.SetState(Gst.State.Playing);
            }
        }
        finally
        {
            _recordingGate.Release();
        }
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
            MaybeSplitRecording();
        }
    }

    /// <summary>
    /// When a size cap is set, checks the current recording's file size about once
    /// a second and — if it exceeds the cap — finalizes it and continues into a
    /// numbered next file (video_…-1.mkv, video_…-2.mkv, …). The preview keeps running.
    /// </summary>
    private void MaybeSplitRecording()
    {
        if (_maxFileSizeMB <= 0 || _recordPath is null || !IsRecording || _splitting)
            return;
        if (_recSizeStopwatch.ElapsedMilliseconds < 1000)
            return;
        _recSizeStopwatch.Restart();

        try
        {
            var info = new FileInfo(_recordPath);
            if (!info.Exists || info.Length < _maxFileSizeMB * 1024 * 1024)
                return;
        }
        catch
        {
            return;
        }

        _splitting = true;
        var nextPath = NextRecordPath(_recordPath);
        _ = Task.Run(async () =>
        {
            try
            {
                await StopRecordingAsync();
                await StartRecordingAsync(nextPath);
                _recordPath = nextPath;
            }
            catch
            {
                // Best-effort split; the recording may simply stop here.
            }
            finally
            {
                _splitting = false;
            }
        });
    }

    private static string NextRecordPath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var index = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{name}-{index}{ext}");
            if (!File.Exists(candidate))
                return candidate;
            index++;
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

        // Track the source resolution so a deferred zoom can be applied once it's known.
        _sourceWidth = width;
        _sourceHeight = height;
        MaybeApplyPendingZoom();

        FrameReady?.Invoke(this, frame);
    }

    /// <summary>
    /// If zoom was requested while the source size was unknown (default resolution),
    /// rebuild the preview once a frame establishes the size, so the zoom engages.
    /// </summary>
    private void MaybeApplyPendingZoom()
    {
        if (!_zoomPending || Zoom <= 1.0 || _rebuildingPreview || IsRecording)
            return;
        if (_sourceWidth <= 0 || _sourceHeight <= 0)
            return;

        _zoomPending = false;
        _rebuildingPreview = true;
        _ = Task.Run(() =>
        {
            try
            {
                StopPreviewInternal();
                if (_currentDevice is not null)
                {
                    BuildPreviewPipeline();
                    _previewPipeline?.SetState(Gst.State.Playing);
                }
            }
            catch
            {
                // Best-effort; the next rebuild will retry.
            }
            finally
            {
                _rebuildingPreview = false;
            }
        });
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
        if (_recordQueue is not null)
        {
            StopBusWatch();
            _recordQueue.GetStaticPad("sink")?.SendEvent(Gst.Event.NewEos());
            _previewPipeline?.SetState(Gst.State.Null);
        }

        StopPreviewInternal();
        await Task.CompletedTask;
    }
}
