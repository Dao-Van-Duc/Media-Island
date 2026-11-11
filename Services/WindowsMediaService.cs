using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandMedia.Helpers;
using DynamicIslandMedia.Models;
using Windows.Media.Control;

namespace DynamicIslandMedia.Services
{
    public class WindowsMediaService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private readonly DispatcherTimer _timelineTimer;
        private readonly DispatcherTimer _sessionCheckTimer;
        private const int PausedSwitchDelayMs = 2000;
        private const int SessionCheckIntervalMs = 500;
        private long? _pausedAtTimestamp;
        private bool _checkingSessions;
        private bool _disposed;
        // UI smoothing: 40ms ~ 25 updates/sec; 33ms ~ 30; 16ms ~ 60.
        // This only refreshes the local display and does NOT poll Windows media 25 times/sec.
        private const int ProgressUiIntervalMs = 40;
        private TimeSpan _timelineAnchorPosition = TimeSpan.Zero;
        private long _timelineAnchorTimestamp = Stopwatch.GetTimestamp();

        private void ResetTimelineAnchor(TimeSpan position)
        {
            _timelineAnchorPosition = position;
            _timelineAnchorTimestamp = Stopwatch.GetTimestamp();
        }

        public event EventHandler<MediaPlaybackInfo>? MediaChanged;
        public event EventHandler<TimeSpan>? TimelineUpdated;

        public MediaPlaybackInfo CurrentMedia { get; private set; } = new();

