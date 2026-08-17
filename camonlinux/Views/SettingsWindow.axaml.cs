using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace camonlinux.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() : this("", "")
    {
    }

    public SettingsWindow(string photoDirectory, string videoDirectory)
    {
        InitializeComponent();
        PhotoDirBox.Text = photoDirectory;
        VideoDirBox.Text = videoDirectory;
    }

    public string PhotoDirectory => PhotoDirBox.Text ?? "";
    public string VideoDirectory => VideoDirBox.Text ?? "";

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
