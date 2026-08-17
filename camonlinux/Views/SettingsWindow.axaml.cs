using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace camonlinux.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this("", "", "2.5 s", "Unlimited")
    {
    }

    public SettingsWindow(string photoDirectory, string videoDirectory, string burstInterval, string burstCount)
    {
        InitializeComponent();
        PhotoDirBox.Text = photoDirectory;
        VideoDirBox.Text = videoDirectory;
        BurstIntervalBox.ItemsSource = new[] { "1 s", "2.5 s", "5 s" };
        BurstCountBox.ItemsSource = new[] { "Unlimited", "5", "10", "20" };
        BurstIntervalBox.SelectedItem = burstInterval;
        BurstCountBox.SelectedItem = burstCount;
    }

    public string PhotoDirectory => PhotoDirBox.Text ?? "";
    public string VideoDirectory => VideoDirBox.Text ?? "";
    public string BurstInterval => BurstIntervalBox.SelectedItem as string ?? "2.5 s";
    public string BurstCount => BurstCountBox.SelectedItem as string ?? "Unlimited";

    private async void OnBrowsePhoto(object? sender, RoutedEventArgs e) => await BrowseAsync(PhotoDirBox);

    private async void OnBrowseVideo(object? sender, RoutedEventArgs e) => await BrowseAsync(VideoDirBox);

    private async Task BrowseAsync(TextBox box)
    {
        var dirs = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose a folder" });
        if (dirs.Count > 0)
            box.Text = dirs[0].Path.LocalPath;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
