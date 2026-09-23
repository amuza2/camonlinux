using camonlinux.Services;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// <see cref="AppInfo"/> reads the version from the assembly rather than repeating it in
/// the UI. Previously the app carried no version at all and only <c>packaging/</c> knew
/// it, so these guard the "one source of truth" wiring.
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void Version_IsASemanticVersionTakenFromTheAssembly()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void InformationalVersion_IncludesTheDisplayVersion()
    {
        // The SDK's informational version is "0.1.0" or "0.1.0+<commit>"; the version
        // shown in the About window must always be a prefix of it.
        Assert.StartsWith(AppInfo.Version, AppInfo.InformationalVersion);
    }

    [Fact]
    public void Links_PointAtTheProjectRepository()
    {
        Assert.Equal("https://github.com/amuza2/camonlinux", AppInfo.RepositoryUrl);
        Assert.StartsWith(AppInfo.RepositoryUrl, AppInfo.IssuesUrl);
        Assert.StartsWith(AppInfo.RepositoryUrl, AppInfo.ReleasesUrl);
    }

    [Fact]
    public void Licence_IsMit()
    {
        Assert.Equal("MIT", AppInfo.LicenseName);
        Assert.StartsWith("https://", AppInfo.LicenseUrl);
    }
}
