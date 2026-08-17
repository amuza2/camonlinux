using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace camonlinux.Views;

/// <summary>
/// Explains how to enable the v4l2loopback virtual camera and copies the modprobe
/// command to the clipboard.
/// </summary>
public partial class VirtualCamHelpWindow : Window
{
    public VirtualCamHelpWindow()
    {
        InitializeComponent();
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not null)
            await top.Clipboard.SetTextAsync(CommandBox.Text);
        Close(true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
