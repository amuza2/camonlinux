using System;
using System.Threading.Tasks;
using camonlinux.Masking.Geometry;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Chroma-key mask. Computes a per-pixel key matte from the chroma distance to the
/// key colour(s) in YCbCr space, with similarity (threshold), smoothness (soft
/// edge), opacity, contrast, brightness and gamma applied to the matte. Spill
/// reduction pulls keyed pixels toward neutral grey. <c>ShowMatte</c> previews the
/// matte as grayscale instead of the masked result.
/// </summary>
public sealed class ChromaKeyMaskEffect : IMaskEffect
{
    public string Name => "Chroma Key";
    public bool Enabled { get; set; } = false;
    public ChromaKeyMaskSettings Settings { get; }

    public ChromaKeyMaskEffect(ChromaKeyMaskSettings settings) => Settings = settings;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var w = frame.Width;
        var h = frame.Height;
        var s = Settings;
        var data = frame.Data;

        var (_, cbKey, crKey) = ColorMath.RgbToYcbcr(s.KeyR / 255.0, s.KeyG / 255.0, s.KeyB / 255.0);
        var (_, cbKey2, crKey2) = s.DoubleColor
            ? ColorMath.RgbToYcbcr(s.KeyR2 / 255.0, s.KeyG2 / 255.0, s.KeyB2 / 255.0)
            : (0.0, cbKey, crKey);

        var threshold = s.Similarity / 100.0 * 0.5;   // 0..0.5 chroma distance
        var soft = s.Smoothness / 100.0 * 0.2;
        var opacity = s.Opacity / 100.0;
        var contrast = 1.0 + s.Contrast / 100.0;
        var brightness = s.Brightness / 100.0;
        var gamma = Math.Max(0.01, s.Gamma / 100.0);
        var spill = s.SpillReduction / 100.0;
        var showMatte = s.ShowMatte;

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var i = rowBase + x * 4;
                var r = data[i + 2];
                var g = data[i + 1];
                var b = data[i];

                var (_, cb, cr) = ColorMath.RgbToYcbcr(r / 255.0, g / 255.0, b / 255.0);
                var d1 = ColorMath.ChromaDistance(cb, cr, cbKey, crKey);
                var d = d1;
                if (s.DoubleColor)
                    d = Math.Min(d, ColorMath.ChromaDistance(cb, cr, cbKey2, crKey2));

                // matte = 1 (masked/transparent) near the key, 0 elsewhere.
                var matte = 1.0 - SmoothStep(threshold - soft, threshold + soft, d);

                // contrast / brightness / gamma / opacity on the matte value.
                matte = (matte - 0.5) * contrast + 0.5 + brightness;
                if (matte < 0) matte = 0; else if (matte > 1) matte = 1;
                matte = Math.Pow(matte, 1.0 / gamma) * opacity;

                var pi = i;
                if (showMatte)
                {
                    // Preview the matte as black/white, fully opaque.
                    var v = (byte)(matte * 255.0 + 0.5);
                    data[pi] = v;
                    data[pi + 1] = v;
                    data[pi + 2] = v;
                    coverage[x + y * w] = 255;
                }
                else
                {
                    coverage[x + y * w] = (byte)(matte * 255.0 + 0.5);
                    if (spill > 0.0 && matte > 0.001)
                    {
                        // Pull keyed pixels toward neutral grey to kill spill.
                        var f = matte * spill;
                        data[pi] = ColorMath.Clamp255(b + (128.0 - b) * f);
                        data[pi + 1] = ColorMath.Clamp255(g + (128.0 - g) * f);
                        data[pi + 2] = ColorMath.Clamp255(r + (128.0 - r) * f);
                    }
                }
            }
        });
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        if (e1 <= e0)
            return x < e1 ? 1.0 : 0.0;
        var t = (x - e0) / (e1 - e0);
        if (t < 0) t = 0; else if (t > 1) t = 1;
        return t * t * (3 - 2 * t);
    }
}
