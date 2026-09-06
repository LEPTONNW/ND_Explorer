using ND_Explorer.Services;

namespace ND_Explorer.ViewModels;

public sealed class MainWindowViewModel : IDisposable
{
    public MainWindowViewModel(
        IFileSystemService fileSystemService,
        IFileOperationService fileOperationService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IImagePreviewService imagePreviewService,
        IFileViewerService fileViewerService)
    {
        Explorer = new ExplorerPaneViewModel(
            fileSystemService,
            fileOperationService,
            dialogService,
            settingsService,
            imagePreviewService,
            fileViewerService);
    }

    public ExplorerPaneViewModel Explorer { get; }

    public Task InitializeAsync() => Explorer.InitializeAsync();

    public void Dispose() => Explorer.Dispose();
}
