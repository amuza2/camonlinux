using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using camonlinux.Capture;
using camonlinux.Services;
using camonlinux.ViewModels;

namespace camonlinux.Views;

public partial class MainWindow : Window
{
    private SettingsService? _settings;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Wires the capture service's frames to the preview surface.</summary>
    public void ConnectCapture(ICaptureService capture)
    {
        capture.FrameReady += (_, frame) => VideoSurfaceControl.PushFrame(frame);
    }

    /// <summary>Gives the window access to settings for window-state persistence.</summary>
    public void SetSettings(SettingsService settings) => _settings = settings;

    /// <summary>Double-clicking a gallery item opens it with the default app.</summary>
    private void OnGalleryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenSelectedCommand.Execute(null);
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