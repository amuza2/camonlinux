using System;
using System.Threading.Tasks;
using camonlinux.Masking.Geometry;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Colour-adjustment mask (Adjustment mode). Reads the frame's alpha as the mask
/// (0..255) and, per pixel, applies brightness/contrast/saturation/hue-shift with
/// each parameter interpolated between its min and max by the mask amount. Runs as
/// an <see cref="IColorAdjustmentEffect"/> AFTER the mask chain + invert; the
/// pipeline then restores alpha to 255.
/// </summary>
public sealed class ColorAdjustmentMaskEffect : IMaskEffect, IColorAdjustmentEffect
{
    public string Name => "Color Adjustment";
    public bool Enabled { get; set; } = false;
    public ColorAdjustmentMaskSettings Settings { get; }

    public ColorAdjustmentMaskEffect(ColorAdjustmentMaskSettings settings) => Settings = settings;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var w = frame.Width;
        var h = frame.Height;
        var s = Settings;
        var data = frame.Data;

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var i = rowBase + x * 4;
                var mask = data[i + 3] / 255.0; // 0..1
                if (mask <= 0.001)
                    continue;

                var rn = data[i + 2] / 255.0;
                var gn = data[i + 1] / 255.0;
                var bn = data[i] / 255.0;

                var bright = Lerp(s.BrightnessMin, s.BrightnessMax, mask) / 100.0;
                rn += bright;
                gn += bright;
                bn += bright;

                var cf = 1.0 + Lerp(s.ContrastMin, s.ContrastMax, mask) / 100.0;
                rn = (rn - 0.5) * cf + 0.5;
                gn = (gn - 0.5) * cf + 0.5;
                bn = (bn - 0.5) * cf + 0.5;

                var lum = 0.2126 * rn + 0.7152 * gn + 0.0722 * bn;
                var sf = 1.0 + Lerp(s.SaturationMin, s.SaturationMax, mask) / 100.0;
                rn = lum + (rn - lum) * sf;
                gn = lum + (gn - lum) * sf;
                bn = lum + (bn - lum) * sf;

                var hueDeg = Lerp(s.HueShiftMin, s.HueShiftMax, mask);
                if (Math.Abs(hueDeg) > 0.01)
                {
                    var (hue, sat, lig) = ColorMath.RgbToHsl(rn, gn, bn);
                    (rn, gn, bn) = ColorMath.HslToRgb(hue + hueDeg, sat, lig);
                }

                data[i] = ColorMath.Clamp255(bn * 255.0);
                data[i + 1] = ColorMath.Clamp255(gn * 255.0);
                data[i + 2] = ColorMath.Clamp255(rn * 255.0);
            }
        });
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
