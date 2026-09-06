using System.IO;
using System.Windows.Media.Imaging;

namespace ND_Explorer.Services;

public sealed class ImagePreviewService : IImagePreviewService
{
    public Task<BitmapSource?> LoadThumbnailAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
        => Task.Run<BitmapSource?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = Math.Clamp(decodePixelWidth, 64, 1600);
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }, cancellationToken);
}
