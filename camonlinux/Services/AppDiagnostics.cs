using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using camonlinux.Capture;

namespace camonlinux.Services;

/// <summary>
/// Builds the plain-text diagnostics block shown in the About window.
///
/// Its purpose is bug reports: this app depends on a lot of optional pieces (the
/// GStreamer plugin sets, frei0r effects, v4l2loopback for the virtual camera, a
/// PulseAudio/PipeWire server), and "which of those did you actually have?" is the
/// first question any issue needs answered.
/// </summary>
public static class AppDiagnostics
{
    public static string Build(ICaptureService capture, int availableEffects, string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var sb = new StringBuilder();
        // CultureInfo.InvariantCulture is passed explicitly (CA1305): diagnostics get
        // pasted into bug reports, so they must read identically on every machine rather
        // than picking up the reporter's locale.
        var culture = CultureInfo.InvariantCulture;
        sb.AppendLine(culture, $"{AppInfo.AppName} {AppInfo.Version}");
        sb.AppendLine(culture, $"build     {AppInfo.InformationalVersion}");
        sb.AppendLine(culture, $"runtime   .NET {Environment.Version}");
        sb.AppendLine(culture, $"os        {RuntimeInformation.OSDescription}");
        sb.AppendLine(culture, $"gstreamer {Yes(capture.IsElementAvailable("v4l2src"))}");
        sb.AppendLine(culture, $"cameras   {capture.Devices.Count}");
        sb.AppendLine(culture, $"effects   {availableEffects}");
        sb.AppendLine(culture, $"v4l2loop  {Yes(VirtualCameraService.FindLoopbackDevice() is not null)}");
        sb.Append(culture, $"settings  {settingsPath}");
        return sb.ToString();
    }

    private static string Yes(bool value) => value ? "available" : "not available";
}
