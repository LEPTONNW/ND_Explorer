using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.IO;
using ND_Explorer.Models;

namespace ND_Explorer.Services;

public sealed class FileSystemService : IFileSystemService
{
    public async IAsyncEnumerable<FileSystemItem> EnumerateItemsAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<FileSystemItem>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            Exception? completionError = null;
            try
            {
                foreach (var entryPath in Directory.EnumerateFileSystemEntries(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileSystemItem? item;
                    try
                    {
                        item = CreateItem(entryPath);
                    }
                    catch (Exception exception) when (exception is IOException
                                                      or UnauthorizedAccessException
                                                      or System.Security.SecurityException)
                    {
                        continue;
                    }

                    await channel.Writer.WriteAsync(item, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation is expected when the user moves to another folder.
            }
            catch (Exception exception)
            {
                completionError = exception;
            }
            finally
            {
                channel.Writer.TryComplete(completionError);
            }
        }, CancellationToken.None);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }

        await producer;
    }

    public IReadOnlyList<NavigationLocation> GetDrives()
    {
        var locations = new List<NavigationLocation>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})"
                    : drive.Name;
                locations.Add(new NavigationLocation(label, drive.RootDirectory.FullName, "\uEDA2"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                locations.Add(new NavigationLocation(drive.Name, drive.Name, "\uEDA2"));
            }
        }

        return locations;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string NormalizeDirectoryPath(string path)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.GetFullPath(expandedPath);
    }

    private static FileSystemItem CreateItem(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);

        if (isDirectory)
        {
            var directory = new DirectoryInfo(path);
            return new FileSystemItem
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                IsDirectory = true,
                IsHidden = attributes.HasFlag(FileAttributes.Hidden),
                LastModified = directory.LastWriteTime,
                TypeName = "파일 폴더"
            };
        }

        var file = new FileInfo(path);
        var extension = file.Extension.TrimStart('.');
        return new FileSystemItem
        {
            Name = file.Name,
            FullPath = file.FullName,
            IsDirectory = false,
            IsHidden = attributes.HasFlag(FileAttributes.Hidden),
            Size = file.Length,
            LastModified = file.LastWriteTime,
            TypeName = string.IsNullOrWhiteSpace(extension) ? "파일" : $"{extension.ToUpperInvariant()} 파일"
        };
    }
}
