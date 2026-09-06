using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace ND_Explorer.Services;

public sealed class FileOperationService : IFileOperationService
{
    private const int BufferSize = 1024 * 1024;

    public Task<string> CreateDirectoryAsync(
        string parentPath,
        string name,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateLeafName(name);

            var path = Path.Combine(parentPath, name.Trim());
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException($"같은 이름의 항목이 이미 있습니다: {name.Trim()}");
            }

            Directory.CreateDirectory(path);
            return path;
        }, cancellationToken);

    public Task<string> RenameAsync(
        string sourcePath,
        string newName,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateLeafName(newName);

            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourcePath))
                         ?? throw new IOException("이 항목의 상위 폴더를 찾을 수 없습니다.");
            var destinationPath = Path.Combine(parent, newName.Trim());

            if (PathsEqual(sourcePath, destinationPath))
            {
                if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
                {
                    return sourcePath;
                }

                var temporaryPath = GetAvailableDestinationPath(sourcePath, parent);
                MoveExisting(sourcePath, temporaryPath);
                try
                {
                    MoveExisting(temporaryPath, destinationPath);
                }
                catch
                {
                    if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                    {
                        MoveExisting(temporaryPath, sourcePath);
                    }

                    throw;
                }
                return destinationPath;
            }

            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                throw new IOException($"같은 이름의 항목이 이미 있습니다: {newName.Trim()}");
            }

            MoveExisting(sourcePath, destinationPath);

            return destinationPath;
        }, cancellationToken);

    public async Task CopyAsync(
        IReadOnlyCollection<string> sourcePaths,
        string destinationDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destinationDirectory);

        var index = 0;
        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report($"복사 중 ({index}/{sourcePaths.Count}): {Path.GetFileName(sourcePath)}");

            var destinationPath = GetAvailableDestinationPath(sourcePath, destinationDirectory);
            EnsureNotInsideSource(sourcePath, destinationPath);
            try
            {
                await CopyItemAsync(sourcePath, destinationPath, cancellationToken);
            }
            catch
            {
                TryDeletePartialCopy(destinationPath);
                throw;
            }
        }
    }

    public async Task MoveAsync(
        IReadOnlyCollection<string> sourcePaths,
        string destinationDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destinationDirectory);

        var index = 0;
        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report($"이동 중 ({index}/{sourcePaths.Count}): {Path.GetFileName(sourcePath)}");

            var originalParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourcePath));
            if (originalParent is not null && PathsEqual(originalParent, destinationDirectory))
            {
                continue;
            }

            var destinationPath = GetAvailableDestinationPath(sourcePath, destinationDirectory);
            EnsureNotInsideSource(sourcePath, destinationPath);

            try
            {
                if (Directory.Exists(sourcePath))
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else if (File.Exists(sourcePath))
                {
                    File.Move(sourcePath, destinationPath);
                }
                else
                {
                    throw new FileNotFoundException("이동할 항목을 찾을 수 없습니다.", sourcePath);
                }
            }
            catch (IOException) when (!PathsShareRoot(sourcePath, destinationPath))
            {
                try
                {
                    await CopyItemAsync(sourcePath, destinationPath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    DeletePermanently(sourcePath);
                }
                catch
                {
                    TryDeletePartialCopy(destinationPath);
                    throw;
                }
            }
        }
    }

    public Task DeleteAsync(
        IReadOnlyCollection<string> paths,
        bool permanently,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var index = 0;
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                progress?.Report($"삭제 중 ({index}/{paths.Count}): {Path.GetFileName(path)}");

                if (permanently)
                {
                    DeletePermanently(path);
                }
                else
                {
                    SendToRecycleBin(path);
                }
            }
        }, cancellationToken);

    private static async Task CopyItemAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(sourcePath))
        {
            await CopyDirectoryAsync(sourcePath, destinationPath, cancellationToken);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("복사할 항목을 찾을 수 없습니다.", sourcePath);
        }

        await CopyFileAsync(sourcePath, destinationPath, cancellationToken);
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(
                filePath,
                Path.Combine(destinationDirectory, Path.GetFileName(filePath)),
                cancellationToken);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(childDirectory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            await CopyDirectoryAsync(
                childDirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(childDirectory)),
                cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, BufferSize, cancellationToken);
    }

    private static string GetAvailableDestinationPath(string sourcePath, string destinationDirectory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
        var candidate = Path.Combine(destinationDirectory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var isDirectory = Directory.Exists(sourcePath);
        var baseName = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (var index = 2; index < int.MaxValue; index++)
        {
            candidate = Path.Combine(destinationDirectory, $"{baseName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("사용 가능한 대상 이름을 만들 수 없습니다.");
    }

    private static void ValidateDestination(string destinationDirectory)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException($"대상 폴더를 찾을 수 없습니다: {destinationDirectory}");
        }
    }

    private static void ValidateLeafName(string name)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("이름을 입력해 주세요.", nameof(name));
        }

        if (trimmedName is "." or ".."
            || trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmedName.EndsWith('.')
            || trimmedName.EndsWith(' '))
        {
            throw new ArgumentException("파일 이름에 사용할 수 없는 문자가 포함되어 있습니다.", nameof(name));
        }
    }

    private static void EnsureNotInsideSource(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        var sourceWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath))
                                  + Path.DirectorySeparatorChar;
        var destinationFullPath = Path.GetFullPath(destinationPath);
        if (destinationFullPath.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("폴더를 자기 자신이나 하위 폴더 안으로 복사할 수 없습니다.");
        }
    }

    private static void DeletePermanently(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void MoveExisting(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath);
        }
        else
        {
            throw new FileNotFoundException("이동할 항목을 찾을 수 없습니다.", sourcePath);
        }
    }

    private static void TryDeletePartialCopy(string path)
    {
        try
        {
            DeletePermanently(path);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
            // Preserve the original copy error. A locked partial item can be removed manually.
        }
    }

    private static void SendToRecycleBin(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        }
        else if (File.Exists(path))
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        }
    }

    private static bool PathsShareRoot(string first, string second)
        => string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
}
