namespace camonlinux.Masking;

using System;

/// <summary>
/// A mutable wrapper around a BGRA32 pixel buffer. It borrows the underlying
/// <see cref="byte[]"/> (no copy) so the capture service's frame buffer is used
/// in place. The instance is reused across frames to avoid per-frame allocation.
/// </summary>
public sealed class MaskFrame
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }

    public MaskFrame()
    {
    }

    public MaskFrame(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    /// <summary>Re-points this wrapper at a (new) buffer — no allocation, no copy.</summary>
    public void Set(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    /// <summary>Bytes per row for BGRA32 (always width * 4).</summary>
    public int Stride => Width * 4;

    public int PixelCount => Width * Height;

    /// <summary>Reads the BGRA byte at pixel (x, y).</summary>
    public void GetPixel(int x, int y, out byte b, out byte g, out byte r, out byte a)
    {
        var i = (y * Width + x) * 4;
        b = Data[i];
        g = Data[i + 1];
        r = Data[i + 2];
        a = Data[i + 3];
    }

    /// <summary>Writes the BGRA byte at pixel (x, y).</summary>
    public void SetPixel(int x, int y, byte b, byte g, byte r, byte a)
    {
        var i = (y * Width + x) * 4;
        Data[i] = b;
        Data[i + 1] = g;
        Data[i + 2] = r;
        Data[i + 3] = a;
    }
}
