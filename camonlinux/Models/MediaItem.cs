using System;

namespace camonlinux.Models;

/// <summary>A photo or video captured by camonlinux.</summary>
public sealed record MediaItem(
    string Path,
    string Name,
    string Extension,
    bool IsVideo,
    DateTime ModifiedAt,
    long Size);
