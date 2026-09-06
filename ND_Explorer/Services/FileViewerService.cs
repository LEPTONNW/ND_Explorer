using System.Diagnostics;
using System.IO;
using System.Windows;
using ND_Explorer.Models;
using ND_Explorer.Views;

namespace ND_Explorer.Services;

public sealed class FileViewerService : IFileViewerService
{
    public void Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("열 파일을 찾을 수 없습니다.", path);
        }

        Window? viewer = null;
        if (MediaFileSupport.IsImage(path))
        {
            viewer = new ImageViewerWindow(path);
        }
        else if (MediaFileSupport.IsVideo(path))
        {
            viewer = new VideoPlayerWindow(path);
        }

        if (viewer is not null)
        {
            viewer.Owner = Application.Current.MainWindow;
            viewer.Show();
            viewer.Activate();
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
