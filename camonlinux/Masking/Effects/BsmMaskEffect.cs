using System;
using System.Threading.Tasks;

namespace camonlinux.Masking.Effects;

/// <summary>
/// BSM — background-subtraction "alpha wipe". Compares each live frame to a stored
/// background reference: pixels that differ from the background (new content) become
/// opaque; pixels that still match the background fade out over <c>FadeOutTime</c>,
/// "wiping" the static scene away so content reveals as it changes. A per-pixel fade
/// state gives a smooth temporal reveal/fade. <c>FreezeFrame</c> stops live updates.
/// </summary>
public sealed class BsmMaskEffect : IMaskEffect
{
    public string Name => "BSM";
    public bool Enabled { get; set; } = false;
    public BsmMaskSettings Settings { get; }

    public BsmMaskEffect(BsmMaskSettings settings) => Settings = settings;

    private readonly object _lock = new();
    private byte[]? _background;   // BGRA reference frame
    private float[]? _fade;        // per-pixel 0..1 wipe state
    private const float Dt = 1f / 30f;         // assumed frame time
    private const float FadeInTime = 0.05f;    // seconds for changed content to fade in

    public void Apply(MaskFrame frame, byte[] coverage)
    {
        var s = Settings;
        var w = frame.Width;
        var h = frame.Height;
        var count = w * h;
        var data = frame.Data;

        // One-shot triggers from the UI (Capture background / Reset background).
        if (s.CaptureBackground)
        {
            lock (_lock)
            {
                if (_background is null || _background.Length != data.Length)
                    _background = new byte[data.Length];
                Array.Copy(data, _background, data.Length);
                _fade = new float[count];
            }
            s.CaptureBackground = false;
        }

        if (s.ResetBackground)
        {
            lock (_lock)
            {
                _background = null;
                _fade = null;
            }
            s.ResetBackground = false;
        }

        lock (_lock)
        {
            if (_background is null)
            {
                // No reference yet: nothing is wiped, everything stays opaque.
                Array.Fill(coverage, (byte)255, 0, count);
                return;
            }

            if (_fade is null || _fade.Length != count)
                _fade = new float[count];

            var bg = _background;
            var fade = _fade;
            var freeze = s.FreezeFrame;
            var threshold = (float)s.Threshold;
            var fadeRate = (float)Math.Max(0.02, s.FadeOutTime);
            var fadeDelta = Dt / fadeRate;
            var fadeInDelta = Dt / FadeInTime;

            Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    var i = (row + x) * 4;
                    var diff = (Math.Abs(data[i] - bg[i])
                              + Math.Abs(data[i + 1] - bg[i + 1])
                              + Math.Abs(data[i + 2] - bg[i + 2])) / 3.0;

                    var st = fade[row + x];
                    if (!freeze)
                    {
                        st = diff > threshold
                            ? Math.Min(1f, st + fadeInDelta)  // changed content: reveal fast
                            : Math.Max(0f, st - fadeDelta);   // matches bg: wipe/fade out
                        fade[row + x] = st;
                    }
                    coverage[row + x] = (byte)(st * 255f + 0.5f);
                }
            });
        }
    }
}
