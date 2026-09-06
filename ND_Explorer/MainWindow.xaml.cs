using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using ND_Explorer.Models;
using ND_Explorer.Services;
using ND_Explorer.ViewModels;
using ND_Explorer.Views;

namespace ND_Explorer;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private StorageAnalyzerWindow? _storageAnalyzerWindow;
    private bool _initialized;
    private Point _dragStartPoint;
    private string _typeSearchText = string.Empty;
    private string _typeSearchPath = string.Empty;
    private DateTime _lastTypeSearchUtc = DateTime.MinValue;

    private static readonly TimeSpan TypeSearchTimeout = TimeSpan.FromSeconds(1);

    public MainWindow(MainWindowViewModel viewModel, ISettingsService settingsService)
    {
        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = viewModel;
        InitializeComponent();
        RestoreWindowPlacement();
        SourceInitialized += ApplyDarkTitleBar;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.InitializeAsync();
        FileGrid.Focus();
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        SaveWindowPlacement();
        _viewModel.Dispose();
    }

    private void FileGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: not null } grid
            && FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not null
            && _viewModel.Explorer.OpenItemCommand.CanExecute(grid.SelectedItem))
        {
            _viewModel.Explorer.OpenItemCommand.Execute(grid.SelectedItem);
        }
    }

    private void FileGrid_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileGrid.SelectedItem is not null
            && _viewModel.Explorer.OpenItemCommand.CanExecute(FileGrid.SelectedItem))
        {
            _viewModel.Explorer.OpenItemCommand.Execute(FileGrid.SelectedItem);
            e.Handled = true;
        }
    }

    private void FileGrid_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastTypeSearchUtc > TypeSearchTimeout
            || !string.Equals(_typeSearchPath, _viewModel.Explorer.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            _typeSearchText = string.Empty;
        }

        var repeatedSingleCharacter = _typeSearchText.Length == 1
                                      && string.Equals(_typeSearchText, e.Text, StringComparison.CurrentCultureIgnoreCase);
        _typeSearchText = repeatedSingleCharacter ? e.Text : _typeSearchText + e.Text;
        _typeSearchPath = _viewModel.Explorer.CurrentPath;
        _lastTypeSearchUtc = now;

        var startIndex = repeatedSingleCharacter ? FileGrid.SelectedIndex + 1 : 0;
        var match = FindTypeSearchMatch(_typeSearchText, startIndex);

        if (match is null && _typeSearchText.Length > e.Text.Length)
        {
            _typeSearchText = e.Text;
            match = FindTypeSearchMatch(_typeSearchText, FileGrid.SelectedIndex + 1);
        }

        if (match is not null)
        {
            FileGrid.SelectedItems.Clear();
            FileGrid.SelectedItem = match;
            FileGrid.ScrollIntoView(match);
        }

        e.Handled = true;
    }

    private FileSystemItem? FindTypeSearchMatch(string searchText, int startIndex)
    {
        if (FileGrid.Items.Count == 0)
        {
            return null;
        }

        var normalizedStartIndex = Math.Clamp(startIndex, 0, FileGrid.Items.Count);
        for (var offset = 0; offset < FileGrid.Items.Count; offset++)
        {
            var index = (normalizedStartIndex + offset) % FileGrid.Items.Count;
            if (FileGrid.Items[index] is FileSystemItem item
                && item.Name.StartsWith(searchText, StringComparison.CurrentCultureIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private void FileGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            _viewModel.Explorer.SetSelectedItems(grid.SelectedItems.Cast<FileSystemItem>());
        }
    }

    private void FileGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null && !row.IsSelected)
        {
            FileGrid.SelectedItems.Clear();
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void FileGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(FileGrid);

    private void FileGrid_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || FileGrid.SelectedItems.Count == 0)
        {
            return;
        }

        var currentPosition = e.GetPosition(FileGrid);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var paths = FileGrid.SelectedItems
            .Cast<FileSystemItem>()
            .Select(item => item.FullPath)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(FileGrid, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void FileGrid_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void FileGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        var destination = row?.Item is FileSystemItem { IsDirectory: true } directory
            ? directory.FullPath
            : _viewModel.Explorer.CurrentPath;
        var move = e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey);

        e.Handled = true;
        await _viewModel.Explorer.ImportDroppedItemsAsync(paths, destination, move);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && AddressBox.IsKeyboardFocusWithin)
        {
            _viewModel.Explorer.AddressText = _viewModel.Explorer.CurrentPath;
            FileGrid.Focus();
            e.Handled = true;
        }
        else if (IsTextInputFocused())
        {
            return;
        }
        else if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExecuteIfAvailable(_viewModel.Explorer.BackCommand);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            ExecuteIfAvailable(_viewModel.Explorer.NewFolderCommand);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            ExecuteIfAvailable(_viewModel.Explorer.CopyCommand);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
        {
            ExecuteIfAvailable(_viewModel.Explorer.CutCommand);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            ExecuteIfAvailable(_viewModel.Explorer.PasteCommand);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExecuteIfAvailable(_viewModel.Explorer.RenameCommand);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ExecuteIfAvailable(_viewModel.Explorer.PermanentDeleteCommand);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExecuteIfAvailable(_viewModel.Explorer.DeleteCommand);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExecuteIfAvailable(_viewModel.Explorer.CancelOperationCommand);
            e.Handled = true;
        }
    }

    private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var navigationCommand = e.ChangedButton switch
        {
            MouseButton.XButton1 => _viewModel.Explorer.BackCommand,
            MouseButton.XButton2 => _viewModel.Explorer.ForwardCommand,
            _ => null,
        };

        if (navigationCommand is not null && navigationCommand.CanExecute(null))
        {
            navigationCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsTextInputFocused()
        => Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;

    private static void ExecuteIfAvailable(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T result)
            {
                return result;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ApplyDarkTitleBar(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    private void RestoreWindowPlacement()
    {
        var settings = _settingsService.Current;
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

        if (settings.WindowLeft is double left
            && settings.WindowTop is double top
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80
            && left + Width > SystemParameters.VirtualScreenLeft + 80
            && top + Height > SystemParameters.VirtualScreenTop + 80)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowPlacement()
    {
        var bounds = RestoreBounds;
        var settings = _settingsService.Current;
        settings.WindowWidth = Math.Max(MinWidth, bounds.Width);
        settings.WindowHeight = Math.Max(MinHeight, bounds.Height);
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.IsMaximized = WindowState == WindowState.Maximized;
        _settingsService.Save();
    }

    private void OpenSourceNotices_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new OpenSourceNoticesWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenStorageAnalyzer_OnClick(object sender, RoutedEventArgs e)
    {
        if (_storageAnalyzerWindow is not null)
        {
            if (_storageAnalyzerWindow.WindowState == WindowState.Minimized)
            {
                _storageAnalyzerWindow.WindowState = WindowState.Normal;
            }

            _storageAnalyzerWindow.Activate();
            return;
        }

        _storageAnalyzerWindow = new StorageAnalyzerWindow { Owner = this };
        _storageAnalyzerWindow.Closed += (_, _) => _storageAnalyzerWindow = null;
        _storageAnalyzerWindow.Show();
    }

    public async Task RevealPathAsync(string path)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        var selectedItem = await _viewModel.Explorer.RevealPathAsync(path);
        if (selectedItem is not null)
        {
            FileGrid.UpdateLayout();
            FileGrid.ScrollIntoView(selectedItem);
            FileGrid.Focus();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
}
