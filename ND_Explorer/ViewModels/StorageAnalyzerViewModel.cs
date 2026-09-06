using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using ND_Explorer.Infrastructure;
using ND_Explorer.Services;

namespace ND_Explorer.ViewModels;

public sealed class StorageAnalyzerViewModel : ObservableObject, IDisposable
{
    private readonly IStorageScanService _scanService;
    private readonly DispatcherTimer _driveRefreshTimer;
    private DriveAnalysisViewModel? _selectedDrive;
    private bool _disposed;

    public StorageAnalyzerViewModel(IStorageScanService scanService)
    {
        _scanService = scanService;
        Drives = new ObservableCollection<DriveAnalysisViewModel>();
        _driveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _driveRefreshTimer.Tick += (_, _) => RefreshDrives();
        RefreshDrives();
        _driveRefreshTimer.Start();
    }

    public ObservableCollection<DriveAnalysisViewModel> Drives { get; }

    public DriveAnalysisViewModel? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (!SetProperty(ref _selectedDrive, value))
            {
                return;
            }

            foreach (var drive in Drives)
            {
                if (!ReferenceEquals(drive, value))
                {
                    drive.Deactivate();
                }
            }

            if (value is not null)
            {
                _ = value.ActivateAsync();
            }
        }
    }

    private void RefreshDrives()
    {
        if (_disposed)
        {
            return;
        }

        DriveInfo[] detected;
        try
        {
            detected = DriveInfo.GetDrives();
        }
        catch
        {
            return;
        }

        var roots = detected
            .Select(drive => drive.RootDirectory.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removed in Drives.Where(item => !roots.Contains(item.RootPath)).ToArray())
        {
            var wasSelected = ReferenceEquals(removed, SelectedDrive);
            Drives.Remove(removed);
            removed.Dispose();
            if (wasSelected)
            {
                _selectedDrive = null;
                OnPropertyChanged(nameof(SelectedDrive));
            }
        }

        foreach (var drive in detected.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var existing = Drives.FirstOrDefault(item => string.Equals(item.RootPath, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Drives.Add(new DriveAnalysisViewModel(drive, _scanService));
            }
            else
            {
                existing.UpdateDriveInfo(drive);
            }
        }

        SelectedDrive ??= Drives.FirstOrDefault();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driveRefreshTimer.Stop();
        foreach (var drive in Drives)
        {
            drive.Dispose();
        }
    }
}
