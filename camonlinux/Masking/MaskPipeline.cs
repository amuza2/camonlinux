using System;
using System.Collections.Generic;

namespace camonlinux.Masking;

/// <summary>
/// Chains masking effects per frame and applies the global mode + invert flag.
///
/// Pipeline semantics:
///  1. The frame's alpha channel is initialised to 255 (fully masked).
///  2. Each enabled mask-producing effect writes a 0..255 coverage buffer, which
///     is ANDed into alpha (alpha = alpha * coverage / 255) so stacked masks
///     intersect correctly.
///  3. A global Invert flips alpha (255 - a).
///  4. In <see cref="MaskMode.Adjustment"/>, colour-adjustment effects run using
///     the final alpha as the mask, then alpha is reset to 255 (the RGB changes
///     are what's shown).
///
/// Settings are plain fields (not observable) so they can be read cheaply from the
/// streaming thread; the UI writes them. A single scratch <c>coverage</c> buffer is
/// reused across frames — no per-frame allocation.
/// </summary>
public sealed class MaskPipeline
{
    public bool Enabled;
    public MaskMode Mode = MaskMode.Alpha;
    public bool Invert;
    public readonly List<IMaskEffect> Effects = new();

    private byte[] _coverage = Array.Empty<byte>();

    /// <summary>Applies the chain in place to <paramref name="frame"/> (BGRA32).</summary>
    public void Apply(MaskFrame frame)
    {
        if (!Enabled)
            return;

        var count = frame.PixelCount;
        EnsureScratch(count);

        FillAlpha(frame.Data, count, 255);

        // Mask producers first (they write coverage that is ANDed into alpha).
        foreach (var effect in Effects)
        {
            if (!effect.Enabled || effect is IColorAdjustmentEffect)
                continue;
            if (effect is IInPlaceEffect)
            {
                // Modifies alpha/RGB directly (e.g. feather blur) — no coverage AND.
                effect.Apply(frame, _coverage);
                continue;
            }
            Array.Clear(_coverage, 0, count);
            effect.Apply(frame, _coverage);
            AndCoverage(frame.Data, count, _coverage);
        }

        if (Invert)
            InvertAlpha(frame.Data, count);

        if (Mode == MaskMode.Adjustment)
        {
            // Colour adjustments use the final alpha mask, then the frame is opaque.
            foreach (var effect in Effects)
            {
                if (effect.Enabled && effect is IColorAdjustmentEffect adj)
                    adj.Apply(frame, _coverage);
            }
            FillAlpha(frame.Data, count, 255);
        }
    }

    private void EnsureScratch(int count)
    {
        if (_coverage.Length < count)
            _coverage = new byte[count];
    }

    // --- buffer helpers (hot loops: no LINQ, no allocation) ---

    private static void FillAlpha(byte[] data, int count, byte value)
    {
        for (var i = 3; i < count * 4; i += 4)
            data[i] = value;
    }

    private static void AndCoverage(byte[] data, int count, byte[] coverage)
    {
        // alpha = alpha * coverage / 255  (multiplicative AND of soft masks)
        for (var p = 0; p < count; p++)
        {
            var i = p * 4 + 3;
            data[i] = (byte)((data[i] * coverage[p]) / 255);
        }
    }

    private static void InvertAlpha(byte[] data, int count)
    {
        for (var p = 0; p < count; p++)
            data[p * 4 + 3] = (byte)(255 - data[p * 4 + 3]);
    }
}
