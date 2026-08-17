using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace camonlinux.Services;

/// <summary>
/// Shows desktop notifications via <c>notify-send</c> (part of libnotify, present on
/// most Linux desktops including KDE Plasma and GNOME).
/// </summary>
public static class NotificationService
{
    public static void Notify(string summary, string? body = null, string? icon = null)
    {
        try
        {
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(icon))
            {
                args.Add("-i");
                args.Add(icon);
            }
            args.Add("--");
            args.Add(summary);
            if (!string.IsNullOrWhiteSpace(body))
                args.Add(body);

            using var process = Process.Start(new ProcessStartInfo("notify-send", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch
        {
            // Notifications are best-effort; never crash the app if notify-send is missing.
        }
    }
}
