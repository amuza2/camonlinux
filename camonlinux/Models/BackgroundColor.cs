namespace camonlinux.Models;

/// <summary>
/// The colour that fills the areas a mask cuts out (cf. OBS's virtual-camera
/// background). A single setting drives both the virtual-webcam feed and the areas
/// flattened into a captured photo, so a photo looks like what consumer apps receive.
/// </summary>
public static class BackgroundColor
{
    public static readonly (byte R, byte G, byte B) Black = (0, 0, 0);
    public static readonly (byte R, byte G, byte B) Green = (0, 255, 0);
    public static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    /// <summary>Maps a settings name ("Black", "Green", "White") to its colour; anything else is black.</summary>
    public static (byte R, byte G, byte B) Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "green" => Green,
        "white" => White,
        _ => Black,
    };
}
