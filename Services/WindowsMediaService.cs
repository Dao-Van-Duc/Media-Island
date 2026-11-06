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
        private bool _disposed;

        public event EventHandler<MediaPlaybackInfo>? MediaChanged;
        public event EventHandler<TimeSpan>? TimelineUpdated;

        public MediaPlaybackInfo CurrentMedia { get; private set; } = new();

        public WindowsMediaService()
        {
            _timelineTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;
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
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] Initialization error: {ex.Message}");
            }
        }

        private GlobalSystemMediaTransportControlsSession? GetBestSession(GlobalSystemMediaTransportControlsSessionManager? manager)
        {
            if (manager == null) return null;
            try
            {
                var current = manager.GetCurrentSession();
                if (current != null) return current;

                var sessions = manager.GetSessions();
                if (sessions != null && sessions.Count > 0)
                {
                    // Prioritize playing session if any
                    foreach (var s in sessions)
                    {
                        var info = s.GetPlaybackInfo();
                        if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            return s;
                        }
                    }
                    // Otherwise return first available session (e.g. paused)
                    return sessions[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] GetBestSession error: {ex.Message}");
            }
            return null;
        }

        private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            try
            {
                var session = GetBestSession(sender);
                await UpdateCurrentSessionAsync(session);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsMediaService] CurrentSessionChanged error: {ex.Message}");
            }
        }

        private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            try
            {
                var session = GetBestSession(sender);
                await UpdateCurrentSessionAsync(session);
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

                if (_currentSession != null)
                {
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                    await RefreshMediaAsync();
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
        }

        private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            await RefreshMediaAsync();
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await RefreshMediaAsync();
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            SyncTimeline();
        }

        public async Task RefreshMediaAsync()
        {
            if (_currentSession == null)
            {
                CurrentMedia = new MediaPlaybackInfo { HasActiveSession = false };
                MediaChanged?.Invoke(this, CurrentMedia);
                return;
            }

            try
            {
                var playbackInfo = _currentSession.GetPlaybackInfo();
                var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
                var timeline = _currentSession.GetTimelineProperties();

                // If session is closed, check for other active sessions
                if (playbackInfo == null || playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                {
                    if (_sessionManager != null)
                    {
                        var altSession = GetBestSession(_sessionManager);
                        if (altSession != null && altSession != _currentSession)
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

                var info = new MediaPlaybackInfo
                {
                    HasActiveSession = true,
                    Title = mediaProps?.Title ?? string.Empty,
                    Artist = mediaProps?.Artist ?? string.Empty,
                    AlbumTitle = mediaProps?.AlbumTitle ?? string.Empty,
                    SourceAppId = _currentSession.SourceAppUserModelId ?? string.Empty,
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

                CurrentMedia = info;

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
            if (CurrentMedia.IsPlaying && CurrentMedia.Duration > TimeSpan.Zero)
            {
                var newPos = CurrentMedia.Position.Add(TimeSpan.FromSeconds(1));
                if (newPos <= CurrentMedia.Duration)
                {
                    CurrentMedia.Position = newPos;
                    TimelineUpdated?.Invoke(this, newPos);
                }
            }
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
