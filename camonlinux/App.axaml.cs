using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Threading;
using camonlinux.Capture;
using camonlinux.Services;
using camonlinux.ViewModels;
using camonlinux.Views;

namespace camonlinux;

public partial class App : Application
{
    private static Mutex? s_singleInstanceMutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only one instance may use the camera at a time.
            s_singleInstanceMutex = new Mutex(true, "camonlinux_single_instance", out var createdNew);
            if (!createdNew)
            {
                try { NotificationService.Notify("camonlinux", "camonlinux is already running."); }
                catch { /* notification is best-effort */ }
                Environment.Exit(0);
                return;
            }

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