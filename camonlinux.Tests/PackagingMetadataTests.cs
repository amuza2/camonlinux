using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// Guards the packaging metadata, which no compiler checks.
/// </summary>
/// <remarks>
/// Everything asserted here has already gone wrong once: appimagetool refused to
/// build the AppImage because the AppStream id was not reverse-DNS and because the
/// desktop file named two main categories, and both the icon and the metainfo file
/// have been renamed behind the scripts that install them. None of that fails a
/// build or a unit test — it fails silently, for users, in the shape of a missing
/// menu entry or a rejected package. These tests read the real files on disk so the
/// packaging cannot drift from the app it ships.
/// </remarks>
public sealed class PackagingMetadataTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PackagingDir = Path.Combine(RepoRoot, "packaging");
    private static readonly string DesktopPath = Path.Combine(PackagingDir, "camonlinux.desktop");
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "camonlinux", "camonlinux.csproj");

    /// <summary>The executable name, which is also the csproj name.</summary>
    private static string BinaryName => Path.GetFileNameWithoutExtension(ProjectPath);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "camonlinux.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"could not find camonlinux.sln above {AppContext.BaseDirectory}");
    }

    // ------------------------------------------------------------------ //
    // .desktop
    // ------------------------------------------------------------------ //

    /// <summary>The keys of the [Desktop Entry] group, in file order.</summary>
    private static List<(string Key, string Value)> DesktopEntries()
    {
        var entries = new List<(string, string)>();
        foreach (var raw in File.ReadAllLines(DesktopPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
                entries.Add((line[..separator], line[(separator + 1)..]));
        }

        return entries;
    }

    private static string DesktopValue(string key) =>
        DesktopEntries().Single(e => e.Key == key).Value;

    private static string[] DesktopCategories() =>
        DesktopValue("Categories").Split(';', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void DesktopFile_HasNoDuplicateKeys()
    {
        // A duplicate key is invalid, but the file still parses "fine" in many
        // tools — appimagetool caught it only as a hard error at packaging time.
        var duplicates = DesktopEntries()
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void DesktopFile_DeclaresExactlyOneMainCategory()
    {
        // Per the freedesktop menu spec a desktop entry has one main category;
        // AudioVideoEditing is a sub-category of AudioVideo, Graphics is another
        // main category entirely. Two of them means the app is listed twice, and
        // appimagetool's AppStream validation fails the build.
        string[] mainCategories =
        [
            "AudioVideo", "Audio", "Video", "Development", "Education", "Game",
            "Graphics", "Network", "Office", "Science", "Settings", "System", "Utility",
        ];

        var declared = DesktopCategories()
            .Where(c => mainCategories.Contains(c, StringComparer.Ordinal))
            .ToList();

        Assert.Single(declared);
    }

    [Fact]
    public void DesktopFile_ExecIsTheBinaryWeActuallyShip()
    {
        // A path here would break both the AppImage (which needs a bare name to
        // resolve inside the AppDir) and a $PATH install.
        var exec = DesktopValue("Exec");

        Assert.Equal(BinaryName, exec);
        Assert.DoesNotContain("/", exec, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopFile_IconMatchesTheInstalledIconNames()
    {
        var icon = DesktopValue("Icon");

        Assert.Equal(BinaryName, icon);
        // installers write $Icon.png / $Icon.svg; a mismatch leaves the launcher blank.
        Assert.True(File.Exists(Path.Combine(PackagingDir, "icons", icon + "-256.png")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "camonlinux", "Assets", "webcam.svg")));
    }

    // ------------------------------------------------------------------ //
    // AppStream metainfo
    // ------------------------------------------------------------------ //

    private static string MetainfoPath() =>
        Assert.Single(Directory.GetFiles(PackagingDir, "*.metainfo.xml"));

    private static XDocument Metainfo() => XDocument.Load(MetainfoPath());

    private static string MetainfoValue(string element) =>
        Metainfo().Root?.Element(element)?.Value
        ?? throw new InvalidOperationException($"<{element}> missing from {MetainfoPath()}");

    [Fact]
    public void Metainfo_IdIsReverseDns()
    {
        // appstreamcli rejects the legacy bare "name.desktop" form with
        // "cid-desktopapp-is-not-rdns", and appimagetool then refuses to build.
        var id = MetainfoValue("id");

        Assert.DoesNotContain(".desktop", id, StringComparison.Ordinal);
        Assert.True(
            id.Count(c => c == '.') >= 2,
            $"component id '{id}' must be reverse-DNS, e.g. io.github.amuza2.camonlinux");
    }

    [Fact]
    public void Metainfo_FilenameMatchesTheComponentId()
    {
        // Software centres pair the two; a mismatch means the app is not found.
        // The extension is .metainfo.xml, the current spelling of .appdata.xml.
        Assert.Equal(MetainfoValue("id") + ".metainfo.xml", Path.GetFileName(MetainfoPath()));
    }

    [Fact]
    public void Metainfo_LaunchablePointsAtTheDesktopFileWeShip()
    {
        var launchable = Metainfo().Root?
            .Element("launchable")?
            .Attribute("type")?
            .Value;

        Assert.Equal("desktop-id", launchable);
        Assert.Equal(Path.GetFileName(DesktopPath), MetainfoValue("launchable"));
    }

    [Fact]
    public void Metainfo_ProvidedBinaryMatchesTheDesktopExec()
    {
        var binary = Metainfo().Descendants("binary").Single().Value;

        Assert.Equal(DesktopValue("Exec"), binary);
    }

    [Fact]
    public void Metainfo_ReleaseVersionMatchesTheCsproj()
    {
        // The csproj asks for these to be kept in step; nothing enforced it, so a
        // release could advertise a version that is not the one being built.
        var release = Metainfo().Descendants("release").First();

        Assert.Equal(CsprojVersion(), release.Attribute("version")?.Value);
    }

    [Fact]
    public void Metainfo_ReleaseDateIsParseable()
    {
        // AppStream requires an ISO 8601 date; a stray locale format silently
        // drops the release from the app's history.
        var date = Metainfo().Descendants("release").First().Attribute("date")?.Value;

        Assert.True(
            DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _),
            $"release date '{date}' is not an ISO 8601 date");
    }

    private static string CsprojVersion()
    {
        var element = XDocument.Load(ProjectPath)
            .Descendants("Version")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("<Version> missing from the csproj");

        return element.Value;
    }

    // ------------------------------------------------------------------ //
    // Release icons
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    public void ReleaseIcons_ExistAtTheSizeTheyClaim(int size)
    {
        var path = Path.Combine(PackagingDir, "icons", $"camonlinux-{size}.png");

        Assert.True(File.Exists(path), $"{path} is missing (the release scripts install every size)");
        Assert.Equal((size, size), ReadPngSize(path));
    }

    [Fact]
    public void ReleaseIcons_AreNotEmbeddedInTheApplication()
    {
        // Assets/** is embedded as AvaloniaResource, so the release ladder lives in
        // packaging/icons instead; putting a 512px icon in Assets would ship it
        // inside the binary for no reason.
        foreach (var size in new[] { 256, 512 })
        {
            Assert.False(
                File.Exists(Path.Combine(RepoRoot, "camonlinux", "Assets", $"camonlinux-{size}.png")),
                $"camonlinux-{size}.png belongs in packaging/icons, not Assets (Assets is embedded)");
        }
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        // The IHDR chunk sits immediately after the 8-byte signature and starts
        // with the width and height as big-endian 32-bit integers. Reading those
        // directly avoids taking a PNG/Skia dependency just to check a header.
        var header = new byte[24];
        using (var stream = File.OpenRead(path))
            stream.ReadExactly(header);

        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, header[..4]);

        return (
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
    }

    // ------------------------------------------------------------------ //
    // Build scripts
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Every path the build scripts and the PKGBUILD point at must exist.
    /// </summary>
    /// <remarks>
    /// Renaming a packaging file (as the metainfo file was) means updating every
    /// reference to it: the two build scripts, the installer and the PKGBUILD.
    /// Nothing fails loudly if one is missed — the install simply copies nothing,
    /// or appimagetool silently ships an AppImage with no AppStream metadata.
    /// </remarks>
    [Fact]
    public void PackagingReferences_PointAtFilesThatExist()
    {
        var missing = new List<string>();

        foreach (var script in Directory.GetFiles(Path.Combine(RepoRoot, "scripts"), "*.sh")
                     .Append(Path.Combine(PackagingDir, "PKGBUILD")))
        {
            var name = Path.GetFileName(script);

            foreach (var line in File.ReadLines(script))
            {
                // Skip comments: they discuss these paths in prose.
                if (line.TrimStart().StartsWith('#'))
                    continue;

                foreach (var reference in ReferencesAfter(line, "packaging/"))
                {
                    if (!File.Exists(Path.Combine(PackagingDir, reference)))
                        missing.Add($"{name} -> packaging/{reference}");
                }

                foreach (var reference in ReferencesAfter(line, "$REPO_ROOT/"))
                {
                    if (!File.Exists(Path.Combine(RepoRoot, reference)))
                        missing.Add($"{name} -> {reference}");
                }
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Yields the quoted token following each occurrence of <paramref name="marker"/>.
    /// </summary>
    /// <remarks>
    /// Paths assembled at runtime (they contain a <c>$variable</c> or a glob) are
    /// skipped: they cannot be checked without knowing the value, and they are the
    /// same files the literal references already cover.
    /// </remarks>
    private static IEnumerable<string> ReferencesAfter(string line, string marker)
    {
        var index = 0;
        while ((index = line.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + marker.Length;
            var end = start;
            while (end < line.Length && line[end] is not ('"' or ' ' or '\''))
                end++;

            var reference = line[start..end];
            index = end;

            if (reference.Length == 0 || reference.Contains('$') || reference.Contains('*'))
                continue;

            // A bare-word path is part of a longer reference (packaging/icons/x.png
            // is found again when the scan reaches "icons"); only check whole names.
            if (!reference.Contains('/') && marker == "$REPO_ROOT/")
                continue;

            yield return reference;
        }
    }

    [Fact]
    public void AppRun_ExecutesTheBinaryThePackagingInstalls()
    {
        var appRun = File.ReadAllText(Path.Combine(PackagingDir, "AppRun"));

        // $APPDIR is set by the AppImage runtime; the exec line is the one thing
        // that has to be right or the AppImage starts and does nothing.
        Assert.Contains($"exec \"$APPDIR/usr/bin/{BinaryName}\"", appRun, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRun_IsAPosixShellScript()
    {
        var firstLine = File.ReadLines(Path.Combine(PackagingDir, "AppRun")).First();

        // The AppImage runtime runs AppRun with /bin/sh, which is dash on Debian
        // and Ubuntu. A bash shebang would work here and fail for those users.
        Assert.Equal("#!/bin/sh", firstLine);
    }
}
