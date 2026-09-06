using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ND_Explorer.Infrastructure;
using ND_Explorer.Models;
using ND_Explorer.Services;

namespace ND_Explorer.ViewModels;

public sealed class DriveAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly IStorageScanService _scanService;
    private readonly DispatcherTimer _changeDebounceTimer;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _entryScanCancellation;
    private FileSystemWatcher? _watcher;
    private StorageEntry? _selectedEntry;
    private string _currentPath;
    private string _statusText = "분석 준비됨";
    private string _driveDetails = string.Empty;
    private bool _isBusy;
    private bool _isActive;
    private bool _rescanRequested;
    private bool _disposed;
    private int _scanGeneration;
    private int _entryScanGeneration;

    public DriveAnalysisViewModel(DriveInfo drive, IStorageScanService scanService)
    {
        _scanService = scanService;
        RootPath = drive.RootDirectory.FullName;
        _currentPath = RootPath;
        Entries = new BulkObservableCollection<StorageEntry>();
        RefreshCommand = new AsyncRelayCommand(_ => ScanCurrentAsync(preserveExistingTree: true), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => CancelScan(), _ => IsBusy);
        UpCommand = new AsyncRelayCommand(_ => NavigateUpAsync(), _ => CanNavigateUp && !IsBusy);

        _changeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _changeDebounceTimer.Tick += ChangeDebounceTimer_OnTick;
        UpdateDriveInfo(drive);
    }

    public string RootPath { get; }

    public string TabLabel { get; private set; } = string.Empty;

    public string DriveDetails
    {
        get => _driveDetails;
        private set => SetProperty(ref _driveDetails, value);
    }

    public BulkObservableCollection<StorageEntry> Entries { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(CanNavigateUp));
                UpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                UpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanNavigateUp => !PathsEqual(CurrentPath, RootPath);

    public StorageEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(SelectedEntryDetails));
            }
        }
    }

    public string SelectedEntryDetails => SelectedEntry is null
        ? "사각형을 선택하면 항목 정보가 표시됩니다. 폴더를 클릭하면 내부 항목을 펼칩니다."
        : $"{SelectedEntry.Name}  ·  {SelectedEntry.TypeText}  ·  {SelectedEntry.SizeText}{(SelectedEntry.IsPartial ? "  ·  일부 항목 접근 불가" : string.Empty)}";

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand UpCommand { get; }

    public void ReportStatus(string message) => StatusText = message;

    public async Task ActivateAsync()
    {
        if (_disposed)
        {
            return;
        }

        _isActive = true;
        await ScanCurrentAsync(preserveExistingTree: Entries.Count > 0);
    }

    public void Deactivate()
    {
        _isActive = false;
        _changeDebounceTimer.Stop();
        StopWatcher();
        CancelScan();
    }

    public async Task OpenEntryAsync(StorageEntry entry)
    {
        SelectedEntry = entry;
        if (!entry.IsDirectory || _disposed)
        {
            return;
        }

        var current = FindEntry(Entries, entry.FullPath);
        if (current is null)
        {
            return;
        }

        if (current.IsLoading)
        {
            _entryScanCancellation?.Cancel();
            UpdateEntry(current.FullPath, item => item with { Children = null, IsExpanded = false, IsLoading = false });
            return;
        }

        if (HasLoadingEntry(Entries))
        {
            StatusText = "현재 폴더 펼침이 끝난 후 다음 폴더를 선택해 주세요. 분석 중인 폴더를 다시 클릭하면 취소할 수 있습니다.";
            return;
        }

        if (current.Children is not null)
        {
            UpdateEntry(current.FullPath, item => item with { IsExpanded = !item.IsExpanded });
            return;
        }

        await ExpandEntryAsync(current);
    }

    private async Task ExpandEntryAsync(StorageEntry entry)
    {
        var generation = ++_entryScanGeneration;
        _entryScanCancellation?.Cancel();
        _entryScanCancellation?.Dispose();
        _entryScanCancellation = new CancellationTokenSource();
        var cancellationToken = _entryScanCancellation.Token;
        var childEntries = new Dictionary<string, StorageEntry>(StringComparer.OrdinalIgnoreCase);

        UpdateEntry(entry.FullPath, item => item with
        {
            Children = Array.Empty<StorageEntry>(),
            IsExpanded = true,
            IsLoading = true
        });
        StatusText = $"{entry.Name} 내부 항목을 분석하는 중…";

        var progress = new Progress<StorageScanProgress>(value =>
        {
            if (generation != _entryScanGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (value.EntryUpdates is { Count: > 0 })
            {
                foreach (var update in value.EntryUpdates)
                {
                    childEntries[update.FullPath] = update;
                }

                var children = childEntries.Values
                    .OrderByDescending(child => child.Size)
                    .ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                UpdateEntry(entry.FullPath, item => item with
                {
                    Children = children,
                    IsExpanded = true,
                    IsLoading = true
                });
            }

            StatusText = $"{entry.Name} 분석 중  ·  파일 {value.FilesScanned:N0}개  ·  폴더 {value.DirectoriesScanned:N0}개";
        });

        try
        {
            var result = await _scanService.ScanAsync(entry.FullPath, progress, cancellationToken);
            if (generation != _entryScanGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            UpdateEntry(entry.FullPath, item => item with
            {
                Children = result.Entries,
                IsExpanded = true,
                IsLoading = false,
                IsPartial = item.IsPartial || result.SkippedItems > 0
            });
            StatusText = $"{entry.Name} 펼침 완료  ·  {result.Entries.Count:N0}개 항목  ·  {result.Elapsed.TotalSeconds:N1}초"
                + (result.SkippedItems > 0 ? $"  ·  변경/접근 제한 {result.SkippedItems:N0}건 건너뜀" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            if (generation == _entryScanGeneration)
            {
                UpdateEntry(entry.FullPath, item => item with { Children = null, IsExpanded = false, IsLoading = false });
                StatusText = $"{entry.Name} 내부 분석을 취소했습니다.";
            }
        }
        catch (Exception exception)
        {
            if (generation == _entryScanGeneration)
            {
                UpdateEntry(entry.FullPath, item => item with
                {
                    Children = null,
                    IsExpanded = false,
                    IsLoading = false,
                    IsPartial = true
                });
                StatusText = $"{entry.Name} 내부를 분석할 수 없습니다: {exception.Message}";
            }
        }
    }

    public void UpdateDriveInfo(DriveInfo drive)
    {
        var driveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            if (drive.IsReady)
            {
                var volume = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "로컬 디스크" : drive.VolumeLabel;
                TabLabel = $"{driveName}  {volume}";
                var used = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
                DriveDetails = $"사용 {StorageEntry.FormatSize(used)} / 전체 {StorageEntry.FormatSize(drive.TotalSize)}  ·  여유 {StorageEntry.FormatSize(drive.AvailableFreeSpace)}";
            }
            else
            {
                TabLabel = $"{driveName}  준비 안 됨";
                DriveDetails = "드라이브가 준비되지 않았습니다.";
            }
        }
        catch (Exception)
        {
            TabLabel = driveName;
            DriveDetails = "드라이브 정보를 읽을 수 없습니다.";
        }

        OnPropertyChanged(nameof(TabLabel));
    }

    private Task NavigateUpAsync()
    {
        var parent = Directory.GetParent(CurrentPath)?.FullName;
        return string.IsNullOrEmpty(parent) || !IsWithinRoot(parent)
            ? Task.CompletedTask
            : ScanPathAsync(parent);
    }

    private Task ScanCurrentAsync(bool preserveExistingTree = true)
        => ScanPathAsync(CurrentPath, preserveExistingTree);

    private async Task ScanPathAsync(string requestedPath, bool preserveExistingTree = false)
    {
        if (_disposed)
        {
            return;
        }

        var path = ResolveExistingPath(requestedPath);
        var keepTree = preserveExistingTree && PathsEqual(path, CurrentPath) && Entries.Count > 0;
        var generation = ++_scanGeneration;
        if (!keepTree)
        {
            _entryScanGeneration++;
            _entryScanCancellation?.Cancel();
        }

        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;

        CurrentPath = path;
        if (!keepTree)
        {
            SelectedEntry = null;
            Entries.Clear();
        }

        IsBusy = true;
        _rescanRequested = false;
        StatusText = "파일과 폴더 크기를 계산하는 중…";
        StopWatcher();

        var progress = new Progress<StorageScanProgress>(value =>
        {
            if (generation == _scanGeneration && !cancellationToken.IsCancellationRequested)
            {
                if (value.EntryUpdates is { Count: > 0 })
                {
                    var updates = value.EntryUpdates
                        .Select(PreserveExpansionState)
                        .ToArray();
                    Entries.UpsertRange(
                        updates,
                        (existing, update) => string.Equals(existing.FullPath, update.FullPath, StringComparison.OrdinalIgnoreCase));
                }

                StatusText = $"분석 중  ·  표시 {Entries.Count:N0}개  ·  파일 {value.FilesScanned:N0}개  ·  폴더 {value.DirectoriesScanned:N0}개  ·  {StorageEntry.FormatSize(value.BytesScanned)}";
            }
        });

        try
        {
            var result = await _scanService.ScanAsync(path, progress, cancellationToken);
            if (generation != _scanGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var finalEntries = result.Entries
                .Select(PreserveExpansionState)
                .ToArray();
            Entries.Reset(finalEntries);
            if (SelectedEntry is not null)
            {
                SelectedEntry = FindEntry(Entries, SelectedEntry.FullPath);
            }

            StatusText = $"완료  ·  {result.Entries.Count:N0}개 항목  ·  {StorageEntry.FormatSize(result.TotalSize)}  ·  {result.Elapsed.TotalSeconds:N1}초"
                + (result.SkippedItems > 0 ? $"  ·  변경/접근 제한 {result.SkippedItems:N0}건 건너뜀" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            if (generation == _scanGeneration)
            {
                StatusText = "분석이 취소되었습니다.";
            }
        }
        catch (Exception exception)
        {
            if (generation == _scanGeneration)
            {
                StatusText = $"분석을 계속할 수 없습니다: {exception.Message}";
            }
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsBusy = false;
                if (_isActive && !_disposed)
                {
                    StartWatcher();
                }

                if (_rescanRequested && _isActive && !_disposed)
                {
                    ScheduleRefresh();
                }
            }
        }
    }

    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        _entryScanCancellation?.Cancel();
    }

    private void StartWatcher()
    {
        if (!_isActive || _disposed || !Directory.Exists(CurrentPath))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(CurrentPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.Size
                    | NotifyFilters.LastWrite,
                InternalBufferSize = 32 * 1024,
                EnableRaisingEvents = true
            };
            _watcher.Changed += Watcher_OnChanged;
            _watcher.Created += Watcher_OnChanged;
            _watcher.Deleted += Watcher_OnChanged;
            _watcher.Renamed += Watcher_OnRenamed;
            _watcher.Error += Watcher_OnError;
        }
        catch (Exception)
        {
            StopWatcher();
        }
    }

    private void StopWatcher()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= Watcher_OnChanged;
            watcher.Created -= Watcher_OnChanged;
            watcher.Deleted -= Watcher_OnChanged;
            watcher.Renamed -= Watcher_OnRenamed;
            watcher.Error -= Watcher_OnError;
            watcher.Dispose();
        }
        catch
        {
            // A drive can disappear while its watcher is being disposed.
        }
    }

    private void Watcher_OnChanged(object sender, FileSystemEventArgs e) => DispatchRefreshRequest();

    private void Watcher_OnRenamed(object sender, RenamedEventArgs e) => DispatchRefreshRequest();

    private void Watcher_OnError(object sender, ErrorEventArgs e) => DispatchRefreshRequest();

    private void DispatchRefreshRequest()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(ScheduleRefresh, DispatcherPriority.Background);
    }

    private void ScheduleRefresh()
    {
        if (!_isActive || _disposed)
        {
            return;
        }

        _rescanRequested = true;
        _changeDebounceTimer.Stop();
        _changeDebounceTimer.Start();
    }

    private void ChangeDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _changeDebounceTimer.Stop();
        if (IsBusy)
        {
            _rescanRequested = true;
            return;
        }

        if (_rescanRequested)
        {
            _rescanRequested = false;
            _ = ScanCurrentAsync();
        }
    }

    private string ResolveExistingPath(string path)
    {
        var candidate = path;
        while (!Directory.Exists(candidate) && !PathsEqual(candidate, RootPath))
        {
            candidate = Directory.GetParent(candidate)?.FullName ?? RootPath;
        }

        return IsWithinRoot(candidate) ? candidate : RootPath;
    }

    private void UpdateEntry(string fullPath, Func<StorageEntry, StorageEntry> update)
    {
        var roots = UpdateEntries(Entries, fullPath, update, out var changed);
        if (!changed)
        {
            return;
        }

        Entries.Reset(roots);
        if (SelectedEntry is not null && PathsEqual(SelectedEntry.FullPath, fullPath))
        {
            SelectedEntry = FindEntry(Entries, fullPath);
        }
    }

    private StorageEntry PreserveExpansionState(StorageEntry updated)
    {
        var existing = FindEntry(Entries, updated.FullPath);
        return existing?.Children is not null
            ? updated with
            {
                Children = existing.Children,
                IsExpanded = existing.IsExpanded,
                IsLoading = existing.IsLoading,
                IsPartial = updated.IsPartial || existing.IsPartial
            }
            : updated;
    }

    private static StorageEntry[] UpdateEntries(
        IEnumerable<StorageEntry> entries,
        string fullPath,
        Func<StorageEntry, StorageEntry> update,
        out bool changed)
    {
        var result = entries.ToArray();
        changed = false;
        for (var index = 0; index < result.Length; index++)
        {
            var entry = result[index];
            if (PathsEqual(entry.FullPath, fullPath))
            {
                result[index] = update(entry);
                changed = true;
                return result;
            }

            if (entry.Children is null)
            {
                continue;
            }

            var children = UpdateEntries(entry.Children, fullPath, update, out var childChanged);
            if (childChanged)
            {
                result[index] = entry with { Children = children };
                changed = true;
                return result;
            }
        }

        return result;
    }

    private static StorageEntry? FindEntry(IEnumerable<StorageEntry> entries, string fullPath)
    {
        foreach (var entry in entries)
        {
            if (PathsEqual(entry.FullPath, fullPath))
            {
                return entry;
            }

            if (entry.Children is not null && FindEntry(entry.Children, fullPath) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private static bool HasLoadingEntry(IEnumerable<StorageEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsLoading || (entry.Children is not null && HasLoadingEntry(entry.Children)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWithinRoot(string path)
    {
        var relative = Path.GetRelativePath(RootPath, path);
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeDebounceTimer.Stop();
        _changeDebounceTimer.Tick -= ChangeDebounceTimer_OnTick;
        StopWatcher();
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _entryScanCancellation?.Cancel();
        _entryScanCancellation?.Dispose();
    }
}
