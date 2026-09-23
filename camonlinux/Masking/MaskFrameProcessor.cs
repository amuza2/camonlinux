using camonlinux.Capture;

namespace camonlinux.Masking;

/// <summary>
/// Runs the <see cref="MaskPipeline"/> as a capture-stage frame processor.
///
/// This used to live in the view (<c>MainWindow.ConnectCapture</c>), which was racy:
/// the capture service cached each frame as "latest" <i>before</i> the view had run
/// the mask, so a photo taken right after a skipped (~15 fps throttle) frame could
/// be encoded unmasked. Running the stage inside the capture path guarantees the
/// cached frame — and therefore photos and the virtual webcam — is always processed.
/// </summary>
public sealed class MaskFrameProcessor : IFrameProcessor
{
    private readonly MaskPipeline _pipeline;

    // Reused wrapper so no allocation happens per frame (see MaskFrame).
    private readonly MaskFrame _frame = new();

    // Masking is the expensive part (per-pixel over the whole frame), so it runs at
    // roughly half the capture rate. Dropped frames are not cached, which is what
    // keeps the "latest frame" invariant: whatever is cached has been masked.
    private int _frameCounter;

    public MaskFrameProcessor(MaskPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public bool Process(CameraFrame frame)
    {
        // Nothing to do while masking is off — let every frame through untouched.
        if (!_pipeline.Enabled || frame.Data.Length == 0)
            return true;

        if ((++_frameCounter & 1) != 0)
            return false;

        _frame.Set(frame.Data, frame.Width, frame.Height);
        _pipeline.Apply(_frame);
        return true;
    }
}
