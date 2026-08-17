using System;
using System.Diagnostics;

namespace camonlinux.Services;

/// <summary>
/// Plays a brief camera-shutter sound when a photo is taken, using the system's
/// sound theme via libcanberra's <c>camera-shutter</c> event (Kamoso-style).
/// Best-effort — silently does nothing if canberra or the event isn't available.
/// </summary>
public static class ShutterSound
{
    public static void Play()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "canberra-gtk-play",
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("camera-shutter");
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add("camonlinux");
            // Fire-and-forget: the short sound plays on its own, never blocking
            // the capture.
            Process.Start(psi);
        }
        catch
        {
            // No sound is fine (e.g. canberra not installed, no sound server).
        }
    }
}
