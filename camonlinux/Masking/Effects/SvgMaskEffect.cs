using System;
using System.Threading.Tasks;
using camonlinux.Masking.Svg;
using SkiaSharp;

namespace camonlinux.Masking.Effects;

/// <summary>
/// SVG mask. Renders the SVG text to a raster mask once (cached until the text
/// changes), then samples it into per-frame coverage at the requested scale /
/// position. The raster is re-rendered on the streaming thread only when the SVG
/// text changes (rare), so steady-state cost is a cheap resample.
/// </summary>
public sealed class SvgMaskEffect : IMaskEffect
{
    public string Name => "SVG";
    public bool Enabled { get; set; } = false;
    public SvgMaskSettings Settings { get; }

    public SvgMaskEffect(SvgMaskSettings settings) => Settings = settings;

    private readonly object _lock = new();
    private string _cachedSvg = "";
    private SKBitmap? _mask;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var s = Settings;
        var text = s.SvgText ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            Array.Clear(coverage, 0, frame.PixelCount);
            return;
        }

        EnsureMask(text);

        SKBitmap? mask;
        lock (_lock)
            mask = _mask;
        if (mask is null || mask.Width <= 0 || mask.Height <= 0)
        {
            Array.Clear(coverage, 0, frame.PixelCount);
            return;
        }

        var w = frame.Width;
        var h = frame.Height;
        var mw = mask.Width;
        var mh = mask.Height;
        var invert = s.Invert;

        // Target draw size per Scale By.
        double drawW, drawH;
        switch (s.ScaleBy)
        {
            case SvgScaleBy.Width:
                drawW = s.Width;
                drawH = s.Width * mh / mw;
                break;
            case SvgScaleBy.Height:
                drawH = s.Height;
                drawW = s.Height * mw / mh;
                break;
            default: // Both
                drawW = s.Width;
                drawH = s.Height;
                break;
        }

        var scaleX = mw / drawW;   // mask texels per output pixel
        var scaleY = mh / drawH;
        var offX = (w - drawW) / 2.0 + s.PositionX; // top-left of the mask in frame px
        var offY = (h - drawH) / 2.0 + s.PositionY;

        var pixels = mask.Pixels; // SKColor[], premultiplied

        Parallel.For(0, h, y =>
        {
            var rowBase = y * w;
            var sy = (y - offY) * scaleY;
            var syi = (int)sy;
            var insideY = sy >= 0 && syi < mh;
            for (var x = 0; x < w; x++)
            {
                if (!insideY)
                {
                    coverage[rowBase + x] = 0;
                    continue;
                }
                var sx = (x - offX) * scaleX;
                var sxi = (int)sx;
                if (sx < 0 || sxi >= mw)
                {
                    coverage[rowBase + x] = 0;
                    continue;
                }
                var lum = pixels[syi * mw + sxi].Red; // white mask -> red channel
                var cov = lum / 255.0;
                if (invert)
                    cov = 1.0 - cov;
                coverage[rowBase + x] = (byte)(cov * 255.0 + 0.5);
            }
        });
    }

    private void EnsureMask(string text)
    {
        lock (_lock)
        {
            if (text == _cachedSvg && _mask is not null)
                return;
            _mask?.Dispose();
            _mask = SvgRasterizer.Rasterize(text);
            _cachedSvg = text;
        }
    }
}
