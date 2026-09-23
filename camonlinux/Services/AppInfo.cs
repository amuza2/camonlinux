using System;
using System.Reflection;

namespace camonlinux.Services;

/// <summary>
/// Static metadata about the application, used by the About window.
///
/// The version is read from the assembly (which gets it from <c>&lt;Version&gt;</c> in the
/// .csproj) rather than being repeated in the UI, so there is one place to bump it. The
/// repository URL doubles as the source for the "report an issue" and "releases" links.
/// </summary>
public static class AppInfo
{
    /// <summary>Public repository. Keep identical to &lt;RepositoryUrl&gt; in the .csproj.</summary>
    public const string RepositoryUrl = "https://github.com/amuza2/camonlinux";

    public const string IssuesUrl = RepositoryUrl + "/issues";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string LicenseName = "MIT";
    public const string LicenseUrl = "https://opensource.org/license/mit";
    public const string AppName = "camonlinux";

    /// <summary>Short description shown under the title in About.</summary>
    public const string Tagline = "A simple, modern webcam app for Linux — take photos and record videos with your webcam.";

    /// <summary>
    /// The app version as "major.minor.build" (e.g. "0.1.0"), taken from the assembly.
    /// <see cref="AssemblyVersion"/> is used rather than the informational version, which
    /// the SDK may suffix with "+&lt;commit&gt;" — that detail belongs in the diagnostics.
    /// </summary>
    public static string Version => Format(Assembly.GetName().Version);

    /// <summary>The SDK's informational version, including the source revision when known.</summary>
    public static string InformationalVersion
    {
        get
        {
            var attribute = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var value = attribute?.InformationalVersion;
            return string.IsNullOrWhiteSpace(value) ? Version : value;
        }
    }

    private static Assembly Assembly => typeof(AppInfo).Assembly;

    private static string Format(Version? version)
        => version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
}
