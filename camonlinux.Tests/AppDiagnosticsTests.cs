using camonlinux.Models;
using camonlinux.Services;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// The About window's diagnostics block. Its value is in bug reports, so what matters is
/// that it always names the version and reports on the optional pieces rather than
/// silently omitting a line.
/// </summary>
public class AppDiagnosticsTests
{
    [Fact]
    public void Build_ReportsTheVersionAndEveryOptionalPiece()
    {
        var capture = new FakeCaptureService
        {
            Devices = new[] { new CameraDevice("/dev/video0", "Test Camera", "/dev/video0") },
        };

        var text = AppDiagnostics.Build(capture, availableEffects: 42, settingsPath: "/tmp/settings.json");

        Assert.Contains($"{AppInfo.AppName} {AppInfo.Version}", text);
        Assert.Contains("build     ", text);
        Assert.Contains("runtime   .NET", text);
        Assert.Contains("os        ", text);
        Assert.Contains("gstreamer ", text);
        Assert.Contains("cameras   1", text);
        Assert.Contains("effects   42", text);
        Assert.Contains("v4l2loop  ", text);
        Assert.Contains("settings  /tmp/settings.json", text);
    }

    [Fact]
    public void Build_StillReportsEmptyCounts()
    {
        // A report from a machine with no camera and no extra effects is exactly when the
        // diagnostics matter most, so the counts must appear rather than being skipped.
        var text = AppDiagnostics.Build(new FakeCaptureService(), availableEffects: 0, settingsPath: "/x");

        Assert.Contains("cameras   0", text);
        Assert.Contains("effects   0", text);
    }

    [Fact]
    public void Build_ReportsGStreamerAsAvailableWhenTheBackendIsUp()
    {
        // FakeCaptureService reports every element as available, so this pins the line's
        // wording; the real service returns false when the GStreamer libs are missing.
        var text = AppDiagnostics.Build(new FakeCaptureService(), 0, "/x");

        Assert.Contains("gstreamer available", text);
    }
}
