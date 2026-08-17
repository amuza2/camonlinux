using System;
using System.Threading.Tasks;
using GObject;
using Gst;
using GstApp;
using GstBase;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Source mask — uses a second webcam as the mask. Owns a small GStreamer pipeline
/// (<c>v4l2src → videoconvert → BGRx → appsink</c>) that runs while the effect is
/// enabled; each frame it pulls the latest sample (non-blocking) and maps it into
/// coverage using the channel / filter / threshold / compression / scale / position
/// settings. The pipeline is started lazily on the streaming thread and stopped when
/// the effect is disabled or the device changes.
/// </summary>
public sealed class SourceMaskEffect : IMaskEffect
{
    public string Name => "Source";
    public bool Enabled { get; set; } = false;
    public SourceMaskSettings Settings { get; }

    public SourceMaskEffect(SourceMaskSettings settings) => Settings = settings;

    private readonly object _lock = new();
    private Element? _pipeline;
    private AppSink? _appSink;
    private string _currentDevice = "";
    private byte[]? _maskBuf;   // latest BGRx sample from the 2nd camera
    private int _mw, _mh;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var s = Settings;
        var w = frame.Width;
        var h = frame.Height;
        var count = w * h;

        if (string.IsNullOrWhiteSpace(s.Device))
        {
            Array.Clear(coverage, 0, count);
            return;
        }

        EnsureRunning(s.Device);

        byte[]? maskBuf;
        int mw, mh;
        lock (_lock)
        {
            maskBuf = _maskBuf;
            mw = _mw;
            mh = _mh;
            PullLatest();
        }
        if (maskBuf is null || mw <= 0 || mh <= 0)
        {
            Array.Clear(coverage, 0, count);
            return;
        }

        // Placement: target draw rect (top-left, w/h) on the frame.
        double drawW, drawH;
        switch (s.ScaleBy)
        {
            case SourceScaleBy.Width:
                drawW = s.Width; drawH = s.Width * mh / mw; break;
            case SourceScaleBy.Height:
                drawH = s.Height; drawW = s.Height * mw / mh; break;
            case SourceScaleBy.Stretch:
                drawW = w; drawH = h; break;
            case SourceScaleBy.Manual:
            case SourceScaleBy.Separate:
                drawW = s.Width; drawH = s.Height; break;
            default: // Percent
                drawW = w * s.Scale / 100.0; drawH = h * s.Scale / 100.0; break;
        }

        var rot = s.Rotation % 360;
        if (rot == 90 || rot == 270)
            (drawW, drawH) = (drawH, drawW);

        // Anchor alignment (3x3) + offset + position.
        var (ax, ay) = s.Alignment switch
        {
            SourceAlignment.TL => (0.0, 0.0), SourceAlignment.TC => (0.5, 0.0),
            SourceAlignment.TR => (1.0, 0.0), SourceAlignment.CL => (0.0, 0.5),
            SourceAlignment.CC => (0.5, 0.5), SourceAlignment.CR => (1.0, 0.5),
            SourceAlignment.BL => (0.0, 1.0), SourceAlignment.BC => (0.5, 1.0),
            _ => (1.0, 1.0) // BR
        };
        var drawLeft = w * ax - drawW * ax + s.OffsetX + s.PositionX;
        var drawTop = h * ay - drawH * ay + s.OffsetY + s.PositionY;

