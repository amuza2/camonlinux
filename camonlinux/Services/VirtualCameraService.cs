using System;
using System.IO;
using Avalonia.Threading;
using GObject;
using Gst;
using GstApp;
using GstBase;

namespace camonlinux.Services;

/// <summary>
/// Exposes the app's live preview (including the mask / colour-adjustment) as a
/// virtual webcam via <c>v4l2loopback</c> + a GStreamer <c>appsrc → v4l2sink</c>
/// pipeline. Frames are pushed from the streaming thread AFTER the mask pipeline
/// has run, so other apps (Zoom, OBS, browsers, …) can use the processed feed.
///
/// The kernel module must be loaded first, e.g.:
/// <c>sudo modprobe v4l2loopback video_nr=10 card_label="camonlinux Virtual Camera" exclusive_caps=1</c>
/// </summary>
public sealed class VirtualCameraService : IDisposable
{
    private readonly object _lock = new();
    private Pipeline? _pipeline;
    private AppSrc? _appSrc;
    private string _device = "/dev/video0";
    private int _fps = 30;
    private int _width;
    private int _height;

    public bool IsRunning
    {
        get { lock (_lock) return _pipeline is not null; }
    }

    /// <summary>Human-readable description of the last failure (or null if all good).</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Finds the first /dev/videoN node backed by the v4l2loopback driver, or null
    /// if the module isn't loaded / no loopback device exists. v4l2loopback devices
    /// are virtual and usually have NO <c>device/driver</c> symlink, so they're also
    /// matched by card name (the label we request, or the driver's default
    /// "Dummy video device").
    /// </summary>
    public static string? FindLoopbackDevice()
    {
        try
        {
            var dir = "/sys/class/video4linux";
            if (!Directory.Exists(dir))
                return null;

            foreach (var node in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(node); // e.g. "video10"

                // 1) Some loopback variants expose a device/driver symlink.
                var driverLink = Path.Combine(node, "device", "driver");
                if (File.Exists(driverLink))
                {
                    var target = new FileInfo(driverLink).LinkTarget ?? string.Empty;
                    if (target.IndexOf("v4l2loopback", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "/dev/" + name;
                }

                // 2) v4l2loopback has no device/ node — match its card name file.
                var nameFile = Path.Combine(node, "name");
                if (File.Exists(nameFile))
                {
                    var cardName = File.ReadAllText(nameFile);
                    if (cardName.IndexOf("camonlinux", StringComparison.OrdinalIgnoreCase) >= 0
                        || cardName.IndexOf("v4l2loopback", StringComparison.OrdinalIgnoreCase) >= 0
                        || cardName.IndexOf("dummy video device", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "/dev/" + name;
                }
            }
        }
        catch
        {
            // best-effort detection
        }
        return null;
    }

    /// <summary>
    /// Builds (or rebuilds) the appsrc → v4l2sink pipeline and starts it.
    /// BGRx frames are pushed by <see cref="PushFrame"/>; videoconvert/videoscale
    /// turn them into a format v4l2loopback accepts (YUYV).
    /// </summary>
    public bool Start(string device, int width, int height, int fps)
    {
        lock (_lock)
        {
            Stop();
            try
            {
                _device = device;
                _fps = Math.Max(1, fps);
                _width = Math.Max(2, width);
                _height = Math.Max(2, height);
                var framerate = _fps;
                var description =
                    $"appsrc name=src format=time is-live=true do-timestamp=true " +
                    $"! video/x-raw,format=BGRx,width={_width},height={_height},framerate={framerate}/1 " +
                    $"! videoconvert ! videoscale " +
                    $"! video/x-raw,format=YUY2 " +
                    $"! v4l2sink device={_device} sync=false";
                _pipeline = (Pipeline)Gst.Functions.ParseLaunch(description);
                if (_pipeline is null)
                {
                    LastError = "Failed to parse the virtual-camera pipeline.";
                    return false;
                }
                var bin = (Gst.Bin)_pipeline;
                _appSrc = bin.GetByName("src") as AppSrc;
                if (_appSrc is null)
                {
                    LastError = "Failed to create the appsrc for the virtual camera.";
                    Stop();
                    return false;
                }
                // Cap buffered frames so a slow consumer can't stall the preview badly.
                try { _appSrc.SetProperty("max-buffers", new Value(2u)); }
                catch { /* optional tuning */ }
                _pipeline.SetState(Gst.State.Playing);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Stop();
                return false;
            }
        }
    }

    /// <summary>Push one processed frame (BGRA/BGRx, width * height * 4 bytes).</summary>
    public void PushFrame(byte[] data, int width, int height)
    {
        if (data is null || data.Length == 0)
            return;

        AppSrc? src;
        lock (_lock)
        {
            if (_pipeline is null)
                return;
            if (width != _width || height != _height)
            {
                // Source resolution changed — rebuild on the UI thread, drop this frame.
                var w = width;
                var h = height;
                Dispatcher.UIThread.Post(() => Restart(w, h));
                return;
            }
            src = _appSrc;
        }

        if (src is null)
            return;

        try
        {
            using var buffer = Gst.Buffer.NewMemdup(data);
            var ret = src.PushBuffer(buffer);
            if (ret != FlowReturn.Ok && ret != FlowReturn.Flushing)
                LastError = $"Push failed: {ret}";
        }
        catch
        {
            // Pipeline being torn down on another thread — drop the frame.
        }
    }

    private void Restart(int width, int height)
    {
        lock (_lock)
        {
            if (_pipeline is null)
                return;
            var device = _device;
            var fps = _fps;
            if (_appSrc is not null)
            {
                try { _appSrc.EndOfStream(); } catch { }
                _appSrc = null;
            }
            try { _pipeline.SetState(Gst.State.Null); } catch { }
            try { _pipeline.Dispose(); } catch { }
            _pipeline = null;
            Start(device, width, height, fps);
        }
    }

    /// <summary>Stops the pipeline and releases the loopback device.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_appSrc is not null)
            {
                try { _appSrc.EndOfStream(); } catch { }
                _appSrc = null;
            }
            if (_pipeline is not null)
            {
                try { _pipeline.SetState(Gst.State.Null); } catch { }
                try { _pipeline.Dispose(); } catch { }
                _pipeline = null;
            }
        }
    }

    public void Dispose() => Stop();
}
