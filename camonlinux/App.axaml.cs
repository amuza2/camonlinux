using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using camonlinux.Capture;
using camonlinux.Services;
using camonlinux.ViewModels;
using camonlinux.Views;

namespace camonlinux;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Compose the app's services (a simple manual composition root —
            // no DI container needed for an app this size).
            var settings = new SettingsService();
            var capture = new GStreamerCaptureService();
            var mediaLibrary = new MediaLibraryService();

            var viewModel = new MainWindowViewModel(capture, settings, mediaLibrary);
            var window = new MainWindow
            {
                DataContext = viewModel
            };
            window.ConnectCapture(capture);

            desktop.MainWindow = window;

            desktop.Exit += async (_, _) =>
            {
                await capture.StopPreviewAsync();
                await capture.DisposeAsync();
                mediaLibrary.Dispose();
            };

            // Start device discovery once the window is shown.
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}