using System;
using System.Threading.Tasks;
using camonlinux.Masking.Geometry;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Shape mask (rectangle/circle/ellipse/polygon/star/heart/superformula). Writes
/// per-pixel coverage from the shape's signed distance + feathering. When
/// <see cref="ShapeMaskSettings.FrameCheck"/> is on, the shape outline is drawn
/// as a light-green overlay in the frame.
///
/// Perf: the row loop is parallelised with <see cref="Parallel.For"/>; vertices are
/// precomputed once per frame; no allocations in the per-pixel path.
/// </summary>
public sealed class ShapeMaskEffect : IMaskEffect
{
    public string Name => "Shape";
    public bool Enabled { get; set; } = true;
    public ShapeMaskSettings Settings { get; }

    public ShapeMaskEffect(ShapeMaskSettings settings) => Settings = settings;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var w = frame.Width;
        var h = frame.Height;
        var settings = Settings;
        var feather = settings.Feather;
        var featherAmt = settings.FeatherAmount;
        var frameCheck = settings.FrameCheck;
        var invert = settings.Invert;
        var data = frame.Data;

        var vertices = ShapeGeometry.ShapeVertices(settings, w, h);

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w;
            var rowDataBase = rowBase * 4;
            for (var x = 0; x < w; x++)
            {
                var d = ShapeGeometry.ShapeDistance(x + 0.5, y + 0.5, settings, w, h, vertices);
                var cov = ShapeGeometry.Coverage(d, feather, featherAmt);
                if (invert)
                    cov = 1.0 - cov;
                coverage[rowBase + x] = (byte)(cov * 255.0 + 0.5);

                if (frameCheck && Math.Abs(d) < 1.5)
                {
                    var pi = rowDataBase + x * 4;
                    data[pi] = 0x20;     // B
                    data[pi + 1] = 0xE0; // G
                    data[pi + 2] = 0x20; // R
                }
            }
        });
    }
}
