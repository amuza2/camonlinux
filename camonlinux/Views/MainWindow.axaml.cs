using Avalonia.Controls;
using Avalonia.Input;
using camonlinux.Capture;
using camonlinux.ViewModels;

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

    /// <summary>Double-clicking a gallery item opens it with the default app.</summary>
    private void OnGalleryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenSelectedCommand.Execute(null);
    }
}