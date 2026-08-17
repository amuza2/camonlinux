using System;

namespace camonlinux.Masking.Geometry;

/// <summary>
/// Pure colour math (no UI, no allocation) shared by the adjustment and chroma-key
/// effects. All channels are 0..255 unless noted.
/// </summary>
public static class ColorMath
{
    public static byte Clamp255(double v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

    // ------------------------------------------------------------------ //
    // RGB <-> HSL  (r,g,b 0..1; h 0..360; s,l 0..1)
    // ------------------------------------------------------------------ //

    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9)
            return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        double h;
        if (Math.Abs(max - r) < 1e-9)
            h = (g - b) / d + (g < b ? 6.0 : 0.0);
        else if (Math.Abs(max - g) < 1e-9)
            h = (b - r) / d + 2.0;
        else
            h = (r - g) / d + 4.0;
        h *= 60.0;
        return (h, s, l);
    }

    public static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s < 1e-9)
            return (l, l, l);

        h = ((h % 360) + 360) % 360 / 360.0;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return (Hue(p, q, h + 1.0 / 3.0), Hue(p, q, h), Hue(p, q, h - 1.0 / 3.0));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    // ------------------------------------------------------------------ //
    // YCbCr (BT.601) + chroma distance for keying
    // ------------------------------------------------------------------ //

    public static (double Y, double Cb, double Cr) RgbToYcbcr(double r, double g, double b)
    {
        return (
            0.299 * r + 0.587 * g + 0.114 * b,
            -0.168736 * r - 0.331264 * g + 0.5 * b,
            0.5 * r - 0.418688 * g - 0.081312 * b);
    }

    public static double ChromaDistance(double cb, double cr, double cbKey, double crKey)
        => Math.Sqrt((cb - cbKey) * (cb - cbKey) + (cr - crKey) * (cr - crKey));
}
