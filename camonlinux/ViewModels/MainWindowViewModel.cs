using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using camonlinux.Capture;
using camonlinux.Models;
using camonlinux.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace camonlinux.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ICaptureService _capture;
    private readonly SettingsService _settings;
    private readonly MediaLibraryService _mediaLibrary;
    private readonly Stopwatch _recordingStopwatch = new();
    private Timer? _recordingTimer;

    public ObservableCollection<CameraDevice> Devices { get; } = new();
    public ObservableCollection<MediaItem> GalleryItems { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private CameraDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private MediaItem? _selectedGalleryItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakePhotoCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRecordingCommand))]
    private bool _isPreviewActive;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _recordingTime = "00:00";

    [ObservableProperty]
    private bool _mirrored;

    public MainWindowViewModel(
        ICaptureService capture,
        SettingsService settings,
        MediaLibraryService mediaLibrary)
    {
        _capture = capture;
        _settings = settings;
        _mediaLibrary = mediaLibrary;
        _mirrored = settings.Settings.Mirrored;

        _capture.ErrorOccurred += (_, message) => StatusMessage = message;
    }

    // ------------------------------------------------------------------ //
    // Startup
    // ------------------------------------------------------------------ //

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsBusy = true;
        StatusMessage = "Looking for cameras…";

        await _capture.InitializeAsync();
        var devices = await _capture.RefreshDevicesAsync();

        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);

        if (Devices.Count == 0)
        {
            StatusMessage = "No camera found. Is it connected, and is your user in the 'video' group?";
            IsBusy = false;
            return;
        }

        SelectedDevice =
            Devices.FirstOrDefault(d => d.Id == _settings.Settings.LastDeviceId)
            ?? Devices[0];

        await StartPreviewAsync(SelectedDevice);
        RefreshGallery();
        IsBusy = false;
    }

    private async Task StartPreviewAsync(CameraDevice device)
    {
        try
        {
            await _capture.StartPreviewAsync(device);
            IsPreviewActive = true;
            StatusMessage = $"Using {device.Name}";
        }
        catch (Exception ex)
        {
            IsPreviewActive = false;
            StatusMessage = $"Could not open the camera: {ex.Message}";
        }
    }

    partial void OnSelectedDeviceChanged(CameraDevice? value)
    {
        if (value is null)
            return;

        _settings.Settings.LastDeviceId = value.Id;
        _settings.Save();
        _ = StartPreviewAsync(value);
    }

    partial void OnMirroredChanged(bool value)
    {
        _settings.Settings.Mirrored = value;
        _settings.Save();
        _capture.Mirrored = value;

        // The flip element is baked into the pipeline, so restart the preview.
        if (IsPreviewActive && SelectedDevice is not null)
            _ = StartPreviewAsync(SelectedDevice);
    }

    // ------------------------------------------------------------------ //
    // Device management
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        var devices = await _capture.RefreshDevicesAsync();
        Devices.Clear();
        foreach (var device in devices)
            Devices.Add(device);
        IsBusy = false;
    }

    // ------------------------------------------------------------------ //
    // Capture
    // ------------------------------------------------------------------ //

    [RelayCommand(CanExecute = nameof(CanTakePhoto))]
    private async Task TakePhotoAsync()
    {
        var directory = _settings.Settings.PhotoDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"picture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg");

        try
        {
            await _capture.TakePhotoAsync(path);
            StatusMessage = $"Photo saved to {path}";
            NotificationService.Notify("Photo taken", Path.GetFileName(path));
            RefreshGallery();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not take a photo: {ex.Message}";
        }
    }

    private bool CanTakePhoto() => IsPreviewActive && !IsRecording;

    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (!IsRecording)
        {
            var directory = _settings.Settings.VideoDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"video_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv");

            try
            {
                await _capture.StartRecordingAsync(path);
                IsRecording = true;
                _recordingStopwatch.Restart();
                _recordingTimer = new Timer(
                    _ => Dispatcher.UIThread.Post(() =>
                        RecordingTime = _recordingStopwatch.Elapsed.ToString(@"mm\:ss")),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1));
                StatusMessage = "Recording…";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not start recording: {ex.Message}";
            }
        }
        else
        {
            await _capture.StopRecordingAsync();
            IsRecording = false;
            _recordingTimer?.Dispose();
            _recordingTimer = null;
            RecordingTime = "00:00";
            StatusMessage = "Recording saved.";
            NotificationService.Notify("Video saved");
            RefreshGallery();
        }
    }

    private bool CanToggleRecording() => IsPreviewActive || IsRecording;

    // ------------------------------------------------------------------ //
    // Gallery
    // ------------------------------------------------------------------ //

    [RelayCommand]
    private void RefreshGallery()
    {
        var items = _mediaLibrary.LoadMedia(_settings.Settings.PhotoDirectory)
            .Concat(_mediaLibrary.LoadMedia(_settings.Settings.VideoDirectory))
            .OrderByDescending(item => item.ModifiedAt)
            .Take(60);

        GalleryItems.Clear();
        foreach (var item in items)
            GalleryItems.Add(item);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync()
    {
        if (SelectedGalleryItem is null)
            return Task.CompletedTask;

        if (TrashService.Trash(SelectedGalleryItem.Path))
            GalleryItems.Remove(SelectedGalleryItem);

        return Task.CompletedTask;
    }

    private bool CanDeleteSelected() => SelectedGalleryItem is not null;

    [RelayCommand]
    private void OpenMediaFolder()
    {
        var directory = _settings.Settings.PhotoDirectory;
        if (Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo("xdg-open", directory) { UseShellExecute = true });
        }
    }
}