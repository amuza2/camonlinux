using System.Diagnostics;

namespace camonlinux.Services;

/// <summary>
/// Opens a file, folder or URL with the user's default handler (xdg-open).
///
/// Named "SystemLauncher" rather than "Launcher" on purpose: <see cref="Avalonia.Controls.TopLevel"/>
/// has a <c>Launcher</c> property (of type <c>Avalonia.Platform.Storage.ILauncher</c>), so
/// inside a window the bare name <c>Launcher</c> resolves to that property instead.
/// </summary>
public static class SystemLauncher
{
    public static bool Open(string target)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
            psi.ArgumentList.Add(target);
            Process.Start(psi);
            return true;
        }
        catch
        {
            // xdg-open missing or no handler registered — best effort.
            return false;
        }
    }
}
