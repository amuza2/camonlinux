using Avalonia.Controls;
using camonlinux.Capture;

namespace camonlinux.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Wires the capture service's frames to the preview surface.</summary>
    public void ConnectCapture(ICaptureService capture)
    {
        capture.FrameReady += (_, frame) => VideoSurfaceControl.PushFrame(frame);
    }
}