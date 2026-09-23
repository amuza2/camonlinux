using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using camonlinux.Capture;
using camonlinux.Masking;
using camonlinux.Services;
using camonlinux.ViewModels;

namespace camonlinux.Views;

public partial class MainWindow : Window
{
    private SettingsService? _settings;
    private MaskPipeline? _maskPipeline;
    private MaskEditorWindow? _maskEditorWindow;
    private VirtualCameraService? _virtualCamera;
    private int _lastFrameW;
    private int _lastFrameH;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the capture service's frames to the preview surface and, optionally, to
    /// the virtual webcam. When a mask pipeline is provided it is registered as the
    /// capture service's <see cref="IFrameProcessor"/>, i.e. it runs on the streaming
    /// thread <b>before</b> each frame is cached — so the preview, photos and the
    /// virtual webcam all see the same masked/adjusted frame.
    /// </summary>
    public void ConnectCapture(ICaptureService capture, MaskPipeline? maskPipeline, VirtualCameraService? virtualCamera = null)
    {
        _maskPipeline = maskPipeline;
        _virtualCamera = virtualCamera;

        if (DataContext is MainWindowViewModel vm)
        {
            VideoSurfaceControl.CompositeAlpha = vm.IsMaskEnabled;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsMaskEnabled))
                    VideoSurfaceControl.CompositeAlpha = vm.IsMaskEnabled;
                else if (e.PropertyName == nameof(MainWindowViewModel.VirtualCamBackground))
                    _virtualCamera?.SetBackground(vm.VirtualCamBackground);
            };

            // Keep the on-preview mask handle in sync with the shape's centre.
            vm.MaskEditor.Shape.PropertyChanged += (_, _) => UpdateMaskHandlePosition();
        }

        // The mask stage lives in the capture path (not here) so the latest-frame
        // cache can never hold an unprocessed frame.
        capture.FrameProcessor = maskPipeline is null ? null : new MaskFrameProcessor(maskPipeline);

        capture.FrameReady += (_, frame) =>
        {
            // If the UI thread is behind on preview updates, drop this frame — the
            // frame was already processed and cached, so nothing is lost by skipping
            // the render (prevents CPU spikes and unbounded memory).
            if (VideoSurfaceControl.IsUiBackedUp)
                return;

            _lastFrameW = frame.Width;
            _lastFrameH = frame.Height;
            VideoSurfaceControl.PushFrame(frame);
            _virtualCamera?.PushFrame(
                frame.Data, frame.Width, frame.Height,
                compositeMask: _maskPipeline is { Enabled: true });
        };
    }

    /// <summary>
    /// Toolbar Webcam toggle: shares the live (masked/adjusted) preview as a virtual
    /// webcam via v4l2loopback. If the module isn't loaded, tells the user how to.
    /// </summary>
    private async void OnVirtualCamToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || DataContext is not MainWindowViewModel vm)
            return;

        var enable = tb.IsChecked == true;
        vm.IsVirtualCamEnabled = enable;
        if (_virtualCamera is null)
        {
            vm.IsVirtualCamEnabled = false;
            tb.IsChecked = false;
            vm.ShowToast("Virtual webcam is not available.");
            return;
        }

        if (enable)
        {
            var device = VirtualCameraService.FindLoopbackDevice();
            if (device is null)
            {
                vm.IsVirtualCamEnabled = false;
                tb.IsChecked = false;
                vm.ShowToast("Virtual webcam unavailable — v4l2loopback module not loaded");
                var help = new VirtualCamHelpWindow();
                await help.ShowDialog(this);
                return;
            }

            var w = _lastFrameW > 0 ? _lastFrameW : 1280;
            var h = _lastFrameH > 0 ? _lastFrameH : 720;
            if (_virtualCamera.Start(device, w, h, 30))
            {
                vm.ShowToast($"Virtual webcam live on {device}");
            }
            else
            {
                vm.IsVirtualCamEnabled = false;
                tb.IsChecked = false;
                vm.ShowToast($"Failed to start virtual webcam: {_virtualCamera.LastError}");
            }
        }
        else
        {
            _virtualCamera.Stop();
            vm.ShowToast("Virtual webcam stopped.");
        }
    }

    /// <summary>
    /// Toolbar Mask toggle: toggles masking and opens/closes the editor. Changes made
    /// in the editor apply to the live preview immediately; closing the editor window
    /// keeps the mask active (it only turns off via this toolbar toggle).
    /// </summary>
    private void OnMaskToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || DataContext is not MainWindowViewModel vm)
            return;
        vm.IsMaskEnabled = tb.IsChecked == true;
        if (vm.IsMaskEnabled)
        {
            if (_maskEditorWindow is null)
            {
                _maskEditorWindow = new MaskEditorWindow { DataContext = vm.MaskEditor };
                _maskEditorWindow.Closed += (_, _) => _maskEditorWindow = null;
            }
            _maskEditorWindow.Show(this);
            UpdateMaskHandlePosition();
        }
        else if (_maskEditorWindow is not null)
        {
            _maskEditorWindow.Close();
            _maskEditorWindow = null;
        }
    }

    /// <summary>Gives the window access to settings for window-state persistence.</summary>
    public void SetSettings(SettingsService settings)
    {
        _settings = settings;
        // Apply the virtual-webcam background (fills masked-out areas) from settings.
        _virtualCamera?.SetBackground(settings.Settings.VirtualCamBackground);
    }

    // ------------------------------------------------------------------ //
    // Draggable mask-position overlay
    // ------------------------------------------------------------------ //

    private bool _maskDragging;

    private void OnPreviewSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateMaskHandlePosition();

    private void UpdateMaskHandlePosition()
    {
        if (DataContext is not MainWindowViewModel vm || MaskHandle is null)
            return;
        var shape = vm.MaskEditor.Shape;
        var b = VideoSurfaceControl.Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;
        Canvas.SetLeft(MaskHandle, (shape.CenterX / 100.0) * b.Width - MaskHandle.Width / 2);
        Canvas.SetTop(MaskHandle, (shape.CenterY / 100.0) * b.Height - MaskHandle.Height / 2);
    }

    private void OnMaskHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Ellipse handle)
            return;
        _maskDragging = true;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnMaskHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_maskDragging || DataContext is not MainWindowViewModel vm)
            return;
        var b = VideoSurfaceControl.Bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return;
        var pos = e.GetPosition(VideoSurfaceControl);
        vm.MaskEditor.Shape.CenterX = Math.Clamp(pos.X / b.Width * 100.0, 0, 100);
        vm.MaskEditor.Shape.CenterY = Math.Clamp(pos.Y / b.Height * 100.0, 0, 100);
        UpdateMaskHandlePosition();
        e.Handled = true;
    }

    private void OnMaskHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        _maskDragging = false;
    }

    /// <summary>Double-clicking a gallery item opens it with the default app.</summary>
    private void OnGalleryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenSelectedCommand.Execute(null);
    }

    /// <summary>Keeps the VM's multi-selection collection in sync with the list box.</summary>
    private void OnGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not ListBox listBox)
            return;
        if (listBox.SelectedItems is not { } selected)
            return;
        vm.SelectedGalleryItems.Clear();
        foreach (var item in selected)
        {
            if (item is camonlinux.Models.MediaItem media)
                vm.SelectedGalleryItems.Add(media);
        }
    }

    /// <summary>Mouse wheel over the preview adjusts the digital zoom.</summary>
    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.AdjustZoom(e.Delta.Y);
    }

    // ------------------------------------------------------------------ //
    // Right panel resize grip
    // ------------------------------------------------------------------ //

    private bool _resizingPanel;
    private Point _resizePointerOrigin;
    private double _resizeStartWidth;

    /// <summary>
    /// Starts dragging the right (captures / effects) panel's left edge. The panel is
    /// docked to the right, so moving the pointer left makes it wider.
    /// </summary>
    private void OnPanelResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control grip || DataContext is not MainWindowViewModel vm)
            return;
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;

        _resizingPanel = true;
        _resizePointerOrigin = e.GetPosition(this);
        _resizeStartWidth = vm.RightPanelWidth;

        // Capture so the drag keeps working once the pointer leaves the 6px grip.
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    /// <summary>Resizes the panel while the grip is dragged.</summary>
    private void OnPanelResizeGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizingPanel || DataContext is not MainWindowViewModel vm)
            return;

        var movedLeft = _resizePointerOrigin.X - e.GetPosition(this).X;
        vm.RightPanelWidth = MainWindowViewModel.PanelWidthForDrag(_resizeStartWidth, movedLeft, Bounds.Width);
        e.Handled = true;
    }

    private void OnPanelResizeGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizingPanel)
            return;

        _resizingPanel = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>Double-clicking the grip restores the panel's default width.</summary>
    private void OnPanelResizeReset(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.RightPanelWidth = MainWindowViewModel.DefaultPanelWidth;
        e.Handled = true;
    }

    /// <summary>Shrinking the window must not let the panel eat the whole preview.</summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || e.NewSize.Width <= 0)
            return;

        // The saved width is already clamped to the static range on load; this only
        // handles the window becoming too small for it.
        vm.RightPanelWidth = Math.Clamp(
            vm.RightPanelWidth,
            MainWindowViewModel.MinPanelWidth,
            MainWindowViewModel.MaxPanelWidthForWindow(e.NewSize.Width));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_settings is not null && !_settings.Settings.IsMaximized)
        {
            WindowState = WindowState.Normal;
            Width = _settings.Settings.WinWidth;
            Height = _settings.Settings.WinHeight;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_settings is not null)
        {
            _settings.Settings.IsMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _settings.Settings.WinWidth = Width;
                _settings.Settings.WinHeight = Height;
            }
            _settings.Save();
        }
    }
}