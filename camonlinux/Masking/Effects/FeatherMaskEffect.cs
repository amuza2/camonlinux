using System;

namespace camonlinux.Masking.Effects;

/// <summary>
/// Edge-feather for the current mask: a separable (horizontal + vertical) box blur
/// over the frame's alpha channel. The blurred mask replaces alpha in place
/// (<see cref="IInPlaceEffect"/>), so it feathers whatever mask was built before it.
/// Uses a sliding-window sum for O(pixels) cost per pass and reusable scratch buffers.
/// </summary>
public sealed class FeatherMaskEffect : IMaskEffect, IInPlaceEffect
{
    public string Name => "Feather";
    public bool Enabled { get; set; } = true;
    public FeatherMaskSettings Settings { get; }

    private byte[] _tmp = Array.Empty<byte>();
    private int[] _prefix = Array.Empty<int>();

    public FeatherMaskEffect(FeatherMaskSettings settings) => Settings = settings;

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var w = frame.Width;
        var h = frame.Height;
        var radius = (int)Math.Max(0, Settings.Size);
        var data = frame.Data;
        if (radius == 0)
            return;

        if (_tmp.Length < w * h)
            _tmp = new byte[w * h];
        if (_prefix.Length < Math.Max(w, h) + 1)
            _prefix = new int[Math.Max(w, h) + 1];

        var win = 2 * radius + 1;

        // Horizontal pass: alpha -> _tmp
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            BuildPrefix(data, _prefix, row, 0, w, radius);
            for (var x = 0; x < w; x++)
            {
                var lo = x - radius < 0 ? 0 : x - radius;
                var hi = x + radius >= w ? w - 1 : x + radius;
                _tmp[row + x] = (byte)((_prefix[hi + 1] - _prefix[lo]) / (hi - lo + 1));
            }
        }

        // Vertical pass: _tmp -> alpha
        for (var x = 0; x < w; x++)
        {
            BuildPrefixCol(_tmp, _prefix, x, 0, h, w);
            for (var y = 0; y < h; y++)
            {
                var lo = y - radius < 0 ? 0 : y - radius;
                var hi = y + radius >= h ? h - 1 : y + radius;
                var avg = (_prefix[hi + 1] - _prefix[lo]) / (hi - lo + 1);
                data[(y * w + x) * 4 + 3] = (byte)avg;
            }
        }
    }

    /// <summary>Builds a prefix sum over row-major alpha at <c>base + i*4 + 3</c>.</summary>
    private static void BuildPrefix(byte[] data, int[] prefix, int rowBase, int start, int count, int stride)
    {
        prefix[0] = 0;
        for (var i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + data[(rowBase + start + i) * 4 + 3];
    }

    /// <summary>Builds a prefix sum down a column of <c>_tmp</c> (row-major, stride = w).</summary>
    private static void BuildPrefixCol(byte[] src, int[] prefix, int col, int start, int count, int stride)
    {
        prefix[0] = 0;
        for (var i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + src[(start + i) * stride + col];
    }
}
