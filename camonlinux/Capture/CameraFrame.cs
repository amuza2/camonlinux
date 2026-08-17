namespace camonlinux.Capture;

/// <summary>
/// A raw BGRA frame (alpha already set to 255) captured from the camera,
/// ready to be rendered on the preview surface or encoded to an image.
/// </summary>
public sealed class CameraFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public int Stride => Width * BytesPerPixel;
    public const int BytesPerPixel = 4;

    public CameraFrame(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }
}
