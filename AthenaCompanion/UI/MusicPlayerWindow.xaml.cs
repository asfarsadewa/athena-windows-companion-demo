using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AthenaCompanion.Music;

namespace AthenaCompanion.UI;

internal partial class MusicPlayerWindow : Window
{
    private readonly string _musicDirectory;
    private readonly MusicPlaybackEngine _engine = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _libraryLoadCancellation;
    private IReadOnlyList<MusicTrack> _tracks = [];
    private long _libraryLoadVersion;
    private bool _loading;
    private bool _seeking;
    private bool _updatingProgress;

    public MusicPlayerWindow(string musicDirectory, MusicPlayerRequest? initialRequest = null)
    {
        _musicDirectory = musicDirectory;
        InitializeComponent();
        _engine.PlaybackStopped += OnPlaybackStopped;
        _progressTimer.Tick += OnProgressTick;
        _progressTimer.Start();
        StartLibraryLoad(initialRequest);
    }

    public void ApplyRequest(MusicPlayerRequest request) => StartLibraryLoad(request);

    protected override void OnClosed(EventArgs e)
    {
        _libraryLoadCancellation?.Cancel();
        _libraryLoadCancellation = null;
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTick;
        _engine.PlaybackStopped -= OnPlaybackStopped;
        _engine.Dispose();
        base.OnClosed(e);
    }

    private void StartLibraryLoad(MusicPlayerRequest? request = null)
    {
        var selected = TracksList.SelectedItem as MusicTrack;
        var loadVersion = ++_libraryLoadVersion;
        _libraryLoadCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _libraryLoadCancellation = cancellation;
        _loading = true;
        SetStatus("Scanning library...");
        EmptyState.Visibility = Visibility.Collapsed;
        TracksList.Visibility = _tracks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        _ = LoadLibraryAsync(loadVersion, selected, request, cancellation);
    }

    private async Task LoadLibraryAsync(
        long loadVersion,
        MusicTrack? selected,
        MusicPlayerRequest? request,
        CancellationTokenSource cancellation)
    {
        try
        {
            var token = cancellation.Token;
            var snapshot = await Task.Run(() => MusicLibrary.Load(_musicDirectory, token), token);
            if (!IsCurrentLibraryLoad(loadVersion, cancellation))
            {
                return;
            }

            ApplyLibrarySnapshot(snapshot, selected, request);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!IsCurrentLibraryLoad(loadVersion, cancellation))
            {
                return;
            }

            _tracks = [];
            TracksList.ItemsSource = _tracks;
            TracksList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateText.Text = $"Could not scan {_musicDirectory}.";
            SetStatus($"Library scan failed: {ex.Message}");
        }
        finally
        {
            if (IsCurrentLibraryLoad(loadVersion, cancellation))
            {
                _loading = false;
                _libraryLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentLibraryLoad(long loadVersion, CancellationTokenSource cancellation) =>
        loadVersion == _libraryLoadVersion && ReferenceEquals(_libraryLoadCancellation, cancellation);

    private void ApplyLibrarySnapshot(MusicLibrarySnapshot snapshot, MusicTrack? selected, MusicPlayerRequest? request)
    {
        _tracks = snapshot.Tracks;
        TracksList.ItemsSource = _tracks;
        EmptyState.Visibility = snapshot.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        TracksList.Visibility = snapshot.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateText.Text = MusicLibraryMessages.Empty(snapshot.DirectoryPath);

        if (snapshot.IsEmpty)
        {
            SetStatus(MusicLibraryMessages.Empty(snapshot.DirectoryPath));
            return;
        }

        if (request is not null)
        {
            ApplyRequestToLoadedLibrary(snapshot, request);
            return;
        }

        var selectedIndex = selected is null ? 0 : IndexOfTrack(selected);
        SelectTrack(Math.Max(0, selectedIndex));
        SetStatus($"Library ready ({FormatTrackCount(_tracks.Count)})");
    }

    private void ApplyRequestToLoadedLibrary(MusicLibrarySnapshot snapshot, MusicPlayerRequest request)
    {
        var track = snapshot.FindBestMatch(request.Query);
        if (track is null)
        {
            SetStatus(MusicLibraryMessages.NoMatch(request.Query));
            SelectTrack(0);
            return;
        }

        var trackIndex = IndexOfTrack(track);
        SelectTrack(trackIndex);
        if (request.Autoplay)
        {
            TryPlayFromIndex(trackIndex, direction: 1);
        }
        else
        {
            SetStatus($"Ready: {track.DisplayName}");
        }
    }

    private void SelectTrack(int index)
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _tracks.Count - 1);
        TracksList.SelectedItem = _tracks[index];
        TracksList.ScrollIntoView(_tracks[index]);
    }

