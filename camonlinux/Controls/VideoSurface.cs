using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using camonlinux.Capture;

namespace camonlinux.Controls;

/// <summary>
/// Renders <see cref="CameraFrame"/>s coming from the capture service into a
/// <see cref="WriteableBitmap"/>, letterboxed to preserve aspect ratio. A checkerboard
/// is drawn behind the video so alpha-masked (transparent) areas are visible.
/// </summary>
public sealed class VideoSurface : Control
{
    private readonly object _sync = new();
    private WriteableBitmap? _bitmap;
    private static readonly DrawingBrush s_checker = CreateChecker();

    // Bounds the UI-thread frame queue so posted 8 MB frames can't pile up
    // (prevents unbounded RSS growth when the composite/preview is slow).
    private const int MaxPending = 2;
    private const int CompositePoolSize = 3;
    private int _pending;
    private readonly byte[][] _compositePool = new byte[CompositePoolSize][];
    private int _poolIndex;

    /// <summary>
    /// When true, the pushed frame's alpha channel is composited in software over a
    /// checkerboard (so transparent mask areas are visible). Avalonia's
    /// <see cref="DrawingContext.DrawImage"/> renders <c>WriteableBitmap</c>s with
    /// their alpha ignored on this platform, so the blend is done here instead.
    /// </summary>
    public bool CompositeAlpha { get; set; }

    /// <summary>True when the UI thread is behind and frames are being dropped.</summary>
    public bool IsUiBackedUp => _pending >= MaxPending;

    private static DrawingBrush CreateChecker()
    {
        const double size = 16;
        var dark = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        var light = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(0, 0, size * 2, size * 2)) });
        group.Children.Add(new GeometryDrawing { Brush = light, Geometry = new RectangleGeometry(new Rect(0, 0, size, size)) });
        group.Children.Add(new GeometryDrawing { Brush = light, Geometry = new RectangleGeometry(new Rect(size, size, size, size)) });
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, size * 2, size * 2, RelativeUnit.Absolute)
        };
    }

    /// <summary>Checkerboard cell colour (dark/light) for a pixel — matches the background brush.</summary>
    private static (byte B, byte G, byte R) CheckerColor(int x, int y)
    {
        var tx = x & 31;
        var ty = y & 31;
        return ((tx < 16 && ty < 16) || (tx >= 16 && ty >= 16))
            ? ((byte)0x3a, (byte)0x3a, (byte)0x3a)
            : ((byte)0x24, (byte)0x24, (byte)0x24);
    }

    /// <summary>Called from the GStreamer streaming thread; marshals to the UI thread.</summary>
    public void PushFrame(CameraFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Data.Length == 0)
            return;

        // Bound the UI queue: if the UI thread can't keep up, drop this frame instead
        // of letting posted 8 MB buffers pile up (unbounded RSS growth).
        if (Interlocked.Increment(ref _pending) > MaxPending)
        {
            Interlocked.Decrement(ref _pending);
            return;
        }

        var source = frame.Data;
        var w = frame.Width;
        var h = frame.Height;

        if (CompositeAlpha)
        {
            // Composite on the calling (streaming) thread so the UI thread only does
            // a memcpy. Round-robin a small pool so the UI's async copy isn't
            // overwritten by a later frame.
            var idx = _poolIndex = (_poolIndex + 1) % CompositePoolSize;
            if (_compositePool[idx] is null || _compositePool[idx].Length != source.Length)
                _compositePool[idx] = new byte[source.Length];
            var dst = _compositePool[idx];
            Composite(source, dst, w, h);
            source = dst;
        }

        var data = source;
        Dispatcher.UIThread.Post(() =>
        {
            try { UpdateFrame(data, w, h); }
            finally { Interlocked.Decrement(ref _pending); }
        }, DispatcherPriority.Render);
    }

    /// <summary>Blends BGRA <paramref name="src"/> over a checkerboard into opaque <paramref name="dst"/>.</summary>
    private static void Composite(byte[] src, byte[] dst, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var o = (row + x) * 4;
                var a = src[o + 3];
                if (a == 255)
                {
                    dst[o] = src[o];
                    dst[o + 1] = src[o + 1];
                    dst[o + 2] = src[o + 2];
                }
                else
                {
                    var (cb, cg, cr) = CheckerColor(x, y);
                    if (a == 0)
                    {
                        dst[o] = cb;
                        dst[o + 1] = cg;
                        dst[o + 2] = cr;
                    }
                    else
                    {
                        var ia = 255 - a;
                        dst[o] = (byte)((src[o] * a + cb * ia) / 255);
                        dst[o + 1] = (byte)((src[o + 1] * a + cg * ia) / 255);
                        dst[o + 2] = (byte)((src[o + 2] * a + cr * ia) / 255);
                    }
                }
                dst[o + 3] = 255;
            }
        }
    }

    private void UpdateFrame(byte[] data, int width, int height)
    {
        lock (_sync)
        {
            if (_bitmap is null
                || _bitmap.PixelSize.Width != width
                || _bitmap.PixelSize.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888);
            }

            using var framebuffer = _bitmap.Lock();
            var rowBytes = Math.Min(framebuffer.RowBytes, width * 4);
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    data,
                    y * width * 4,
                    framebuffer.Address + (y * framebuffer.RowBytes),
                    rowBytes);
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        WriteableBitmap? bitmap;
        lock (_sync)
            bitmap = _bitmap;

        if (bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // Checkerboard behind the video so alpha-masked areas are visible.
        context.DrawRectangle(s_checker, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var drawWidth = source.Width * scale;
        var drawHeight = source.Height * scale;
        var destination = new Rect(
            (Bounds.Width - drawWidth) / 2,
            (Bounds.Height - drawHeight) / 2,
            drawWidth,
            drawHeight);

        context.DrawImage(bitmap, source, destination);
    }
}
