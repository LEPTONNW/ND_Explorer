using System.Windows.Media.Imaging;

namespace ND_Explorer.Services;

public interface IImagePreviewService
{
    Task<BitmapSource?> LoadThumbnailAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken = default);
}
