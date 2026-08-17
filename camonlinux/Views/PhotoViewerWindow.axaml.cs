using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace camonlinux.Views;

/// <summary>
/// A lightweight in-app photo viewer with wheel zoom (to the viewport centre) and
/// drag-to-pan. Zoom works by resizing the image, so the ScrollViewer's scroll
/// extent grows naturally. Photos double-clicked in the gallery open here instead
/// of an external app; videos still use the system player.
/// </summary>
public partial class PhotoViewerWindow : Window
{
    private readonly Bitmap? _bitmap;
    private double _zoom = 1.0;
    private bool _dragging;
    private Point _dragStart;
    private Vector _scrollStart;

    /// <summary>Parameterless ctor exists for the XAML resource loader; the path ctor is used in practice.</summary>
    public PhotoViewerWindow()
    {
        InitializeComponent();
    }

    public PhotoViewerWindow(string path) : this()
    {
        _bitmap = new Bitmap(path);
        Photo.Source = _bitmap;
        TitleText.Text = System.IO.Path.GetFileName(path);
        Title = TitleText.Text;
        FitToWindow();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => AdjustZoom(0.15);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => AdjustZoom(-0.15);
    private void OnZoomReset(object? sender, RoutedEventArgs e) => AdjustZoomTo(1.0);
    private void OnZoomFit(object? sender, RoutedEventArgs e) => FitToWindow();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        AdjustZoom(e.Delta.Y > 0 ? 0.15 : -0.15);
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        _dragStart = e.GetPosition(Canvas);
        _scrollStart = Scroller.Offset;
        e.Pointer.Capture(Canvas);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;
        var pos = e.GetPosition(Canvas);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        Scroller.Offset = ClampOffset(new Vector(_scrollStart.X - dx, _scrollStart.Y - dy));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Changes the zoom by a delta, keeping the viewport centre stable.</summary>
    private void AdjustZoom(double delta) => AdjustZoomTo(_zoom + delta);

    private void AdjustZoomTo(double target)
    {
        if (_bitmap is null)
            return;
        // Remember where the viewport centre is (as a fraction of the current extent).
        var cx = Scroller.Offset.X + Scroller.Viewport.Width / 2;
        var cy = Scroller.Offset.Y + Scroller.Viewport.Height / 2;
        var fx = Scroller.Extent.Width > 0 ? cx / Scroller.Extent.Width : 0.5;
        var fy = Scroller.Extent.Height > 0 ? cy / Scroller.Extent.Height : 0.5;

        _zoom = Math.Clamp(target, 0.05, 8.0);
        ApplyZoom();

        // Keep that same fraction at the centre of the (resized) viewport.
        var nx = fx * Scroller.Extent.Width - Scroller.Viewport.Width / 2;
        var ny = fy * Scroller.Extent.Height - Scroller.Viewport.Height / 2;
        Scroller.Offset = ClampOffset(new Vector(nx, ny));
    }

    private void FitToWindow()
    {
        if (_bitmap is null)
            return;
        var w = Math.Max(1.0, Scroller.Viewport.Width);
        var h = Math.Max(1.0, Scroller.Viewport.Height);
        var scale = Math.Min(w / _bitmap.PixelSize.Width, h / _bitmap.PixelSize.Height);
        _zoom = Math.Clamp(scale, 0.05, 8.0);
        ApplyZoom();
        Scroller.Offset = new Vector(0, 0);
    }

    private void ApplyZoom()
    {
        if (_bitmap is null)
            return;
        Photo.Width = _bitmap.PixelSize.Width * _zoom;
        Photo.Height = _bitmap.PixelSize.Height * _zoom;
    }

    private static Vector ClampOffset(Vector offset)
    {
        // Negative offsets clamp to zero; the ScrollViewer clamps the max itself.
        return new Vector(Math.Max(0, offset.X), Math.Max(0, offset.Y));
    }
}