    private int IndexOfTrack(MusicTrack track)
    {
        for (var i = 0; i < _tracks.Count; i++)
        {
            if (string.Equals(_tracks[i].FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private bool TryPlayFromIndex(int startIndex, int direction)
    {
        if (_tracks.Count == 0)
        {
            SetStatus(_loading ? "Scanning library..." : MusicLibraryMessages.Empty(_musicDirectory));
            return false;
        }

        startIndex = Math.Clamp(startIndex, 0, _tracks.Count - 1);
        direction = direction < 0 ? -1 : 1;
        string? lastError = null;

        for (var attempt = 0; attempt < _tracks.Count; attempt++)
        {
            var index = (startIndex + attempt * direction) % _tracks.Count;
            if (index < 0)
            {
                index += _tracks.Count;
            }

            var track = _tracks[index];
            try
            {
                _engine.Play(track.FilePath);
                SelectTrack(index);
                PlayPauseButton.Content = "Pause";
                SetStatus($"Tuned: {track.DisplayName}");
                UpdateProgress();
                return true;
            }
            catch (Exception ex)
            {
                lastError = $"Skipped unsupported file: {track.RelativePath} ({ex.Message})";
            }
        }

        PlayPauseButton.Content = "Play";
        SetStatus(lastError ?? "No playable tracks.");
        return false;
    }

    private void OnPlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_tracks.Count == 0 || TracksList.SelectedIndex < 0)
            {
                PlayPauseButton.Content = "Play";
                return;
            }

            TryPlayFromIndex((TracksList.SelectedIndex + 1) % _tracks.Count, direction: 1);
        });
    }

    private void OnProgressTick(object? sender, EventArgs e) => UpdateProgress();

    private void UpdateProgress()
    {
        var duration = _engine.Duration;
        var position = _engine.Position;
        _updatingProgress = true;
        try
        {
            SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            if (!_seeking)
            {
                SeekSlider.Value = Math.Clamp(position.TotalSeconds, 0, SeekSlider.Maximum);
            }

            PositionText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
        }
        finally
        {
            _updatingProgress = false;
        }

        PlayPauseButton.Content = _engine.IsPlaying ? "Pause" : "Play";
    }

    private void SetStatus(string status) => StatusText.Text = status;

    private static string FormatTrackCount(int count) => count == 1 ? "1 track" : $"{count:N0} tracks";

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TracksList.SelectedItem is not MusicTrack track)
        {
            return;
        }

        if (!_engine.IsPlaying)
        {
            SetStatus($"Ready: {track.DisplayName}");
        }
    }

    private void OnTrackDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TracksList.SelectedIndex >= 0)
        {
            TryPlayFromIndex(TracksList.SelectedIndex, direction: 1);
        }
    }

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        var index = TracksList.SelectedIndex <= 0 ? _tracks.Count - 1 : TracksList.SelectedIndex - 1;
        TryPlayFromIndex(index, direction: -1);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_engine.IsPlaying)
        {
            _engine.Pause();
            PlayPauseButton.Content = "Play";
            SetStatus("Paused");
            return;
        }

        if (_engine.IsPaused)
        {
            _engine.Resume();
            PlayPauseButton.Content = "Pause";
            if (TracksList.SelectedItem is MusicTrack current)
            {
                SetStatus($"Tuned: {current.DisplayName}");
            }

            return;
        }

        TryPlayFromIndex(Math.Max(0, TracksList.SelectedIndex), direction: 1);
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        var index = TracksList.SelectedIndex < 0 ? 0 : (TracksList.SelectedIndex + 1) % _tracks.Count;
        TryPlayFromIndex(index, direction: 1);
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _engine.Stop();
        PlayPauseButton.Content = "Play";
        UpdateProgress();
        SetStatus("Stopped");
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_musicDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _musicDirectory,
            UseShellExecute = true
        });
        StartLibraryLoad();
    }

    private void OnSeekStarted(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void OnSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        _engine.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
        UpdateProgress();
    }

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingProgress || _seeking || !_engine.HasTrack)
        {
            return;
        }

        _engine.Seek(TimeSpan.FromSeconds(SeekSlider.Value));
        UpdateProgress();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
