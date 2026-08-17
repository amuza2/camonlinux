using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly MaskFrame _maskFrame = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the capture service's frames to the preview surface and, optionally, to
    /// the virtual webcam. When a mask pipeline is provided, it is applied in place
    /// (BGRA32) on the streaming thread before the frame is pushed, so the virtual
    /// webcam shows the masked/adjusted feed too.
    /// </summary>
    public void ConnectCapture(ICaptureService capture, MaskPipeline? maskPipeline, VirtualCameraService? virtualCamera = null)
    {
        _maskPipeline = maskPipeline;
        _virtualCamera = virtualCamera;

        // Composite the mask alpha in software while masking is enabled; fall back to
        // the fast copy path otherwise.
        if (DataContext is MainWindowViewModel vm)
        {
            VideoSurfaceControl.CompositeAlpha = vm.IsMaskEnabled;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsMaskEnabled))
                    VideoSurfaceControl.CompositeAlpha = vm.IsMaskEnabled;
            };
        }

        capture.FrameReady += (_, frame) =>
        {
            if (_maskPipeline is { Enabled: true } && frame.Data.Length > 0)
            {
                _maskFrame.Set(frame.Data, frame.Width, frame.Height);
                _maskPipeline.Apply(_maskFrame);
            }
            _lastFrameW = frame.Width;
            _lastFrameH = frame.Height;
            VideoSurfaceControl.PushFrame(frame);
            _virtualCamera?.PushFrame(frame.Data, frame.Width, frame.Height);
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
        }
        else if (_maskEditorWindow is not null)
        {
            _maskEditorWindow.Close();
            _maskEditorWindow = null;
        }
    }

    /// <summary>Gives the window access to settings for window-state persistence.</summary>
    public void SetSettings(SettingsService settings) => _settings = settings;

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