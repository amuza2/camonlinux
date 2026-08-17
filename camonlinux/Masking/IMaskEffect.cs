namespace camonlinux.Masking;

/// <summary>How the final mask is applied to the frame.</summary>
public enum MaskMode
{
    /// <summary>The mask modulates the frame's ALPHA channel (transparent areas show the checkerboard in the preview).</summary>
    Alpha,

    /// <summary>The mask drives color adjustments only in masked regions; the frame stays fully opaque.</summary>
    Adjustment
}

/// <summary>
/// A single masking effect. Mask-producing effects write a per-pixel coverage
/// (0..255) into <c>coverage</c>; the pipeline ANDs it into the frame's alpha
/// channel. Color-adjustment effects implement <see cref="IColorAdjustmentEffect"/>
/// and instead read the current alpha and modify RGB.
///
/// Implementations must be allocation-free in the per-pixel path and safe to call
/// from the capture streaming thread (the settings are read into locals up front).
/// </summary>
public interface IMaskEffect
{
    string Name { get; }

    bool Enabled { get; set; }

    /// <summary>Computes this effect's contribution for the current frame.</summary>
    /// <param name="frame">The BGRA32 frame (alpha channel holds the running mask).</param>
    /// <param name="coverage">A per-pixel buffer (same length as <see cref="MaskFrame.PixelCount"/>); the effect writes 0..255 coverage here.</param>
    void Apply(MaskFrame frame, byte[] coverage);
}

/// <summary>Marker for effects that apply a color transform using the frame's alpha mask (Adjustment mode).</summary>
public interface IColorAdjustmentEffect : IMaskEffect
{
}

/// <summary>
/// Marker for effects that modify the frame's alpha (or RGB) directly in place
/// (e.g. an edge-feather blur of the current mask). The pipeline does NOT AND
/// their output into alpha — the <c>coverage</c> parameter is ignored.
/// </summary>
public interface IInPlaceEffect : IMaskEffect
{
}
