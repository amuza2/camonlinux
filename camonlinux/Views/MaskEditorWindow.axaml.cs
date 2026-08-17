using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using camonlinux.ViewModels;

namespace camonlinux.Views;

/// <summary>Mask Editor window — hosts the shared MaskEditorViewModel (tabs for each mask type).</summary>
public partial class MaskEditorWindow : Window
{
    public MaskEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Loads an SVG file's text into the SVG mask settings.</summary>
    private async void OnLoadSvgFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MaskEditorViewModel vm)
            return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an SVG file",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SVG") { Patterns = new[] { "*.svg" } },
                    FilePickerFileTypes.All
                }
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                vm.Svg.SvgText = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            // Best-effort; keep the editor usable if the picker/IO fails.
            Console.WriteLine($"[svg] load failed: {ex.Message}");
        }
    }
}
