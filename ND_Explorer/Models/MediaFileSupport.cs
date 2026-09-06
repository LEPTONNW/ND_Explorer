using System.IO;

namespace ND_Explorer.Models;

public static class MediaFileSupport
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v",
        ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".flv", ".3gp",
        ".3g2", ".ogv", ".vob", ".asf", ".f4v"
    };

    public static bool IsImage(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public static bool IsVideo(string path)
        => VideoExtensions.Contains(Path.GetExtension(path));
}
