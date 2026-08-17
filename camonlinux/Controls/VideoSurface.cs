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
/// <see cref="WriteableBitmap"/>, letterboxed to preserve aspect ratio.
/// </summary>
public sealed class VideoSurface : Control
{
    private readonly object _sync = new();
    private WriteableBitmap? _bitmap;

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

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        WriteableBitmap? bitmap;
        lock (_sync)
            bitmap = _bitmap;

        if (bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

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
