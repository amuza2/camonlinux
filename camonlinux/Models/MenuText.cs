using System;

namespace camonlinux.Models;

/// <summary>Helpers for building menu labels.</summary>
public static class MenuText
{
    /// <summary>
    /// Escapes a value for use in a menu <c>Header</c>.
    /// </summary>
    /// <remarks>
    /// Menu headers are rendered as access text, so a lone underscore is consumed and the
    /// next character is underlined instead: a camera called <c>USB_CAM</c> — and device
    /// names really do contain underscores — shows up as <c>USBCAM</c>. Doubling the
    /// underscore makes it render literally.
    /// </remarks>
    public static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("_", "__", StringComparison.Ordinal);
}
