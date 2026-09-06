using System.Windows;
using System.IO;
using ND_Explorer.Services;
using ND_Explorer.ViewModels;

namespace ND_Explorer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var fileSystemService = new FileSystemService();
        var fileOperationService = new FileOperationService();
        var dialogService = new DialogService();
        var settingsService = new SettingsService();
        var imagePreviewService = new ImagePreviewService();
        var fileViewerService = new FileViewerService();
        var viewModel = new MainWindowViewModel(
            fileSystemService,
            fileOperationService,
            dialogService,
            settingsService,
            imagePreviewService,
            fileViewerService);
        var mainWindow = new MainWindow(viewModel, settingsService);

        MainWindow = mainWindow;
        mainWindow.Show();

        var fileArgument = e.Args.FirstOrDefault(File.Exists);
        if (fileArgument is not null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    fileViewerService.Open(Path.GetFullPath(fileArgument));
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        mainWindow,
                        $"파일을 열 수 없습니다: {exception.Message}",
                        "ND Explorer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }
    }
}
