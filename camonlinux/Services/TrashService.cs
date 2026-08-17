using System;
using System.IO;

namespace camonlinux.Services;

/// <summary>
/// Moves files to the user's trash following the
/// <see href="https://specifications.freedesktop.org/trash-spec/latest/">freedesktop trash spec</see>.
/// </summary>
public static class TrashService
{
    public static bool Trash(string path)
    {
        try
        {
            if (File.Exists(path))
                return MoveToTrash(path, isDirectory: false);
            if (Directory.Exists(path))
                return MoveToTrash(path, isDirectory: true);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool MoveToTrash(string path, bool isDirectory)
    {
        var trashRoot = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(trashRoot))
        {
            trashRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        }

        trashRoot = Path.Combine(trashRoot, "Trash");
        var filesDir = Path.Combine(trashRoot, "files");
        var infoDir = Path.Combine(trashRoot, "info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        var name = Path.GetFileName(path);
        var destination = Path.Combine(filesDir, name);
        var index = 1;
        while (File.Exists(destination) || Directory.Exists(destination))
        {
            var baseName = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            destination = Path.Combine(filesDir, $"{baseName}_{index++}{ext}");
        }

        if (isDirectory)
            Directory.Move(path, destination);
        else
            File.Move(path, destination);

        var uri = new Uri(path).AbsoluteUri;
        var date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var trashInfoPath = Path.Combine(infoDir, Path.GetFileName(destination) + ".trashinfo");
        File.WriteAllText(trashInfoPath, $"[Trash Info]\nPath={uri}\nDeletionDate={date}\n");
        return true;
    }
}
