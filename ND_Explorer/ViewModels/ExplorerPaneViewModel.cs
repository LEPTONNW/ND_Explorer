using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ND_Explorer.Infrastructure;
using ND_Explorer.Models;
using ND_Explorer.Services;

namespace ND_Explorer.ViewModels;

public sealed class ExplorerPaneViewModel : ObservableObject, IDisposable
{
    private const int UiBatchSize = 64;

    private readonly IFileSystemService _fileSystemService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IImagePreviewService _imagePreviewService;
    private readonly IFileViewerService _fileViewerService;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly List<FileSystemItem> _selectedItems = [];
    private readonly List<string> _clipboardPaths = [];
    private readonly AsyncRelayCommand _backCommand;
    private readonly AsyncRelayCommand _forwardCommand;
    private readonly AsyncRelayCommand _upCommand;
    private readonly AsyncRelayCommand _pasteCommand;
    private readonly AsyncRelayCommand _newFolderCommand;
    private readonly AsyncRelayCommand _renameCommand;
    private readonly AsyncRelayCommand _deleteCommand;
    private readonly AsyncRelayCommand _permanentDeleteCommand;
    private readonly RelayCommand _copyCommand;
    private readonly RelayCommand _cutCommand;
    private readonly RelayCommand _cancelOperationCommand;
    private CancellationTokenSource? _navigationCancellation;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _watcherDebounceCancellation;
    private CancellationTokenSource? _previewCancellation;
    private FileSystemWatcher? _watcher;
    private string _currentPath = string.Empty;
    private string _addressText = string.Empty;
    private string _filterText = string.Empty;
    private string? _errorMessage;
    private string _statusText = "준비됨";
    private bool _isLoading;
    private bool _isOperationRunning;
    private bool _isPreviewLoading;
    private bool _isEmpty;
    private bool _showHiddenItems;
    private bool _isSortDescending;
    private FileSystemItem? _selectedItem;
    private SortOption _selectedSortOption;
    private bool _clipboardIsCut;
    private BitmapSource? _previewImage;
    private string? _previewError;

    public ExplorerPaneViewModel(
        IFileSystemService fileSystemService,
        IFileOperationService fileOperationService,
        IDialogService dialogService,
        ISettingsService settingsService,
        IImagePreviewService imagePreviewService,
        IFileViewerService fileViewerService)
    {
        _fileSystemService = fileSystemService;
        _fileOperationService = fileOperationService;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _imagePreviewService = imagePreviewService;
        _fileViewerService = fileViewerService;

        SortOptions =
        [
            new SortOption("이름", nameof(FileSystemItem.Name)),
            new SortOption("수정한 날짜", nameof(FileSystemItem.LastModified)),
            new SortOption("유형", nameof(FileSystemItem.TypeName)),
            new SortOption("크기", nameof(FileSystemItem.Size))
        ];
        _selectedSortOption = SortOptions[0];

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ApplySort();

        NavigateCommand = new AsyncRelayCommand(_ => NavigateFromAddressAsync());
        NavigateToLocationCommand = new AsyncRelayCommand(NavigateToLocationAsync);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !string.IsNullOrWhiteSpace(CurrentPath));
        OpenItemCommand = new AsyncRelayCommand(OpenItemAsync, parameter => parameter is FileSystemItem);
        ClearFilterCommand = new RelayCommand(_ => FilterText = string.Empty, _ => !string.IsNullOrEmpty(FilterText));

        _newFolderCommand = new AsyncRelayCommand(_ => CreateFolderAsync(), _ => CanModifyCurrentFolder());
        _renameCommand = new AsyncRelayCommand(_ => RenameSelectedAsync(), _ => CanModifySelection(singleItemOnly: true));
        _copyCommand = new RelayCommand(_ => SetClipboard(isCut: false), _ => _selectedItems.Count > 0);
        _cutCommand = new RelayCommand(_ => SetClipboard(isCut: true), _ => CanModifySelection());
        _pasteCommand = new AsyncRelayCommand(_ => PasteAsync(), _ => CanModifyCurrentFolder() && _clipboardPaths.Count > 0);
        _deleteCommand = new AsyncRelayCommand(_ => DeleteSelectedAsync(permanently: false), _ => CanModifySelection());
        _permanentDeleteCommand = new AsyncRelayCommand(_ => DeleteSelectedAsync(permanently: true), _ => CanModifySelection());
        _cancelOperationCommand = new RelayCommand(_ => _operationCancellation?.Cancel(), _ => IsOperationRunning);

