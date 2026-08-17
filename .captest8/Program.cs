using Gst;
using GObject;
using GstBase;

GstBase.Module.Initialize();
Gst.Module.Initialize();
var gstArgs = Array.Empty<string>();
Gst.Functions.Init(ref gstArgs);

foreach (var dev in new[] { "/dev/video0", "/dev/video2" })
{
    Console.WriteLine($"=== {dev} ===");
    var src = Gst.ElementFactory.Make("v4l2src", null);
    if (src is null) { Console.WriteLine("  no v4l2src"); continue; }
    try
    {
        src.SetProperty("device", new GObject.Value(dev));
        src.SetState(Gst.State.Ready);
        var pad = src.GetStaticPad("src");
        using var caps = pad?.QueryCaps(null);
        if (caps is null) { Console.WriteLine("  no caps"); continue; }
        for (uint i = 0; i < caps.GetSize(); i++)
        {
            using var s = caps.GetStructure(i);
            if (s is null) continue;
            var name = s.GetName();
            s.GetInt("width", out int w);
            s.GetInt("height", out int h);
            s.GetFraction("framerate", out int frn, out int frd);
            Console.WriteLine($"  {name} {w}x{h} @ {frn}/{frd}");
        }
    }
    finally
    {
        src.SetState(Gst.State.Null);
        src.Dispose();
    }
}
