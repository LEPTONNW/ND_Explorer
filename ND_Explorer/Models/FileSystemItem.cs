namespace ND_Explorer.Models;

public sealed class FileSystemItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsHidden { get; init; }
    public long? Size { get; init; }
    public DateTime LastModified { get; init; }
    public required string TypeName { get; init; }

    public string Glyph => IsDirectory ? "\uE8B7" : "\uE8A5";

    public bool IsImage => !IsDirectory && MediaFileSupport.IsImage(FullPath);

    public bool IsVideo => !IsDirectory && MediaFileSupport.IsVideo(FullPath);

    public bool HasInternalViewer => IsImage || IsVideo;

    public string SizeText => IsDirectory || Size is null ? string.Empty : FormatSize(Size.Value);

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{bytes:N0} {suffixes[suffixIndex]}"
            : $"{value:N1} {suffixes[suffixIndex]}";
    }
}
