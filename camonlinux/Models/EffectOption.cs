using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace camonlinux.Models;

/// <summary>
/// A selectable video effect. <see cref="Filter"/> is the GStreamer filter chain
/// inserted into the preview/recording pipeline (empty string = no effect).
/// <see cref="Thumbnail"/> is a live preview of the effect rendered from a camera
/// frame.
/// </summary>
public partial class EffectOption : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Filter { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public EffectOption(string id, string name, string filter)
    {
        Id = id;
        Name = name;
        Filter = filter;
    }

    public override string ToString() => Name;
}
