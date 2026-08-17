namespace camonlinux.Models;

/// <summary>
/// A selectable video effect. <see cref="Filter"/> is the GStreamer filter chain
/// inserted into the preview/recording pipeline (empty string = no effect).
/// </summary>
public sealed record EffectOption(string Id, string Name, string Filter)
{
    public override string ToString() => Name;
}
