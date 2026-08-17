using System;
using System.Runtime.InteropServices;
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
    private byte[]? _scratch;
    private static readonly DrawingBrush s_checker = CreateChecker();

    /// <summary>
    /// When true, the pushed frame's alpha channel is composited in software over a
    /// checkerboard (so transparent mask areas are visible). Avalonia's
    /// <see cref="DrawingContext.DrawImage"/> renders <c>WriteableBitmap</c>s with
    /// their alpha ignored on this platform, so the blend is done here instead.
    /// </summary>
    public bool CompositeAlpha { get; set; }

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

        // WriteableBitmap must only be touched on the UI thread.
        Dispatcher.UIThread.Post(() => UpdateFrame(frame), DispatcherPriority.Render);
    }

    private void UpdateFrame(CameraFrame frame)
    {
        lock (_sync)
        {
            if (_bitmap is null
                || _bitmap.PixelSize.Width != frame.Width
                || _bitmap.PixelSize.Height != frame.Height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(frame.Width, frame.Height),
                    new Vector(96, 96),
                    PixelFormats.Bgra8888);
            }

            using var framebuffer = _bitmap.Lock();
            var source = frame.Data;

            if (CompositeAlpha)
            {
                // Software-composite the video over the checkerboard into an opaque
                // buffer, so alpha-masked (transparent) regions are visible.
                var w = frame.Width;
                var h = frame.Height;
                if (_scratch is null || _scratch.Length != source.Length)
                    _scratch = new byte[source.Length];
                var dst = _scratch;
                for (var y = 0; y < h; y++)
                {
                    var row = y * w;
                    for (var x = 0; x < w; x++)
                    {
                        var o = (row + x) * 4;
                        var a = source[o + 3];
                        if (a == 255)
                        {
                            dst[o] = source[o];
                            dst[o + 1] = source[o + 1];
                            dst[o + 2] = source[o + 2];
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
                                dst[o] = (byte)((source[o] * a + cb * ia) / 255);
                                dst[o + 1] = (byte)((source[o + 1] * a + cg * ia) / 255);
                                dst[o + 2] = (byte)((source[o + 2] * a + cr * ia) / 255);
                            }
                        }
                        dst[o + 3] = 255;
                    }
                }
                Marshal.Copy(dst, 0, framebuffer.Address, dst.Length);
            }
            else
            {
                var rowBytes = Math.Min(framebuffer.RowBytes, frame.Stride);
                for (var y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(
                        source,
                        y * frame.Stride,
                        framebuffer.Address + (y * framebuffer.RowBytes),
                        rowBytes);
                }
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
