namespace camonlinux.Models;

/// <summary>User settings, persisted as JSON in ~/.config/camonlinux/settings.json.</summary>
public sealed class AppSettings
{
    public string PhotoDirectory { get; set; } = "";
    public string VideoDirectory { get; set; } = "";
    public bool Mirrored { get; set; } = true;
    public bool MicEnabled { get; set; } = true;
    public string Resolution { get; set; } = "";
    public string RecordQuality { get; set; } = "medium";
    public long MaxFileSizeMB { get; set; }
    public string? LastDeviceId { get; set; }
}
