using System;
using System.Text;

namespace camonlinux.Capture;

/// <summary>
/// Helpers for embedding values that come from the user's environment — file paths,
/// PulseAudio source names, device nodes — into a GStreamer launch description.
///
/// <c>gst_parse_launch</c> treats a double quote as a string terminator, so a single
/// <c>"</c> in a folder or microphone name silently truncates the pipeline string and
/// breaks parsing (at best the feature fails, at worst a crafted value injects extra
/// pipeline elements). Values are therefore always wrapped in double quotes with the
/// parser's escape characters escaped.
/// </summary>
internal static class GstEscape
{
    /// <summary>
    /// Wraps <paramref name="value"/> in double quotes and escapes <c>\</c> and <c>"</c>
    /// so the result parses back to exactly <paramref name="value"/>.
    /// </summary>
    public static string Quote(string? value)
    {
        var source = value ?? string.Empty;
        var sb = new StringBuilder(source.Length + 2);
        sb.Append('"');
        foreach (var c in source)
        {
            if (c is '\\' or '"')
                sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Formats a quoted element property, e.g.
    /// <c>Property("location", "/a &quot;b&quot;/x.mkv")</c> → <c>location="/a \"b\"/x.mkv"</c>.
    /// </summary>
    public static string Property(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"{name}={Quote(value)}";
    }
}
