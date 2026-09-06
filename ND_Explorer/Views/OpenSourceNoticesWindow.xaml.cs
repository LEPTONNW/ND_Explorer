using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ND_Explorer.Views;

public partial class OpenSourceNoticesWindow : Window
{
    private const string LibVlcSharpSourceUrl = "https://code.videolan.org/videolan/LibVLCSharp/-/tree/3.x";
    private const string LibVlcSourceUrl = "https://code.videolan.org/videolan/vlc/-/tree/3.0.x";
    private const string LgplLicenseUrl = "https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html";

    public OpenSourceNoticesWindow()
    {
        InitializeComponent();
    }

    private static void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void OpenLibVlcSharpSource_OnClick(object sender, RoutedEventArgs e) => OpenUrl(LibVlcSharpSourceUrl);
    private void OpenLibVlcSource_OnClick(object sender, RoutedEventArgs e) => OpenUrl(LibVlcSourceUrl);
    private void OpenLgplLicense_OnClick(object sender, RoutedEventArgs e)
    {
        var localLicense = Path.Combine(AppContext.BaseDirectory, "Licenses", "LGPL-2.1.txt");
        OpenUrl(File.Exists(localLicense) ? localLicense : LgplLicenseUrl);
    }
}
