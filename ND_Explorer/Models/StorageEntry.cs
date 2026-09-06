using System.IO;

namespace ND_Explorer.Models;

public sealed record StorageEntry(
    string Name,
    string FullPath,
    long Size,
    bool IsDirectory,
    bool IsPartial,
    string Extension,
    IReadOnlyList<StorageEntry>? Children = null,
    bool IsExpanded = false,
    bool IsLoading = false)
{
    public string SizeText => FormatSize(Size);

    public string TypeText => IsDirectory ? "폴더" : string.IsNullOrEmpty(Extension) ? "파일" : $"{Extension.TrimStart('.').ToUpperInvariant()} 파일";

    public static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = Math.Max(0, (double)bytes);
        var suffixIndex = 0;

        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0 ? $"{bytes:N0} B" : $"{value:N1} {suffixes[suffixIndex]}";
    }
}

public sealed record StorageScanResult(
    string Path,
    IReadOnlyList<StorageEntry> Entries,
    long TotalSize,
    long FilesScanned,
    long DirectoriesScanned,
    long SkippedItems,
    TimeSpan Elapsed);

public readonly record struct StorageScanProgress(
    long FilesScanned,
    long DirectoriesScanned,
    long BytesScanned,
    long SkippedItems,
    IReadOnlyList<StorageEntry>? EntryUpdates = null);
