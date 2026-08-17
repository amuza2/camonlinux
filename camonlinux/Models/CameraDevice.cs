namespace camonlinux.Models;

/// <summary>Represents an available camera device.</summary>
public sealed record CameraDevice(string Id, string Name, string Path)
{
    public override string ToString() => Name;
}
