using System;
using System.Threading.Tasks;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Linear gradient mask. The mask is 1 on one side of a ramp and 0 on the other,
/// with a <see cref="GradientMaskSettings.Width"/>-pixel soft transition. Rotation
/// sets the ramp direction; <c>Position</c> offsets the ramp centre along the axis.
/// <c>DebugLines</c> draws the two ramp edges (red) into the frame.
/// </summary>
public sealed class GradientMaskEffect : IMaskEffect
{
    public string Name => "Gradient";
    public bool Enabled { get; set; } = true;
    public GradientMaskSettings Settings { get; }

    public GradientMaskEffect(GradientMaskSettings settings) => Settings = settings;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var w = frame.Width;
        var h = frame.Height;
        var settings = Settings;
        var data = frame.Data;

        var rad = settings.Rotation * Math.PI / 180.0;
        var dirX = Math.Cos(rad);
        var dirY = Math.Sin(rad);
        var width = Math.Max(1.0, settings.Width);
        var cx = w / 2.0 + settings.Position * dirX;
        var cy = h / 2.0 + settings.Position * dirY;
        var edge = width / 2.0;
        var invert = settings.Invert;

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w;
            var rowDataBase = rowBase * 4;
            for (var x = 0; x < w; x++)
            {
                var t = (x + 0.5 - cx) * dirX + (y + 0.5 - cy) * dirY;
                // 1 at t = -edge .. 0 at t = +edge (soft ramp)
                var cov = 0.5 - t / (width * 2.0);
                cov = cov < 0 ? 0 : (cov > 1 ? 1 : cov);
                if (invert)
                    cov = 1.0 - cov;
                coverage[rowBase + x] = (byte)(cov * 255.0 + 0.5);

                if (settings.DebugLines && Math.Abs(Math.Abs(t) - edge) < 1.5)
                {
                    var pi = rowDataBase + x * 4;
                    data[pi] = 0x20;     // B
                    data[pi + 1] = 0x20; // G
                    data[pi + 2] = 0xE0; // R (red edge)
                }
            }
        });
    }
}
