using System;
using System.Numerics;

namespace camonlinux.Imaging;

/// <summary>
/// Hot-loop helpers for the 32-bit BGRA pixel buffers used throughout the app
/// (camera frames, effect thumbnails, mask buffers).
///
/// The alpha byte of a BGRA pixel lives at index 3, i.e. every 4th byte, which makes
/// a naive <c>for (i = 3; i &lt; n; i += 4)</c> loop one of the most-executed loops in the
/// program (it runs over every byte of every frame at capture rate). These helpers
/// instead process <see cref="Vector{T}"/>-sized blocks and use
/// <see cref="Vector.ConditionalSelect{T}"/> with an alpha-only lane mask so the RGB
/// bytes are copied through untouched.
///
/// This used to be duplicated in three places (the mask pipeline, the capture service
/// and the thumbnail renderer); it now lives here so there is a single implementation.
/// </summary>
public static class PixelBuffer
{
    /// <summary>Bytes per BGRA32 pixel.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>A vector whose lanes are 0xFF exactly at the alpha byte positions.</summary>
    private static readonly Vector<byte> s_alphaLanes = BuildAlphaLanes();

    private static Vector<byte> BuildAlphaLanes()
    {
        Span<byte> lanes = stackalloc byte[Vector<byte>.Count];
        for (var i = 3; i < lanes.Length; i += BytesPerPixel)
            lanes[i] = 0xFF;
        return new Vector<byte>(lanes);
    }

    /// <summary>
    /// Number of whole pixels covered by <paramref name="byteLength"/> bytes, clamped to
    /// the buffer, so callers can never index past the end of the array.
    /// </summary>
    private static int SafePixelCount(byte[] data, int byteLength)
    {
        ArgumentNullException.ThrowIfNull(data);
        var length = Math.Min(byteLength, data.Length);
        return length <= 0 ? 0 : length / BytesPerPixel;
    }

    /// <summary>Sets the alpha byte of every BGRA32 pixel to <paramref name="alpha"/>.</summary>
    public static void SetAlpha(byte[] data, int byteLength, byte alpha)
    {
        var pixels = SafePixelCount(data, byteLength);
        if (pixels == 0)
            return;

        var fill = new Vector<byte>(alpha);
        var end = pixels * BytesPerPixel;
        var i = 0;
        for (; i + Vector<byte>.Count <= end; i += Vector<byte>.Count)
        {
            var v = Vector.LoadUnsafe(ref data[i]);
            Vector.ConditionalSelect(s_alphaLanes, fill, v).CopyTo(data.AsSpan(i));
        }

        for (var p = i / BytesPerPixel; p < pixels; p++)
            data[p * BytesPerPixel + 3] = alpha;
    }

    /// <summary>Sets every BGRA32 pixel opaque (alpha = 255) — the common case.</summary>
    public static void SetOpaque(byte[] data, int byteLength = -1)
        => SetAlpha(data, byteLength < 0 ? data.Length : byteLength, 0xFF);

    /// <summary>
    /// Replaces the alpha byte of every BGRA32 pixel with <c>255 - alpha</c>.
    /// </summary>
    public static void InvertAlpha(byte[] data, int byteLength)
    {
        var pixels = SafePixelCount(data, byteLength);
        if (pixels == 0)
            return;

        var max = new Vector<byte>(255);
        var end = pixels * BytesPerPixel;
        var i = 0;
        for (; i + Vector<byte>.Count <= end; i += Vector<byte>.Count)
        {
            var v = Vector.LoadUnsafe(ref data[i]);
            // 255 - alpha on the alpha lanes only; RGB is passed through untouched.
            var inverted = max - (v & s_alphaLanes);
            Vector.ConditionalSelect(s_alphaLanes, inverted, v).CopyTo(data.AsSpan(i));
        }

        for (var p = i / BytesPerPixel; p < pixels; p++)
            data[p * BytesPerPixel + 3] = (byte)(255 - data[p * BytesPerPixel + 3]);
    }

    /// <summary>
    /// Flattens a BGRA32 buffer whose alpha may be masked (0 = cut away) over an opaque
    /// background colour, writing the fully opaque result to <paramref name="destination"/>
    /// (which may be the same length as, or longer than, the source).
    ///
    /// Fully opaque pixels are copied verbatim, so when no mask is active this is a
    /// plain copy. Shared by the virtual-webcam feed and photo capture so both show the
    /// same composite (the old code had one implementation per call site).
    /// </summary>
    public static void FlattenOverBackground(
        byte[] source, byte[] destination, int byteLength, byte r, byte g, byte b)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var pixels = Math.Min(
            SafePixelCount(source, byteLength),
            destination.Length / BytesPerPixel);

        for (var p = 0; p < pixels; p++)
        {
            var o = p * BytesPerPixel;
            var a = source[o + 3];
            if (a == 255)
            {
                destination[o] = source[o];
                destination[o + 1] = source[o + 1];
                destination[o + 2] = source[o + 2];
            }
            else if (a == 0)
            {
                destination[o] = b;
                destination[o + 1] = g;
                destination[o + 2] = r;
            }
            else
            {
                var inverse = 255 - a;
                destination[o] = (byte)((source[o] * a + b * inverse) / 255);
                destination[o + 1] = (byte)((source[o + 1] * a + g * inverse) / 255);
                destination[o + 2] = (byte)((source[o + 2] * a + r * inverse) / 255);
            }
            destination[o + 3] = 255;
        }
    }
}
