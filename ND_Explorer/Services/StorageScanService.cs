using System.Diagnostics;
using System.IO;
using System.Security;
using ND_Explorer.Models;

namespace ND_Explorer.Services;

public sealed class StorageScanService : IStorageScanService
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0
    };

    public Task<StorageScanResult> ScanAsync(
        string path,
        IProgress<StorageScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(path, progress, cancellationToken), cancellationToken);

    private static StorageScanResult Scan(
        string path,
        IProgress<StorageScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new ScanState(progress);
        var entries = new List<StorageEntry>();

        foreach (var entryPath in EnumerateSafely(path, state))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetAttributes(entryPath, state, out var attributes))
            {
                continue;
            }

            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
            var skippedBefore = state.SkippedItems;
            var size = isDirectory ? 0 : TryGetFileSize(entryPath, state);
            if (isDirectory && isReparsePoint)
            {
                state.SkippedItems++;
            }

            var entry = new StorageEntry(
                GetDisplayName(entryPath),
                entryPath,
                size,
                isDirectory,
                state.SkippedItems > skippedBefore,
                isDirectory ? string.Empty : Path.GetExtension(entryPath));
            entries.Add(entry);
            state.Report(entry);
        }

        // Show direct children immediately. Directory sizes are filled in progressively below.
        state.Report(force: true);

        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            if (!entry.IsDirectory || entry.IsPartial)
            {
                continue;
            }

            var skippedBefore = state.SkippedItems;
            var size = CalculateDirectorySize(entry.FullPath, entry, skippedBefore, state, cancellationToken);
            var completed = entry with
            {
                Size = size,
                IsPartial = state.SkippedItems > skippedBefore
            };
            entries[index] = completed;
            state.Report(completed, force: true);
        }

        state.Report(force: true);
        stopwatch.Stop();
        var ordered = entries
            .OrderByDescending(entry => entry.Size)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        long totalSize = 0;
        foreach (var entry in ordered)
        {
            totalSize = totalSize >= long.MaxValue - entry.Size ? long.MaxValue : totalSize + entry.Size;
        }

        return new StorageScanResult(
            path,
            ordered,
            totalSize,
            state.FilesScanned,
            state.DirectoriesScanned,
            state.SkippedItems,
            stopwatch.Elapsed);
    }

    private static long CalculateDirectorySize(
        string rootPath,
        StorageEntry displayedEntry,
        long skippedBefore,
        ScanState state,
        CancellationToken cancellationToken)
    {
        long totalSize = 0;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            state.DirectoriesScanned++;

            foreach (var entryPath in EnumerateSafely(currentDirectory, state))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryGetAttributes(entryPath, state, out var attributes))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        state.SkippedItems++;
                    }
                    else
                    {
                        pendingDirectories.Push(entryPath);
                    }

                    continue;
                }

                var fileSize = TryGetFileSize(entryPath, state);
                totalSize = totalSize >= long.MaxValue - fileSize ? long.MaxValue : totalSize + fileSize;
                state.Report(displayedEntry with
                {
                    Size = totalSize,
                    IsPartial = state.SkippedItems > skippedBefore
                });
            }

            state.Report(displayedEntry with
            {
                Size = totalSize,
                IsPartial = state.SkippedItems > skippedBefore
            });
        }

        return totalSize;
    }

    private static IReadOnlyList<string> EnumerateSafely(string path, ScanState state)
    {
        var entries = new List<string>();
        IEnumerator<string>? enumerator = null;

        try
        {
            enumerator = Directory.EnumerateFileSystemEntries(path, "*", EnumerationOptions).GetEnumerator();
            while (true)
            {
                string current;
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    current = enumerator.Current;
                }
                catch (Exception exception) when (IsExpectedFileSystemException(exception))
                {
                    state.SkippedItems++;
                    break;
                }

                entries.Add(current);
            }
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            state.SkippedItems++;
        }
        finally
        {
            enumerator?.Dispose();
        }

        return entries;
    }

    private static bool TryGetAttributes(string path, ScanState state, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            state.SkippedItems++;
            attributes = default;
            return false;
        }
    }

    private static long TryGetFileSize(string path, ScanState state)
    {
        try
        {
            var length = new FileInfo(path).Length;
            state.FilesScanned++;
            state.BytesScanned = state.BytesScanned >= long.MaxValue - length
                ? long.MaxValue
                : state.BytesScanned + length;
            return length;
        }
        catch (Exception exception) when (IsExpectedFileSystemException(exception))
        {
            state.SkippedItems++;
            return 0;
        }
    }

    private static string GetDisplayName(string path)
    {
        try
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return path;
        }
    }

    private static bool IsExpectedFileSystemException(Exception exception)
        => exception is UnauthorizedAccessException
            or SecurityException
            or DirectoryNotFoundException
            or FileNotFoundException
            or DriveNotFoundException
            or PathTooLongException
            or IOException
            or NotSupportedException
            or ArgumentException;

    private sealed class ScanState(IProgress<StorageScanProgress>? progress)
    {
        private readonly Stopwatch _reportTimer = Stopwatch.StartNew();
        private readonly Dictionary<string, StorageEntry> _pendingEntryUpdates = new(StringComparer.OrdinalIgnoreCase);

        public long FilesScanned { get; set; }

        public long DirectoriesScanned { get; set; }

        public long BytesScanned { get; set; }

        public long SkippedItems { get; set; }

        public void Report(StorageEntry? entryUpdate = null, bool force = false)
        {
            if (progress is null)
            {
                return;
            }

            if (entryUpdate is not null)
            {
                _pendingEntryUpdates[entryUpdate.FullPath] = entryUpdate;
            }

            if (!force && _reportTimer.ElapsedMilliseconds < 150 && _pendingEntryUpdates.Count < 24)
            {
                return;
            }

            var updates = _pendingEntryUpdates.Count == 0
                ? null
                : _pendingEntryUpdates.Values.ToArray();
            _pendingEntryUpdates.Clear();
            _reportTimer.Restart();
            progress.Report(new StorageScanProgress(FilesScanned, DirectoriesScanned, BytesScanned, SkippedItems, updates));
        }
    }
}
