using System.Collections.Generic;

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
    public int TimerSeconds { get; set; }
    public string Rotation { get; set; } = "auto";
    public double Zoom { get; set; } = 1.0;
    public string PhotoFormat { get; set; } = "jpeg";
    public bool ShowTimestamp { get; set; }
    public int Brightness { get; set; } = 128;
    public int Contrast { get; set; } = 128;
    public int Saturation { get; set; } = 128;
    public int Sharpness { get; set; } = 128;
    public int Gain { get; set; }
    public int BacklightCompensation { get; set; }
    public bool WhiteBalanceAuto { get; set; } = true;
    public int WhiteBalanceTemperature { get; set; } = 4000;
    public bool ExposureAuto { get; set; } = true;
    public int ExposureValue { get; set; } = 250;
    public bool FocusAuto { get; set; } = true;
    public int FocusValue { get; set; }
    public string AudioDevice { get; set; } = "";
    public List<string> FavoriteEffects { get; set; } = new();
    public Dictionary<string, double> EffectIntensities { get; set; } = new();
    public bool IsMaximized { get; set; } = true;
    public double WinWidth { get; set; } = 960;
    public double WinHeight { get; set; } = 600;
    public string? LastDeviceId { get; set; }
}
