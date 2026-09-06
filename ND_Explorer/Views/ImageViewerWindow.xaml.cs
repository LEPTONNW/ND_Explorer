using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ND_Explorer.Views;

public partial class ImageViewerWindow : Window
{
    private const double MinimumScale = 0.05;
    private const double MaximumScale = 16;

    private readonly BitmapSource _source;
    private double _scale = 1;
    private int _rotation;
    private bool _isFullscreen;
    private bool _isFitMode = true;
    private bool _fitUpdatePending;
    private bool _isDragging;
    private Point _dragStart;
    private double _dragStartHorizontalOffset;
    private double _dragStartVerticalOffset;

    public ImageViewerWindow(string path)
    {
        InitializeComponent();
        _source = LoadImage(path);
        DisplayedImage.Source = _source;
        FileNameText.Text = Path.GetFileName(path);
        Title = $"{Path.GetFileName(path)} - ND 이미지 뷰어";
        ImageInfoText.Text = $"{_source.PixelWidth:N0} × {_source.PixelHeight:N0}  ·  {GetFileSize(path)}";
        Loaded += (_, _) => ZoomToFit();
    }

    private static BitmapSource LoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string GetFileSize(string path)
    {
        var length = new FileInfo(path).Length;
        string[] suffixes = ["B", "KB", "MB", "GB"];
        double value = length;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0 ? $"{length:N0} B" : $"{value:N1} {suffixes[index]}";
    }

    private void SetScale(double value, bool keepFitMode = false)
    {
        if (!keepFitMode)
        {
            _isFitMode = false;
        }

        _scale = Math.Clamp(value, MinimumScale, MaximumScale);
        ImageScale.ScaleX = _scale;
        ImageScale.ScaleY = _scale;
        ZoomText.Text = $"{_scale:P0}";
    }

    private void ZoomAt(double value, Point viewportPoint)
    {
        var previousScale = _scale;
        var contentX = Viewer.HorizontalOffset + viewportPoint.X;
        var contentY = Viewer.VerticalOffset + viewportPoint.Y;

        SetScale(value);
        var scaleRatio = _scale / previousScale;

        Dispatcher.BeginInvoke(() =>
        {
            Viewer.ScrollToHorizontalOffset((contentX * scaleRatio) - viewportPoint.X);
            Viewer.ScrollToVerticalOffset((contentY * scaleRatio) - viewportPoint.Y);
            UpdatePanCursor();
        });
    }

    private void ZoomAtCenter(double value) =>
        ZoomAt(value, new Point(Viewer.ViewportWidth / 2, Viewer.ViewportHeight / 2));

    private void ZoomToFit()
    {
        _isFitMode = true;
        UpdateFitScale();
    }

    private void UpdateFitScale()
    {
        var availableWidth = Math.Max(100, Viewer.ViewportWidth - 32);
        var availableHeight = Math.Max(100, Viewer.ViewportHeight - 32);
        var rotated = Math.Abs(_rotation % 180) == 90;
        var imageWidth = rotated ? _source.Height : _source.Width;
        var imageHeight = rotated ? _source.Width : _source.Height;
        SetScale(Math.Min(1, Math.Min(availableWidth / imageWidth, availableHeight / imageHeight)), keepFitMode: true);
        Viewer.ScrollToHorizontalOffset(0);
        Viewer.ScrollToVerticalOffset(0);
        Dispatcher.BeginInvoke(UpdatePanCursor);
    }

    private void QueueFitUpdate()
    {
        if (!_isFitMode || _fitUpdatePending || !IsLoaded)
        {
            return;
        }

        _fitUpdatePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitUpdatePending = false;
            if (_isFitMode)
            {
                UpdateFitScale();
            }
        });
    }

    private bool CanPan => Viewer.ScrollableWidth > 0 || Viewer.ScrollableHeight > 0;

    private void UpdatePanCursor()
    {
        if (!_isDragging)
        {
            Viewer.Cursor = CanPan ? Cursors.Hand : Cursors.Arrow;
        }
    }

    private void Rotate(int delta)
    {
        _rotation = (_rotation + delta + 360) % 360;
        ImageRotation.Angle = _rotation;
        ZoomToFit();
    }

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Add or Key.OemPlus)
        {
            ZoomAtCenter(_scale * 1.2);
            e.Handled = true;
        }
        else if (e.Key is Key.Subtract or Key.OemMinus)
        {
            ZoomAtCenter(_scale / 1.2);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D0)
        {
            ZoomToFit();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueFitUpdate();

    private void Viewer_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.Delta > 0 ? _scale * 1.12 : _scale / 1.12, e.GetPosition(Viewer));
        e.Handled = true;
    }

    private void Viewer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || !CanPan)
        {
            return;
        }

        _isDragging = true;
        _dragStart = e.GetPosition(Viewer);
        _dragStartHorizontalOffset = Viewer.HorizontalOffset;
        _dragStartVerticalOffset = Viewer.VerticalOffset;
        Viewer.Cursor = Cursors.SizeAll;
        Viewer.CaptureMouse();
        e.Handled = true;
    }

    private void Viewer_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(Viewer);
        Viewer.ScrollToHorizontalOffset(_dragStartHorizontalOffset - (current.X - _dragStart.X));
        Viewer.ScrollToVerticalOffset(_dragStartVerticalOffset - (current.Y - _dragStart.Y));
        e.Handled = true;
    }

    private void Viewer_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        EndDragging();
        e.Handled = true;
    }

    private void Viewer_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            EndDragging();
        }
    }

    private void EndDragging()
    {
        _isDragging = false;
        if (Viewer.IsMouseCaptured)
        {
            Viewer.ReleaseMouseCapture();
        }

        UpdatePanCursor();
    }

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => ZoomAtCenter(_scale / 1.2);
    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => ZoomAtCenter(_scale * 1.2);
    private void Fit_OnClick(object sender, RoutedEventArgs e) => ZoomToFit();
    private void RotateLeft_OnClick(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRight_OnClick(object sender, RoutedEventArgs e) => Rotate(90);
    private void Fullscreen_OnClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Image_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }
}