        NewFolderCommand = _newFolderCommand;
        RenameCommand = _renameCommand;
        CopyCommand = _copyCommand;
        CutCommand = _cutCommand;
        PasteCommand = _pasteCommand;
        DeleteCommand = _deleteCommand;
        PermanentDeleteCommand = _permanentDeleteCommand;
        CancelOperationCommand = _cancelOperationCommand;

        _backCommand = new AsyncRelayCommand(_ => GoBackAsync(), _ => _backHistory.Count > 0);
        _forwardCommand = new AsyncRelayCommand(_ => GoForwardAsync(), _ => _forwardHistory.Count > 0);
        _upCommand = new AsyncRelayCommand(_ => GoUpAsync(), _ => CanNavigateUp());
        BackCommand = _backCommand;
        ForwardCommand = _forwardCommand;
        UpCommand = _upCommand;

        BuildNavigationLocations();
        BuildRecentLocations();
    }

    public BulkObservableCollection<FileSystemItem> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public ObservableCollection<NavigationLocation> Favorites { get; } = [];
    public ObservableCollection<NavigationLocation> Drives { get; } = [];
    public ObservableCollection<NavigationLocation> RecentLocations { get; } = [];
    public IReadOnlyList<SortOption> SortOptions { get; }

    public ICommand NavigateCommand { get; }
    public ICommand NavigateToLocationCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PermanentDeleteCommand { get; }
    public ICommand CancelOperationCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                _upCommand.RaiseCanExecuteChanged();
                RaiseFileCommandCanExecuteChanged();
            }
        }
    }

    public string AddressText
    {
        get => _addressText;
        set => SetProperty(ref _addressText, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ItemsView.Refresh();
                UpdateStatus();
                if (ClearFilterCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RaiseFileCommandCanExecuteChanged();
            }
        }
    }

    public bool IsOperationRunning
    {
        get => _isOperationRunning;
        private set
        {
            if (SetProperty(ref _isOperationRunning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RaiseFileCommandCanExecuteChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsOperationRunning;

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public bool ShowHiddenItems
    {
        get => _showHiddenItems;
        set
        {
            if (SetProperty(ref _showHiddenItems, value))
            {
                ItemsView.Refresh();
                UpdateStatus();
            }
        }
    }

    public bool IsSortDescending
    {
        get => _isSortDescending;
        set
        {
            if (SetProperty(ref _isSortDescending, value))
            {
                ApplySort();
            }
        }
    }

    public SortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (value is not null && SetProperty(ref _selectedSortOption, value))
            {
                ApplySort();
            }
        }
    }

    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
                OnPropertyChanged(nameof(IsImageSelected));
                OnPropertyChanged(nameof(IsVideoSelected));
                OnPropertyChanged(nameof(OpenActionText));
                UpdateSelectedPreview(value);
                UpdateStatus();
            }
        }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
            {
                OnPropertyChanged(nameof(HasImagePreview));
            }
        }
    }

    public string? PreviewError
    {
        get => _previewError;
        private set => SetProperty(ref _previewError, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetProperty(ref _isPreviewLoading, value);
    }

    public bool HasSelectedItem => SelectedItem is not null;
    public bool IsImageSelected => SelectedItem?.IsImage == true;
    public bool IsVideoSelected => SelectedItem?.IsVideo == true;
    public bool HasImagePreview => PreviewImage is not null;
    public string OpenActionText => IsVideoSelected ? "동영상 재생" : IsImageSelected ? "이미지 보기" : "열기";

    private void UpdateSelectedPreview(FileSystemItem? item)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        PreviewImage = null;
        PreviewError = null;
        IsPreviewLoading = false;

        if (item?.IsImage != true)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        IsPreviewLoading = true;
        _ = LoadSelectedPreviewAsync(item, cancellation);
    }

    private async Task LoadSelectedPreviewAsync(FileSystemItem item, CancellationTokenSource cancellation)
    {
        try
        {
            var image = await _imagePreviewService.LoadThumbnailAsync(item.FullPath, 720, cancellation.Token);
            if (!cancellation.IsCancellationRequested
                && ReferenceEquals(_previewCancellation, cancellation)
                && string.Equals(item.FullPath, SelectedItem?.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                PreviewImage = image;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Selection changed before the thumbnail was ready.
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException)
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                PreviewError = $"미리보기를 표시할 수 없습니다: {exception.Message}";
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                PreviewError = $"미리보기를 표시할 수 없습니다: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                IsPreviewLoading = false;
                _previewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var initialPath = _settingsService.Current.LastPath;
        if (string.IsNullOrWhiteSpace(initialPath) || !_fileSystemService.DirectoryExists(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(initialPath) || !_fileSystemService.DirectoryExists(initialPath))
        {
            initialPath = Drives.FirstOrDefault()?.Path ?? Environment.CurrentDirectory;
        }

        await NavigateAsync(initialPath, addToHistory: false);
    }

    public void SetSelectedItems(IEnumerable<FileSystemItem> selectedItems)
    {
        _selectedItems.Clear();
        _selectedItems.AddRange(selectedItems);
        SelectedItem = _selectedItems.FirstOrDefault();
        RaiseFileCommandCanExecuteChanged();
        UpdateStatus();
    }

    public async Task<FileSystemItem?> RevealPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string folderPath;
        string? itemToSelect = null;
        if (Directory.Exists(path))
        {
            folderPath = path;
        }
        else if (File.Exists(path))
        {
            folderPath = Path.GetDirectoryName(path) ?? string.Empty;
            itemToSelect = path;
        }
        else
        {
            ShowError("선택한 항목이 이동되었거나 삭제되어 위치를 열 수 없습니다.");
            return null;
        }

        FilterText = string.Empty;
        if (!await NavigateAsync(folderPath, addToHistory: true) || itemToSelect is null)
        {
            return null;
        }

        var item = Items.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, itemToSelect, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SetSelectedItems([item]);
        }

        return item;
    }

    public async Task ImportDroppedItemsAsync(
        IEnumerable<string> sourcePaths,
        string? destinationDirectory,
        bool move)
    {
        var paths = sourcePaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var destination = string.IsNullOrWhiteSpace(destinationDirectory) ? CurrentPath : destinationDirectory;

        if (paths.Length == 0 || !_fileSystemService.DirectoryExists(destination) || IsBusy)
        {
            return;
        }

        await RunFileOperationAsync(
            (token, progress) => move
                ? _fileOperationService.MoveAsync(paths, destination, progress, token)
                : _fileOperationService.CopyAsync(paths, destination, progress, token),
            $"{paths.Length:N0}개 항목을 {(move ? "이동했습니다" : "복사했습니다")}");
    }

    public void Dispose()
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _watcherDebounceCancellation?.Cancel();
        _watcherDebounceCancellation?.Dispose();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        DisposeWatcher();
        _settingsService.Save();
    }

    private async Task NavigateFromAddressAsync()
        => await NavigateAsync(AddressText, addToHistory: true);

    private async Task NavigateToLocationAsync(object? parameter)
    {
        var path = parameter switch
        {
            NavigationLocation location => location.Path,
            string stringPath => stringPath,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(path))
        {
            await NavigateAsync(path, addToHistory: true);
        }
    }

    private async Task<bool> NavigateAsync(string requestedPath, bool addToHistory)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            ShowError("폴더 경로를 입력해 주세요.");
            AddressText = CurrentPath;
            return false;
        }

        string normalizedPath;
        try
        {
            normalizedPath = _fileSystemService.NormalizeDirectoryPath(requestedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowError("올바른 폴더 경로를 입력해 주세요.");
            AddressText = CurrentPath;
            return false;
        }

        if (!_fileSystemService.DirectoryExists(normalizedPath))
        {
            ShowError("폴더가 존재하지 않거나 현재 접근할 수 없습니다.");
            AddressText = CurrentPath;
            return false;
        }

        if (addToHistory && !string.IsNullOrWhiteSpace(CurrentPath)
            && !PathsEqual(CurrentPath, normalizedPath))
        {
            _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
            UpdateNavigationCommands();
        }

        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        var navigationCancellation = new CancellationTokenSource();
        _navigationCancellation = navigationCancellation;

        CurrentPath = normalizedPath;
        AddressText = normalizedPath;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }
        ErrorMessage = null;
        IsLoading = true;
        IsEmpty = false;
        Items.Clear();
        SelectedItem = null;
        StatusText = "폴더 내용을 불러오는 중...";

        var batch = new List<FileSystemItem>(UiBatchSize);

        try
        {
            await foreach (var item in _fileSystemService.EnumerateItemsAsync(normalizedPath, navigationCancellation.Token))
            {
                batch.Add(item);
                if (batch.Count < UiBatchSize)
                {
                    continue;
                }

                AddBatch(batch);
                batch.Clear();
                StatusText = $"{Items.Count:N0}개 항목을 불러오는 중...";
                await Task.Yield();
            }

            AddBatch(batch);
            ConfigureWatcher(normalizedPath);
            RememberPath(normalizedPath);
            return true;
        }
        catch (OperationCanceledException) when (navigationCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            ShowError("이 폴더에 접근할 권한이 없습니다.");
            return false;
        }
        catch (IOException exception)
        {
            ShowError($"폴더를 읽는 중 오류가 발생했습니다: {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            ShowError($"폴더를 열 수 없습니다: {exception.Message}");
            return false;
        }
        finally
        {
            if (ReferenceEquals(_navigationCancellation, navigationCancellation))
            {
                IsLoading = false;
                UpdateStatus();
            }
        }
    }

    private async Task RefreshAsync()
    {
        BuildDrives();
        await NavigateAsync(CurrentPath, addToHistory: false);
    }

    private async Task GoBackAsync()
    {
        if (_backHistory.Count == 0)
        {
            return;
        }

        var target = _backHistory.Pop();
        var previousPath = CurrentPath;
        if (await NavigateAsync(target, addToHistory: false))
        {
            _forwardHistory.Push(previousPath);
        }
        else
        {
            _backHistory.Push(target);
        }

        UpdateNavigationCommands();
    }

    private async Task GoForwardAsync()
    {
        if (_forwardHistory.Count == 0)
        {
            return;
        }

        var target = _forwardHistory.Pop();
        var previousPath = CurrentPath;
        if (await NavigateAsync(target, addToHistory: false))
        {
            _backHistory.Push(previousPath);
        }
        else
        {
            _forwardHistory.Push(target);
        }

        UpdateNavigationCommands();
    }

    private async Task GoUpAsync()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
        {
            await NavigateAsync(parent.FullName, addToHistory: true);
        }
    }

    private async Task OpenItemAsync(object? parameter)
    {
        if (parameter is not FileSystemItem item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            await NavigateAsync(item.FullPath, addToHistory: true);
            return;
        }

        try
        {
            _fileViewerService.Open(item.FullPath);
        }
        catch (Exception exception)
        {
            ShowError($"파일을 열 수 없습니다: {exception.Message}");
        }
    }

    private async Task CreateFolderAsync()
    {
        var name = _dialogService.Prompt("새 폴더", "새 폴더의 이름을 입력하세요.", "새 폴더");
        if (name is null)
        {
            return;
        }

        await RunFileOperationAsync(
            async (token, _) => await _fileOperationService.CreateDirectoryAsync(CurrentPath, name, token),
            $"폴더를 만들었습니다: {name}");
    }

    private async Task RenameSelectedAsync()
    {
        var item = _selectedItems.SingleOrDefault();
        if (item is null)
        {
            return;
        }

        var newName = _dialogService.Prompt("이름 바꾸기", "새 이름을 입력하세요.", item.Name);
        if (newName is null || string.Equals(newName, item.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunFileOperationAsync(
            async (token, _) => await _fileOperationService.RenameAsync(item.FullPath, newName, token),
            $"이름을 바꿨습니다: {newName}");
    }

    private void SetClipboard(bool isCut)
    {
        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(_selectedItems.Select(item => item.FullPath));
        _clipboardIsCut = isCut;
        StatusText = $"{_clipboardPaths.Count:N0}개 항목을 {(isCut ? "잘라냈습니다" : "복사했습니다")}";
        _pasteCommand.RaiseCanExecuteChanged();
    }

    private async Task PasteAsync()
    {
        if (_clipboardPaths.Count == 0)
        {
            return;
        }

        var paths = _clipboardPaths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (paths.Length == 0)
        {
            _clipboardPaths.Clear();
            _pasteCommand.RaiseCanExecuteChanged();
            ShowError("복사하거나 이동할 원본 항목을 찾을 수 없습니다.");
            return;
        }

        var moving = _clipboardIsCut;
        var succeeded = await RunFileOperationAsync(
            (token, progress) => moving
                ? _fileOperationService.MoveAsync(paths, CurrentPath, progress, token)
                : _fileOperationService.CopyAsync(paths, CurrentPath, progress, token),
            $"{paths.Length:N0}개 항목을 {(moving ? "이동했습니다" : "복사했습니다")}");

        if (succeeded && moving)
        {
            _clipboardPaths.Clear();
            _clipboardIsCut = false;
            _pasteCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task DeleteSelectedAsync(bool permanently)
    {
        var paths = _selectedItems.Select(item => item.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var targetDescription = paths.Length == 1
            ? $"'{Path.GetFileName(paths[0])}'"
            : $"선택한 {paths.Length:N0}개 항목";
        var message = permanently
            ? $"{targetDescription}을(를) 영구 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다."
            : $"{targetDescription}을(를) 휴지통으로 보내시겠습니까?";

        if (!_dialogService.Confirm(permanently ? "영구 삭제" : "삭제", message, isDestructive: true))
        {
            return;
        }

        await RunFileOperationAsync(
            (token, progress) => _fileOperationService.DeleteAsync(paths, permanently, progress, token),
            permanently ? "선택한 항목을 영구 삭제했습니다." : "선택한 항목을 휴지통으로 보냈습니다.");
    }

    private async Task<bool> RunFileOperationAsync(
        Func<CancellationToken, IProgress<string>, Task> operation,
        string successMessage)
    {
        _operationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsOperationRunning = true;
        ErrorMessage = null;

        var progress = new Progress<string>(message => StatusText = message);
        try
        {
            await operation(cancellation.Token, progress);
            StatusText = successMessage;
            await RefreshAsync();
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "파일 작업을 취소했습니다.";
            await RefreshAsync();
            return false;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            ShowError($"파일 작업을 완료하지 못했습니다: {exception.Message}");
            await RefreshAsync();
            return false;
        }
        catch (Exception exception)
        {
            ShowError($"파일 작업을 완료하지 못했습니다: {exception.Message}");
            await RefreshAsync();
            return false;
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                IsOperationRunning = false;
                cancellation.Dispose();
                _operationCancellation = null;
            }
        }
    }

    private void AddBatch(IReadOnlyCollection<FileSystemItem> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using (ItemsView.DeferRefresh())
        {
            Items.AddRange(batch);
        }
    }

    private bool FilterItem(object item)
    {
        if (item is not FileSystemItem fileSystemItem)
        {
            return false;
        }

        if (!ShowHiddenItems && fileSystemItem.IsHidden)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(FilterText)
            || fileSystemItem.Name.Contains(FilterText.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplySort()
    {
        using (ItemsView.DeferRefresh())
        {
            ItemsView.SortDescriptions.Clear();
            ItemsView.SortDescriptions.Add(new SortDescription(
                nameof(FileSystemItem.IsDirectory),
                ListSortDirection.Descending));
            ItemsView.SortDescriptions.Add(new SortDescription(
                SelectedSortOption.PropertyName,
                IsSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsLoading)
        {
            return;
        }

        var visibleCount = Items.Count(item => FilterItem(item));
        IsEmpty = !HasError && visibleCount == 0;

        if (_selectedItems.Count > 1)
        {
            var selectedFileSize = _selectedItems
                .Where(item => !item.IsDirectory && item.Size.HasValue)
                .Sum(item => item.Size!.Value);
            StatusText = $"{visibleCount:N0}개 항목  ·  {_selectedItems.Count:N0}개 선택  ·  {FormatSize(selectedFileSize)}";
        }
        else if (SelectedItem is not null)
        {
            StatusText = SelectedItem.IsDirectory
                ? $"{visibleCount:N0}개 항목  ·  선택: {SelectedItem.Name}"
                : $"{visibleCount:N0}개 항목  ·  선택: {SelectedItem.Name} ({SelectedItem.SizeText})";
        }
        else if (visibleCount != Items.Count)
        {
            StatusText = $"{visibleCount:N0} / {Items.Count:N0}개 항목";
        }
        else
        {
            StatusText = $"{visibleCount:N0}개 항목";
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsEmpty = false;
        StatusText = message;
    }

    private bool CanNavigateUp()
        => !string.IsNullOrWhiteSpace(CurrentPath) && Directory.GetParent(CurrentPath) is not null;

    private void UpdateNavigationCommands()
    {
        _backCommand.RaiseCanExecuteChanged();
        _forwardCommand.RaiseCanExecuteChanged();
        _upCommand.RaiseCanExecuteChanged();
    }

    private bool CanModifyCurrentFolder()
        => !IsBusy
           && !string.IsNullOrWhiteSpace(CurrentPath)
           && _fileSystemService.DirectoryExists(CurrentPath);

    private bool CanModifySelection(bool singleItemOnly = false)
        => CanModifyCurrentFolder()
           && _selectedItems.Count > 0
           && (!singleItemOnly || _selectedItems.Count == 1);

    private void RaiseFileCommandCanExecuteChanged()
    {
        _newFolderCommand.RaiseCanExecuteChanged();
        _renameCommand.RaiseCanExecuteChanged();
        _copyCommand.RaiseCanExecuteChanged();
        _cutCommand.RaiseCanExecuteChanged();
        _pasteCommand.RaiseCanExecuteChanged();
        _deleteCommand.RaiseCanExecuteChanged();
        _permanentDeleteCommand.RaiseCanExecuteChanged();
        _cancelOperationCommand.RaiseCanExecuteChanged();
    }

    private void ConfigureWatcher(string path)
    {
        DisposeWatcher();

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size
            };
            _watcher.Created += OnWatchedFolderChanged;
            _watcher.Deleted += OnWatchedFolderChanged;
            _watcher.Changed += OnWatchedFolderChanged;
            _watcher.Renamed += OnWatchedFolderChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException)
        {
            DisposeWatcher();
        }
    }

    private void OnWatchedFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (IsOperationRunning)
        {
            return;
        }

        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _watcherDebounceCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        _ = RefreshAfterWatcherDelayAsync(CurrentPath, replacement);
    }

    private async Task RefreshAfterWatcherDelayAsync(string watchedPath, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(350, cancellation.Token);
            var application = Application.Current;
            if (application is null)
            {
                return;
            }

            await application.Dispatcher.InvokeAsync(() =>
            {
                if (!cancellation.IsCancellationRequested
                    && !IsBusy
                    && PathsEqual(CurrentPath, watchedPath))
                {
                    _ = RefreshAsync();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // A newer file-system event replaced this refresh request.
        }
        finally
        {
            if (ReferenceEquals(_watcherDebounceCancellation, cancellation))
            {
                _watcherDebounceCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnWatchedFolderChanged;
        _watcher.Deleted -= OnWatchedFolderChanged;
        _watcher.Changed -= OnWatchedFolderChanged;
        _watcher.Renamed -= OnWatchedFolderChanged;
        _watcher.Dispose();
        _watcher = null;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffixIndex = 0;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0 ? $"{bytes:N0} B" : $"{value:N1} {suffixes[suffixIndex]}";
    }

    private void BuildNavigationLocations()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddFavorite("홈", userProfile, "\uE80F");
        AddFavorite("바탕 화면", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE8FC");
        AddFavorite("다운로드", Path.Combine(userProfile, "Downloads"), "\uE896");
        AddFavorite("문서", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5");
        AddFavorite("사진", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uEB9F");
        BuildDrives();
    }

    private void RememberPath(string path)
    {
        var settings = _settingsService.Current;
        settings.RecentPaths ??= [];

        if (string.Equals(settings.LastPath, path, StringComparison.OrdinalIgnoreCase)
            && settings.RecentPaths.Count > 0
            && PathsEqual(settings.RecentPaths[0], path))
        {
            return;
        }

        settings.LastPath = path;
        settings.RecentPaths.RemoveAll(recentPath => PathsEqual(recentPath, path));
        settings.RecentPaths.Insert(0, path);
        if (settings.RecentPaths.Count > 12)
        {
            settings.RecentPaths.RemoveRange(12, settings.RecentPaths.Count - 12);
        }

        BuildRecentLocations();
        _settingsService.Save();
    }

    private void BuildRecentLocations()
    {
        RecentLocations.Clear();
        var recentPaths = _settingsService.Current.RecentPaths ?? [];
        foreach (var path in recentPaths.Where(_fileSystemService.DirectoryExists))
        {
            var trimmedPath = Path.TrimEndingDirectorySeparator(path);
            var name = Path.GetFileName(trimmedPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = path;
            }

            RecentLocations.Add(new NavigationLocation(name, path, "\uE823"));
        }
    }

    private void AddFavorite(string name, string path, string glyph)
    {
        if (!string.IsNullOrWhiteSpace(path)
            && _fileSystemService.DirectoryExists(path)
            && Favorites.All(location => !PathsEqual(location.Path, path)))
        {
            Favorites.Add(new NavigationLocation(name, path, glyph));
        }
    }

    private void BuildDrives()
    {
        Drives.Clear();
        foreach (var drive in _fileSystemService.GetDrives())
        {
            Drives.Add(drive);
        }
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);
}
