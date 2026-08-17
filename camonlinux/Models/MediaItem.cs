using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace camonlinux.Models;

/// <summary>A photo or video captured by camonlinux.</summary>
public partial class MediaItem : ObservableObject
{
    public string Path { get; }
    public string Name { get; }
    public string Extension { get; }
    public bool IsVideo { get; }
    public DateTime ModifiedAt { get; }
    public long Size { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public MediaItem(string path, string name, string extension, bool isVideo, DateTime modifiedAt, long size)
    {
        Path = path;
        Name = name;
        Extension = extension;
        IsVideo = isVideo;
        ModifiedAt = modifiedAt;
        Size = size;
    }
}
