namespace ND_Explorer.Services;

public interface IFileOperationService
{
    Task<string> CreateDirectoryAsync(
        string parentPath,
        string name,
        CancellationToken cancellationToken = default);

    Task<string> RenameAsync(
        string sourcePath,
        string newName,
        CancellationToken cancellationToken = default);

    Task CopyAsync(
        IReadOnlyCollection<string> sourcePaths,
        string destinationDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        IReadOnlyCollection<string> sourcePaths,
        string destinationDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        IReadOnlyCollection<string> paths,
        bool permanently,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