        var channel = s.Channel;
        var filter = s.Filter;
        var multiplier = s.Multiplier;
        var useThreshold = s.UseThreshold;
        var threshold = s.Threshold;
        var compression = s.Compression;
        var rangeMin = s.RangeMin;
        var rangeMax = s.RangeMax;
        var invert = s.Invert;
        var boundary = s.Boundary;

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w;
            for (var x = 0; x < w; x++)
            {
                if (!MapSourceCoords(x, y, drawLeft, drawTop, drawW, drawH, rot, mw, mh, boundary,
                        out var si, out var sx, out var sy))
                {
                    coverage[rowBase + x] = 0;
                    continue;
                }

                var b = maskBuf[si];
                var g = maskBuf[si + 1];
                var r = maskBuf[si + 2];
                var a = maskBuf[si + 3];

                double v = channel switch
                {
                    SourceChannel.Red => r,
                    SourceChannel.Green => g,
                    SourceChannel.Blue => b,
                    _ => a
                };
                if (filter == SourceFilter.Alpha) v = a;
                else if (filter == SourceFilter.Grayscale) v = (r + g + b) / 3.0;
                else if (filter == SourceFilter.Luminosity) v = 0.299 * r + 0.587 * g + 0.114 * b;

                v *= multiplier;
                if (useThreshold)
                    v = v >= threshold ? 255 : 0;
                if (compression == SourceCompression.Threshold)
                    v = v >= threshold ? 255 : 0;
                else if (compression == SourceCompression.Range)
                    v = (v >= rangeMin && v <= rangeMax) ? 255 : 0;

                var cov = v / 255.0;
                if (invert)
                    cov = 1.0 - cov;
                coverage[rowBase + x] = (byte)(cov * 255.0 + 0.5);
            }
        });
    }

    /// <summary>Maps an output pixel to a source BGRx index; returns false when outside.</summary>
    private static bool MapSourceCoords(
        int x, int y, double drawLeft, double drawTop, double drawW, double drawH, int rot,
        int mw, int mh, SourceBoundary boundary,
        out int si, out int sx, out int sy)
    {
        si = 0; sx = 0; sy = 0;
        if (drawW <= 0 || drawH <= 0)
            return false;

        var nx = (x - drawLeft) / drawW;   // 0..1 over the draw rect
        var ny = (y - drawTop) / drawH;

        // Rotate normalized coords back to the source orientation.
        double u, v;
        switch (rot)
        {
            case 90: u = ny; v = 1.0 - nx; break;
            case 180: u = 1.0 - nx; v = 1.0 - ny; break;
            case 270: u = 1.0 - ny; v = nx; break;
            default: u = nx; v = ny; break;
        }

        var fxs = u * mw;
        var fys = v * mh;
        sx = (int)fxs;
        sy = (int)fys;

        if (sx >= 0 && sx < mw && sy >= 0 && sy < mh)
        {
            si = (sy * mw + sx) * 4;
            return true;
        }

        switch (boundary)
        {
            case SourceBoundary.Extend:
                sx = Math.Clamp(sx, 0, mw - 1);
                sy = Math.Clamp(sy, 0, mh - 1);
                si = (sy * mw + sx) * 4;
                return true;
            case SourceBoundary.Tile:
                sx = ((sx % mw) + mw) % mw;
                sy = ((sy % mh) + mh) % mh;
                si = (sy * mw + sx) * 4;
                return true;
            case SourceBoundary.Mirror:
                sx = Mirror(sx, mw);
                sy = Mirror(sy, mh);
                si = (sy * mw + sx) * 4;
                return true;
            default:
                return false; // None
        }
    }

    private static int Mirror(int v, int size)
    {
        var p = ((v % size) + size) % size;
        return (p / size) % 2 == 0 ? p : size - 1 - p;
    }

    // ------------------------------------------------------------------ //
    // Secondary GStreamer capture
    // ------------------------------------------------------------------ //

    private void EnsureRunning(string device)
    {
        lock (_lock)
        {
            if (_pipeline is not null && _currentDevice == device)
                return;
            StopLocked();
            _currentDevice = device;
            try
            {
                var description =
                    $"v4l2src device={device} ! videoconvert ! videoscale " +
                    $"! video/x-raw,format=BGRx,width=320,height=240,framerate=15/1 " +
                    "! appsink name=sink max-buffers=1 drop=true";
                _pipeline = Gst.Functions.ParseLaunch(description);
                var bin = (Gst.Bin)_pipeline!;
                _appSink = bin.GetByName("sink") as AppSink;
                if (_appSink is null)
                {
                    StopLocked();
                    return;
                }
                _pipeline.SetState(Gst.State.Playing);
            }
            catch
            {
                StopLocked();
            }
        }
    }

    private void PullLatest()
    {
        var sink = _appSink;
        if (sink is null)
            return;
        try
        {
            // Non-blocking: grab the newest sample if one is buffered.
            using var sample = sink.TryPullSample((Gst.ClockTime)0UL);
            if (sample is null)
                return;
            using var buffer = sample.GetBuffer();
            if (buffer is null)
                return;
            var size = (int)buffer.GetSize();
            if (size <= 0)
                return;
            if (_maskBuf is null || _maskBuf.Length != size)
                _maskBuf = new byte[size];
            buffer.Extract(0, _maskBuf);

            using var caps = sample.GetCaps();
            if (caps is not null)
            {
                var structure = caps.GetStructure(0);
                if (structure is not null)
                {
                    structure.GetInt("width", out _mw);
                    structure.GetInt("height", out _mh);
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Stops the secondary camera (e.g. when the effect is disabled).</summary>
    public void Stop()
    {
        lock (_lock)
            StopLocked();
    }

    private void StopLocked()
    {
        if (_pipeline is not null)
        {
            try { _pipeline.SetState(Gst.State.Null); } catch { }
            try { _pipeline.Dispose(); } catch { }
            _pipeline = null;
            _appSink = null;
        }
        _maskBuf = null;
        _mw = 0;
        _mh = 0;
    }
}