        public WindowsMediaService()
        {
            _timelineTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ProgressUiIntervalMs)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            // Session selection is deliberately independent from the 40ms UI progress timer.
            _sessionCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SessionCheckIntervalMs) };
            _sessionCheckTimer.Tick += OnSessionCheckTick;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    _sessionManager.SessionsChanged += OnSessionsChanged;
                    await UpdateCurrentSessionAsync(GetBestSession(_sessionManager));
                    _sessionCheckTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] Initialization error: {ex.Message}");
            }
        }

        private static bool IsPlayingSession(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                return session.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch { return false; }
        }

        private static bool IsClosedSession(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var status = session.GetPlaybackInfo()?.PlaybackStatus;
                return status == null || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
            }
            catch { return true; }
        }

        private GlobalSystemMediaTransportControlsSession? GetBestSession(GlobalSystemMediaTransportControlsSessionManager? manager)
        {
            if (manager == null) return null;
            try
            {
                var sessions = manager.GetSessions();
                var current = manager.GetCurrentSession();
                // Sticky selection: a playing selection is never displaced by a new session.
                if (_currentSession != null && !IsClosedSession(_currentSession))
                    return _currentSession;
                // Initial selection: prefer an active player, otherwise Windows' current session.
                foreach (var session in sessions)
                    if (IsPlayingSession(session)) return session;
                if (current != null && !IsClosedSession(current)) return current;
                foreach (var session in sessions)
                    if (!IsClosedSession(session)) return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] GetBestSession error: {ex.Message}");
            }
            return null;
        }

        // Keep the currently selected session during a short pause, then switch only
        // when another session is actually playing. No fallback means stay paused.
        private async void OnSessionCheckTick(object? sender, EventArgs e)
        {
            if (_disposed || _checkingSessions || _sessionManager == null) return;
            _checkingSessions = true;
            try
            {
                var selected = _currentSession;
                if (selected == null || IsClosedSession(selected))
                {
                    _pausedAtTimestamp = null;
                    var replacement = GetBestSession(_sessionManager);
                    if (!ReferenceEquals(selected, replacement))
                        await UpdateCurrentSessionAsync(replacement);
                    return;
                }

                if (IsPlayingSession(selected))
                {
                    _pausedAtTimestamp = null;
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                _pausedAtTimestamp ??= now;
                double pausedMs = (now - _pausedAtTimestamp.Value) * 1000.0 / Stopwatch.Frequency;
                if (pausedMs < PausedSwitchDelayMs) return;

                foreach (var candidate in _sessionManager.GetSessions())
                {
                    if (ReferenceEquals(candidate, selected) || !IsPlayingSession(candidate)) continue;
                    _pausedAtTimestamp = null;
                    await UpdateCurrentSessionAsync(candidate);
                    return;
                }
                // Preserve the current paused session if there is nothing playing.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] Session check error: {ex.Message}");
            }
            finally { _checkingSessions = false; }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            try
            {
                // Never steal focus from a playing session; honor the 2-second pause grace period.
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnSessionCheckTick(null, EventArgs.Empty)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] CurrentSessionChanged error: {ex.Message}");
            }
        }

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            try
            {
                // Never steal focus from a playing session; honor the 2-second pause grace period.
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnSessionCheckTick(null, EventArgs.Empty)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] SessionsChanged error: {ex.Message}");
            }
        }

        private async Task UpdateCurrentSessionAsync(GlobalSystemMediaTransportControlsSession? newSession)
        {
            await _updateLock.WaitAsync();
            try
            {
                if (_currentSession != null)
                {
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }

                _currentSession = newSession;
                _pausedAtTimestamp = null;

                if (_currentSession != null)
                {
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
                else
                {
                    _timelineTimer.Stop();
                    CurrentMedia = new MediaPlaybackInfo { HasActiveSession = false };
                    MediaChanged?.Invoke(this, CurrentMedia);
                }
            }
            finally
            {
                _updateLock.Release();
            }
            // Never refresh while holding _updateLock: RefreshMediaAsync may
            // need to switch sessions when a provider closes unexpectedly.
            if (!_disposed && ReferenceEquals(_currentSession, newSession) && newSession != null)
                await RefreshMediaAsync();
        }

        private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            if (!ReferenceEquals(sender, _currentSession)) return;
            try { await RefreshMediaAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[WindowsMediaService] Playback event: {ex.Message}"); }
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            if (!ReferenceEquals(sender, _currentSession)) return;
            try { await RefreshMediaAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[WindowsMediaService] Media event: {ex.Message}"); }
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            if (ReferenceEquals(sender, _currentSession)) SyncTimeline();
        }

        public async Task RefreshMediaAsync()
        {
            var session = _currentSession;
            if (session == null)
            {
                CurrentMedia = new MediaPlaybackInfo { HasActiveSession = false };
                MediaChanged?.Invoke(this, CurrentMedia);
                return;
            }

            try
            {
                var playbackInfo = session.GetPlaybackInfo();
                var mediaProps = await session.TryGetMediaPropertiesAsync();
                if (_disposed || !ReferenceEquals(session, _currentSession)) return;
                var timeline = session.GetTimelineProperties();

                // If session is closed, check for other active sessions
                if (playbackInfo == null || playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                {
                    if (_sessionManager != null)
                    {
                        var altSession = GetBestSession(_sessionManager);
                        if (altSession != null && !ReferenceEquals(altSession, session))
                        {
                            await UpdateCurrentSessionAsync(altSession);
                            return;
                        }
                    }

                    _timelineTimer.Stop();
                    CurrentMedia = new MediaPlaybackInfo { HasActiveSession = false };
                    MediaChanged?.Invoke(this, CurrentMedia);
                    return;
                }

                var isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                // Record the pause as soon as Windows reports it, rather than waiting
                // for the next 500ms selection poll. Keep the timestamp if still paused.
                if (isPlaying) _pausedAtTimestamp = null;
                else _pausedAtTimestamp ??= Stopwatch.GetTimestamp();

                var info = new MediaPlaybackInfo
                {
                    HasActiveSession = true,
                    Title = mediaProps?.Title ?? string.Empty,
                    Artist = mediaProps?.Artist ?? string.Empty,
                    AlbumTitle = mediaProps?.AlbumTitle ?? string.Empty,
                    SourceAppId = session.SourceAppUserModelId ?? string.Empty,
                    IsPlaying = isPlaying,
                    PlaybackStatus = playbackInfo.PlaybackStatus,
                    Position = timeline?.Position ?? TimeSpan.Zero,
                    Duration = timeline != null && timeline.EndTime > timeline.StartTime
                        ? timeline.EndTime - timeline.StartTime
                        : TimeSpan.Zero
                };

                // Read album artwork
                if (mediaProps?.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await mediaProps.Thumbnail.OpenReadAsync();
                        if (stream != null && stream.Size > 0)
                        {
                            using var netStream = stream.AsStreamForRead();
                            using var memoryStream = new MemoryStream();
                            await netStream.CopyToAsync(memoryStream);
                            memoryStream.Position = 0;

                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = memoryStream;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            info.AlbumArt = bitmap;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WindowsMediaService] Thumbnail load error: {ex.Message}");
                    }
                }

                if (info.AlbumArt == null)
                {
                    info.AlbumArt = IconHelper.GetFallbackAlbumArt();
                }

                var (c1, c2) = AlbumColorExtractor.ExtractColors(info.AlbumArt);
                info.PrimaryColor = c1;
                info.SecondaryColor = c2;

                if (_disposed || !ReferenceEquals(session, _currentSession)) return;
                CurrentMedia = info;
                ResetTimelineAnchor(info.Position);

                if (isPlaying)
                {
                    if (!_timelineTimer.IsEnabled)
                        _timelineTimer.Start();
                }
                else
                {
                    _timelineTimer.Stop();
                }

                MediaChanged?.Invoke(this, CurrentMedia);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] RefreshMedia error: {ex.Message}");
            }
        }

        private void SyncTimeline()
        {
            if (_currentSession == null) return;

            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null)
                {
                    CurrentMedia.Position = timeline.Position;
                    ResetTimelineAnchor(CurrentMedia.Position);
                    if (timeline.EndTime > timeline.StartTime)
                    {
                        CurrentMedia.Duration = timeline.EndTime - timeline.StartTime;
                    }
                    TimelineUpdated?.Invoke(this, CurrentMedia.Position);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] SyncTimeline error: {ex.Message}");
            }
        }

        private void OnTimelineTimerTick(object? sender, EventArgs e)
        {
            if (!CurrentMedia.IsPlaying || CurrentMedia.Duration <= TimeSpan.Zero)
                return;

            var elapsedSeconds = (Stopwatch.GetTimestamp() - _timelineAnchorTimestamp)
                                 / (double)Stopwatch.Frequency;
            var target = _timelineAnchorPosition + TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
            CurrentMedia.Position = target > CurrentMedia.Duration ? CurrentMedia.Duration : target;
            TimelineUpdated?.Invoke(this, CurrentMedia.Position);
        }

        public async Task TogglePlayPauseAsync()
        {
            if (_currentSession == null) return;
            try
            {
                var info = _currentSession.GetPlaybackInfo();
                if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    await _currentSession.TryPauseAsync();
                }
                else
                {
                    await _currentSession.TryPlayAsync();
                }
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] TogglePlayPause error: {ex.Message}");
            }
        }

        public async Task SkipNextAsync()
        {
            if (_currentSession == null) return;
            try
            {
                await _currentSession.TrySkipNextAsync();
                await Task.Delay(200);
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] SkipNext error: {ex.Message}");
            }
        }

        public async Task SkipPreviousAsync()
        {
            if (_currentSession == null) return;
            try
            {
                await _currentSession.TrySkipPreviousAsync();
                await Task.Delay(200);
                await RefreshMediaAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] SkipPrevious error: {ex.Message}");
            }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            if (_currentSession == null) return;
            try
            {
                await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
                CurrentMedia.Position = position;
                ResetTimelineAnchor(position);
                TimelineUpdated?.Invoke(this, position);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] Seek error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _timelineTimer.Stop();
            _sessionCheckTimer.Stop();

            if (_currentSession != null)
            {
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                _currentSession = null;
            }

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
                _sessionManager = null;
            }

            _updateLock.Dispose();
        }
    }
}
