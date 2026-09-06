using ND_Explorer.Models;

namespace ND_Explorer.Services;

public interface IStorageScanService
{
    Task<StorageScanResult> ScanAsync(
        string path,
        IProgress<StorageScanProgress>? progress,
        CancellationToken cancellationToken);
}
