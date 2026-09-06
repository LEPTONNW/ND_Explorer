using System.Diagnostics;
using System.IO;
using System.Windows;
using ND_Explorer.Controls;
using ND_Explorer.Models;
using ND_Explorer.Services;
using ND_Explorer.ViewModels;

namespace ND_Explorer.Views;

public partial class StorageAnalyzerWindow : Window
{
    private readonly StorageAnalyzerViewModel _viewModel;

    public StorageAnalyzerWindow()
    {
        _viewModel = new StorageAnalyzerViewModel(new StorageScanService());
        DataContext = _viewModel;
        InitializeComponent();
    }

    private async void Treemap_OnEntryInvoked(object? sender, StorageEntry entry)
    {
        if (sender is StorageTreemapControl { DataContext: DriveAnalysisViewModel drive })
        {
            await drive.OpenEntryAsync(entry);
        }
    }

    private async void Treemap_OnOpenInNdExplorerRequested(object? sender, StorageEntry entry)
    {
        if (sender is not StorageTreemapControl { DataContext: DriveAnalysisViewModel drive })
        {
            return;
        }

        if (!Directory.Exists(entry.FullPath) && !File.Exists(entry.FullPath))
        {
            drive.ReportStatus("선택한 항목이 이동되었거나 삭제되어 ND Explorer에서 열 수 없습니다.");
            return;
        }

        if (Owner is not MainWindow mainWindow)
        {
            drive.ReportStatus("ND Explorer 메인 창을 찾을 수 없습니다.");
            return;
        }

        WindowState = WindowState.Minimized;
        await mainWindow.RevealPathAsync(entry.FullPath);
    }

    private void Treemap_OnOpenInWindowsExplorerRequested(object? sender, StorageEntry entry)
    {
        if (sender is not StorageTreemapControl { DataContext: DriveAnalysisViewModel drive })
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };

            if (!entry.IsDirectory && File.Exists(entry.FullPath))
            {
                startInfo.ArgumentList.Add("/select,");
                startInfo.ArgumentList.Add(entry.FullPath);
            }
            else
            {
                var directory = FindExistingDirectory(entry);
                if (directory is null)
                {
                    drive.ReportStatus("선택한 항목과 상위 폴더가 이동되었거나 삭제되어 Windows 탐색기에서 열 수 없습니다.");
                    return;
                }

                startInfo.ArgumentList.Add(directory);
                if (!Directory.Exists(entry.FullPath))
                {
                    drive.ReportStatus("선택한 항목은 사라졌지만 가장 가까운 상위 폴더를 Windows 탐색기에서 열었습니다.");
                }
            }

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            drive.ReportStatus($"Windows 탐색기에서 위치를 열 수 없습니다: {exception.Message}");
        }
    }

    private static string? FindExistingDirectory(StorageEntry entry)
    {
        var candidate = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath);
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Directory.GetParent(candidate)?.FullName;
        }

        return null;
    }

    private void Window_OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}
