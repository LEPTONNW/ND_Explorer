using ND_Explorer.Models;

namespace ND_Explorer.Services;

public interface IFileSystemService
{
    IAsyncEnumerable<FileSystemItem> EnumerateItemsAsync(string path, CancellationToken cancellationToken);
    IReadOnlyList<NavigationLocation> GetDrives();
    bool DirectoryExists(string path);
    string NormalizeDirectoryPath(string path);
}
