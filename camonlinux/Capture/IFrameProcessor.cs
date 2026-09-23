namespace camonlinux.Capture;

/// <summary>
/// An in-place processing stage applied to every captured frame on the streaming
/// thread, <b>before</b> the frame is cached as "latest" and published through
/// <see cref="ICaptureService.FrameReady"/>.
///
/// Because the stage runs before caching, everything that reads the latest frame —
/// photo capture, the virtual webcam, the preview — always sees the processed
/// result. Implementations must be cheap and allocation-free: they run per frame.
/// </summary>
public interface IFrameProcessor
{
    /// <summary>
    /// Processes <paramref name="frame"/> in place (the data buffer is owned by the
    /// caller and may be mutated). Return <c>false</c> to drop the frame: it is then
    /// neither cached as the latest frame nor published via
    /// <see cref="ICaptureService.FrameReady"/>. Use this to deliberately run an
    /// expensive stage at a lower rate than the capture rate.
    /// </summary>
    bool Process(CameraFrame frame);
}
