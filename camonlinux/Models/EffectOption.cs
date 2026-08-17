using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace camonlinux.Models;

/// <summary>
/// A selectable video effect. <see cref="Filter"/> is the GStreamer filter chain
/// inserted into the preview/recording pipeline (empty string = no effect). For
/// effects with an adjustable parameter, the filter contains a <c>{I}</c> placeholder
/// that is substituted with the current intensity, and the range/defaults are
/// exposed here so the UI can show a slider.
/// </summary>
public partial class EffectOption : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Filter { get; }

    /// <summary>GStreamer property adjusted by the intensity slider (null = no slider).</summary>
    public string? IntensityProp { get; }

    public double IntensityMin { get; }
    public double IntensityMax { get; }
    public double IntensityDefault { get; }

    public bool HasIntensity => IntensityProp is not null;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>Pinned to the top of the effects gallery.</summary>
    [ObservableProperty]
    private bool _isFavorite;

    public EffectOption(
        string id,
        string name,
        string filter,
        string? intensityProp = null,
        double intensityMin = 0,
        double intensityMax = 1,
        double intensityDefault = 1)
    {
        Id = id;
        Name = name;
        Filter = filter;
        IntensityProp = intensityProp;
        IntensityMin = intensityMin;
        IntensityMax = intensityMax;
        IntensityDefault = intensityDefault;
    }

    public override string ToString() => Name;
}
