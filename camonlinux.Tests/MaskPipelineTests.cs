using camonlinux.Masking;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// The mask pipeline is the numeric core of the masking feature and is driven by
/// several interacting flags (enabled / mode / invert / per-effect enabled). These
/// tests cover the alpha semantics, the effect-type dispatch and the mode switch.
/// </summary>
public class MaskPipelineTests
{
    private const byte Opaque = 255;

    // ------------------------------------------------------------------ //
    // Gating
    // ------------------------------------------------------------------ //

    [Fact]
    public void Apply_WhenDisabled_LeavesTheFrameUntouched()
    {
        var pipeline = new MaskPipeline { Enabled = false };
        pipeline.Effects.Add(new FakeCoverageEffect(0));
        var frame = CreateFrame(8, alpha: 0x11);
        var original = (byte[])frame.Data.Clone();

        pipeline.Apply(frame);

        Assert.Equal(original, frame.Data);
    }

    [Fact]
    public void Apply_WithNoEffects_OnlyInitialisesAlphaToOpaque()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        var frame = CreateFrame(4, alpha: 0x11);
        SetRgb(frame, 0x01);

        pipeline.Apply(frame);

        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(Opaque, a));
        Assert.All(EnumerateRgb(frame), c => Assert.Equal(0x01, c));
    }

    [Fact]
    public void Apply_SkipsDisabledEffects()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        var disabled = new FakeCoverageEffect(0, enabled: false);
        pipeline.Effects.Add(disabled);
        var frame = CreateFrame(4);

        pipeline.Apply(frame);

        Assert.Equal(0, disabled.ApplyCount);
        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(Opaque, a));
    }

    // ------------------------------------------------------------------ //
    // Alpha mode
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(254)]
    [InlineData(255)]
    public void Apply_AndsCoverageIntoAlpha(byte coverage)
    {
        var pipeline = new MaskPipeline { Enabled = true };
        pipeline.Effects.Add(new FakeCoverageEffect(coverage));
        var frame = CreateFrame(5);

        pipeline.Apply(frame);

        // alpha = 255 * coverage / 255
        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(coverage, a));
    }

    [Fact]
    public void Apply_StacksMasksMultiplicatively()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        pipeline.Effects.Add(new FakeCoverageEffect(128));
        pipeline.Effects.Add(new FakeCoverageEffect(64));
        var frame = CreateFrame(3);

        pipeline.Apply(frame);

        // 255 -> 128 -> 128 * 64 / 255 = 32
        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(32, a));
    }

    [Fact]
    public void Apply_Invert_FlipsTheFinalAlpha()
    {
        var pipeline = new MaskPipeline { Enabled = true, Invert = true };
        pipeline.Effects.Add(new FakeCoverageEffect(64));
        var frame = CreateFrame(3);

        pipeline.Apply(frame);

        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(255 - 64, a));
    }

    [Fact]
    public void Apply_InPlaceEffect_ControlsAlphaDirectlyWithoutAndingCoverage()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        // The coverage effect runs first and leaves 128 in alpha; the in-place effect
        // then sets alpha outright, and must NOT be ANDed a second time.
        pipeline.Effects.Add(new FakeCoverageEffect(128));
        pipeline.Effects.Add(new FakeInPlaceEffect(77));
        var frame = CreateFrame(3);

        pipeline.Apply(frame);

        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(77, a));
    }

    [Fact]
    public void Apply_DoesNotModifyRgbInAlphaMode()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        pipeline.Effects.Add(new FakeCoverageEffect(0));
        var frame = CreateFrame(4);
        SetRgb(frame, 0x42);

        pipeline.Apply(frame);

        Assert.All(EnumerateRgb(frame), c => Assert.Equal(0x42, c));
    }

    // ------------------------------------------------------------------ //
    // Adjustment mode
    // ------------------------------------------------------------------ //

    [Fact]
    public void Apply_AdjustmentMode_RunsColourAdjustmentsAndMakesTheFrameOpaque()
    {
        var pipeline = new MaskPipeline { Enabled = true, Mode = MaskMode.Adjustment };
        var adjustment = new FakeColourAdjustmentEffect();
        pipeline.Effects.Add(new FakeCoverageEffect(0));
        pipeline.Effects.Add(adjustment);
        var frame = CreateFrame(3);

        pipeline.Apply(frame);

        Assert.Equal(1, adjustment.ApplyCount);
        Assert.All(EnumerateAlpha(frame), a => Assert.Equal(Opaque, a));
        Assert.All(EnumerateRgb(frame), c => Assert.Equal(0xEE, c));
    }

    [Fact]
    public void Apply_AlphaMode_NeverRunsColourAdjustmentEffects()
    {
        var pipeline = new MaskPipeline { Enabled = true, Mode = MaskMode.Alpha };
        var adjustment = new FakeColourAdjustmentEffect();
        pipeline.Effects.Add(adjustment);
        var frame = CreateFrame(3);

        pipeline.Apply(frame);

        Assert.Equal(0, adjustment.ApplyCount);
        Assert.All(EnumerateRgb(frame), c => Assert.NotEqual(0xEE, c));
    }

    // ------------------------------------------------------------------ //
    // Buffer reuse
    // ------------------------------------------------------------------ //

    [Fact]
    public void Apply_ReusesItsScratchBufferAcrossFrames()
    {
        var pipeline = new MaskPipeline { Enabled = true };
        pipeline.Effects.Add(new FakeCoverageEffect(200));

        var small = CreateFrame(4);
        pipeline.Apply(small);
        Assert.All(EnumerateAlpha(small), a => Assert.Equal(200, a));

        // A larger frame afterwards must still be handled correctly (the scratch buffer
        // is grown, not reset).
        var large = CreateFrame(1024);
        pipeline.Apply(large);
        Assert.All(EnumerateAlpha(large), a => Assert.Equal(200, a));
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    /// <summary>Creates a single-row BGRA frame whose alpha bytes start at <paramref name="alpha"/>.</summary>
    private static MaskFrame CreateFrame(int pixels, byte alpha = Opaque)
    {
        var data = new byte[pixels * 4];
        for (var p = 0; p < pixels; p++)
            data[p * 4 + 3] = alpha;
        return new MaskFrame(data, pixels, 1);
    }

    private static void SetRgb(MaskFrame frame, byte value)
    {
        for (var i = 0; i < frame.Data.Length; i++)
        {
            if (i % 4 != 3)
                frame.Data[i] = value;
        }
    }

    private static System.Collections.Generic.IEnumerable<byte> EnumerateAlpha(MaskFrame frame)
    {
        for (var p = 0; p < frame.PixelCount; p++)
            yield return frame.Data[p * 4 + 3];
    }

    private static System.Collections.Generic.IEnumerable<byte> EnumerateRgb(MaskFrame frame)
    {
        for (var p = 0; p < frame.PixelCount; p++)
        {
            yield return frame.Data[p * 4];
            yield return frame.Data[p * 4 + 1];
            yield return frame.Data[p * 4 + 2];
        }
    }

    // ------------------------------------------------------------------ //
    // Test doubles
    // ------------------------------------------------------------------ //

    private sealed class FakeCoverageEffect : IMaskEffect
    {
        private readonly byte _coverage;

        public FakeCoverageEffect(byte coverage, bool enabled = true)
        {
            _coverage = coverage;
            Enabled = enabled;
        }

        public string Name => "fake-coverage";

        public bool Enabled { get; set; }

        public int ApplyCount { get; private set; }

        public void Apply(MaskFrame frame, byte[] coverage)
        {
            ApplyCount++;
            for (var p = 0; p < frame.PixelCount; p++)
                coverage[p] = _coverage;
        }
    }

    private sealed class FakeInPlaceEffect : IMaskEffect, IInPlaceEffect
    {
        private readonly byte _alpha;

        public FakeInPlaceEffect(byte alpha) => _alpha = alpha;

        public string Name => "fake-in-place";

        public bool Enabled { get; set; } = true;

        public void Apply(MaskFrame frame, byte[] coverage)
        {
            for (var p = 0; p < frame.PixelCount; p++)
                frame.Data[p * 4 + 3] = _alpha;
        }
    }

    private sealed class FakeColourAdjustmentEffect : IColorAdjustmentEffect
    {
        public string Name => "fake-adjustment";

        public bool Enabled { get; set; } = true;

        public int ApplyCount { get; private set; }

        public void Apply(MaskFrame frame, byte[] coverage)
        {
            ApplyCount++;
            for (var p = 0; p < frame.PixelCount; p++)
            {
                frame.Data[p * 4] = 0xEE;
                frame.Data[p * 4 + 1] = 0xEE;
                frame.Data[p * 4 + 2] = 0xEE;
            }
        }
    }
}
