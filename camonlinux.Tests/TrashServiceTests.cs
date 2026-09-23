using System;
using System.IO;
using camonlinux.Services;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// <see cref="TrashService"/> implements the freedesktop trash specification by hand
/// (no library), including the on-disk <c>.trashinfo</c> format. These tests pin the
/// layout and the name-collision handling down.
/// </summary>
public sealed class TrashServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string? _originalXdgDataHome;
    private readonly string _xdgDataHome;

    public TrashServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "camonlinux_trash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _xdgDataHome = Path.Combine(_root, "data");
        _originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _xdgDataHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _originalXdgDataHome);
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private string TrashFiles => Path.Combine(_xdgDataHome, "Trash", "files");
    private string TrashInfo => Path.Combine(_xdgDataHome, "Trash", "info");

    [Fact]
    public void Trash_MovesTheFileAndWritesATrashInfoSidecar()
    {
        var source = CreateFile("photo.jpg", "jpeg-bytes");

        Assert.True(TrashService.Trash(source));

        Assert.False(File.Exists(source));
        var trashed = Path.Combine(TrashFiles, "photo.jpg");
        Assert.True(File.Exists(trashed));
        Assert.Equal("jpeg-bytes", File.ReadAllText(trashed));

        var sidecar = Path.Combine(TrashInfo, "photo.jpg.trashinfo");
        Assert.True(File.Exists(sidecar));
        var text = File.ReadAllText(sidecar);
        Assert.StartsWith("[Trash Info]", text);
        Assert.Contains($"Path={new Uri(source).AbsoluteUri}", text);
        Assert.Contains("DeletionDate=", text);
    }

    [Fact]
    public void Trash_RecordsAnEscapedAbsoluteUriInTheTrashInfo()
    {
        var source = CreateFile("a b & c.jpg", "x");

        Assert.True(TrashService.Trash(source));

        var text = File.ReadAllText(Path.Combine(TrashInfo, "a b & c.jpg.trashinfo"));
        var line = Array.Find(text.Split('\n'), l => l.StartsWith("Path=", StringComparison.Ordinal));
        Assert.NotNull(line);

        var recorded = line!["Path=".Length..].Trim();
        Assert.StartsWith("file://", recorded);
        // Spaces must be percent-encoded, and the value must round-trip to the original path.
        Assert.DoesNotContain(" ", recorded);
        Assert.Equal(source, new Uri(recorded).LocalPath);
    }

    [Fact]
    public void Trash_RenamesOnNameCollision()
    {
        var first = CreateFile("photo.jpg", "first", subDirectory: "one");
        var second = CreateFile("photo.jpg", "second", subDirectory: "two");

        Assert.True(TrashService.Trash(first));
        Assert.True(TrashService.Trash(second));

        Assert.Equal("first", File.ReadAllText(Path.Combine(TrashFiles, "photo.jpg")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(TrashFiles, "photo_1.jpg")));
        Assert.True(File.Exists(Path.Combine(TrashInfo, "photo_1.jpg.trashinfo")));
    }

    [Fact]
    public void Trash_MovesDirectoriesToo()
    {
        var directory = Path.Combine(_root, "captures");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.jpg"), "a");

        Assert.True(TrashService.Trash(directory));

        Assert.False(Directory.Exists(directory));
        Assert.True(File.Exists(Path.Combine(TrashFiles, "captures", "a.jpg")));
    }

    [Fact]
    public void Trash_ReturnsFalseForAMissingPath()
    {
        Assert.False(TrashService.Trash(Path.Combine(_root, "does-not-exist.jpg")));
    }

    private string CreateFile(string name, string content, string? subDirectory = null)
    {
        var directory = subDirectory is null ? _root : Path.Combine(_root, subDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
