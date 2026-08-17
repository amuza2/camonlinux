using System;

namespace camonlinux.Masking;

/// <summary>
/// GPU extension point for mask effects (future work).
///
/// The current pipeline is fully CPU (Parallel.For over rows + SIMD in the alpha
/// buffer ops) and comfortably holds 30 fps at 1080p with several stacked effects.
/// A GPU path via SkiaSharp <c>SKShader</c>/SKSL would help for very large frame
/// sizes or when many effects stack. It is intentionally NOT wired yet: the app
/// relies on Avalonia.Skia's pinned SkiaSharp native library, and adding a separate
/// SkiaSharp reference risks a libSkiaSharp version mismatch at startup on Linux.
///
/// When implemented, a GPU effect would render into a shared <c>SKSurface</c> for the
/// whole frame (one surface, reused — never recreated per frame) and the pipeline
/// would fall back to the CPU path whenever a GPU effect can't be built. The shape /
/// gradient / chroma / BSM effects are all expressible as SKSL fragment shaders
/// (signed-distance shapes, linear gradients, chroma distance, background diff).
/// </summary>
public interface IGpuMaskEffect : IMaskEffect
{
    /// <summary>Compiles/creates the shader for the current settings; null if unavailable.</summary>
    object? TryBuildShader(int frameWidth, int frameHeight);
}
