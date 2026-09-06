using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace ND_Explorer.Views;

public partial class VideoPlayerWindow : Window
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Media _media;
    private readonly DispatcherTimer _positionTimer;
    private bool _isScrubbing;
    private bool _repeat;
    private bool _isFullscreen;
    private bool _disposed;

    public VideoPlayerWindow(string path)
    {
        InitializeComponent();
        Core.Initialize();

        FileNameText.Text = Path.GetFileName(path);
        Title = $"{Path.GetFileName(path)} - ND 동영상 플레이어";

        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        _mediaPlayer = new MediaPlayer(_libVlc) { Volume = 80 };
        _media = new Media(_libVlc, new Uri(path));
        VideoView.MediaPlayer = _mediaPlayer;

        _mediaPlayer.EndReached += MediaPlayer_OnEndReached;
        _mediaPlayer.Playing += MediaPlayer_OnStateChanged;
        _mediaPlayer.Paused += MediaPlayer_OnStateChanged;
        _mediaPlayer.Stopped += MediaPlayer_OnStateChanged;
        _mediaPlayer.EncounteredError += MediaPlayer_OnEncounteredError;

        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, UpdatePosition, Dispatcher);
        Loaded += (_, _) =>
        {
            _positionTimer.Start();
            _mediaPlayer.Play(_media);
        };
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else if (_mediaPlayer.State == VLCState.Ended)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Play(_media);
        }
        else if (!_mediaPlayer.Play())
        {
            _mediaPlayer.Play(_media);
        }
    }

    private void SeekBy(long milliseconds)
    {
        if (!_mediaPlayer.IsSeekable)
        {
            return;
        }

        var maximum = Math.Max(0, _mediaPlayer.Length);
        _mediaPlayer.Time = Math.Clamp(_mediaPlayer.Time + milliseconds, 0, maximum);
    }

    private void ChangeVolume(int delta)
        => VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 100);

    private void UpdatePosition(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var length = Math.Max(0, _mediaPlayer.Length);
        var time = Math.Clamp(_mediaPlayer.Time, 0, length);
        if (!_isScrubbing)
        {
            TimelineSlider.Maximum = Math.Max(1, length);
            TimelineSlider.Value = time;
        }

        CurrentTimeText.Text = FormatTime(time);
        DurationText.Text = FormatTime(length);
        PlayPauseButton.Content = _mediaPlayer.IsPlaying ? "❚❚  일시정지" : "▶  재생";
    }

    private static string FormatTime(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void MediaPlayer_OnEndReached(object? sender, EventArgs e)
    {
        if (!_repeat || _disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && _repeat)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Play(_media);
            }
        });
    }

    private void MediaPlayer_OnStateChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => UpdatePosition(null, EventArgs.Empty));

    private void MediaPlayer_OnEncounteredError(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                MessageBox.Show(
                    this,
                    "이 동영상을 재생하지 못했습니다. 파일이 손상되었거나 지원되지 않는 형식일 수 있습니다.",
                    "재생 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        });
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
        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                break;
            case Key.Left:
                SeekBy(-GetSeekInterval());
                break;
            case Key.Right:
                SeekBy(GetSeekInterval());
                break;
            case Key.Up:
                ChangeVolume(5);
                break;
            case Key.Down:
                ChangeVolume(-5);
                break;
            case Key.Enter:
            case Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape:
                WindowState = WindowState.Minimized;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static long GetSeekInterval()
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return 60_000;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return 30_000;
        }

        return 10_000;
    }

    private void Timeline_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _isScrubbing = true;

    private void Timeline_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isScrubbing = false;
        if (_mediaPlayer.IsSeekable)
        {
            _mediaPlayer.Time = (long)TimelineSlider.Value;
        }
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var volume = (int)Math.Round(e.NewValue);
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{volume}%";
        }
        if (IsLoaded && !_disposed)
        {
            _mediaPlayer.Volume = volume;
        }
    }

    private void Repeat_OnChanged(object sender, RoutedEventArgs e) => _repeat = RepeatButton.IsChecked == true;
    private void Back10_OnClick(object sender, RoutedEventArgs e) => SeekBy(-10_000);
    private void Forward10_OnClick(object sender, RoutedEventArgs e) => SeekBy(10_000);
    private void PlayPause_OnClick(object sender, RoutedEventArgs e) => TogglePlayPause();
    private void Stop_OnClick(object sender, RoutedEventArgs e) => _mediaPlayer.Stop();
    private void Fullscreen_OnClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void VideoSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (!_isFullscreen && WindowState == WindowState.Normal && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionTimer.Stop();
        _mediaPlayer.EndReached -= MediaPlayer_OnEndReached;
        _mediaPlayer.Playing -= MediaPlayer_OnStateChanged;
        _mediaPlayer.Paused -= MediaPlayer_OnStateChanged;
        _mediaPlayer.Stopped -= MediaPlayer_OnStateChanged;
        _mediaPlayer.EncounteredError -= MediaPlayer_OnEncounteredError;
        VideoView.MediaPlayer = null;
        _mediaPlayer.Stop();
        _media.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
