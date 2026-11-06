using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using DynamicIslandMedia.Helpers;
using DynamicIslandMedia.Models;
using DynamicIslandMedia.Services;

namespace DynamicIslandMedia.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly WindowsMediaService _mediaService;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _collapseTimer;
        private readonly DispatcherTimer _noMediaGraceTimer;

        private MediaPlaybackInfo _currentMedia = new();
        private bool _isExpanded;
        private bool _isPinned;
        private bool _isVisible = false;
        private bool _isMasterEnabled = true;
        private string _islandPosition = AppSettings.PositionBottom;
        private double _edgeDistance = AppSettings.DefaultEdgeDistance;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestResetPosition;

        public MainViewModel(WindowsMediaService mediaService, AppSettings settings)
        {
            _mediaService = mediaService;
            _settings = settings;
            _islandPosition = _settings.IslandPosition;
            _edgeDistance = _settings.EdgeDistance;
            _isMasterEnabled = _settings.IsMasterEnabled;
            _isPinned = _settings.IsPositionPinned;

            _collapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1800)
            };
            _collapseTimer.Tick += (s, e) =>
            {
                _collapseTimer.Stop();
                if (IsExpanded)
                {
                    IsExpanded = false;
                }
            };

            // 15-second grace period timer when active media stops
            _noMediaGraceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _noMediaGraceTimer.Tick += (s, e) =>
            {
                _noMediaGraceTimer.Stop();
                if (!HasMedia && _isMasterEnabled)
                {
                    if (IsExpanded) IsExpanded = false;
                    IsVisible = false;
                }
            };

            // Hook media events
            _mediaService.MediaChanged += OnMediaChanged;
            _mediaService.TimelineUpdated += OnTimelineUpdated;

            // Setup commands
            PlayPauseCommand = new RelayCommand(async () => await _mediaService.TogglePlayPauseAsync());
            NextCommand = new RelayCommand(async () => await _mediaService.SkipNextAsync());
            PreviousCommand = new RelayCommand(async () => await _mediaService.SkipPreviousAsync());
            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            TogglePinCommand = new RelayCommand(() => IsPinned = !IsPinned);
            CollapseCommand = new RelayCommand(() => IsExpanded = false);
            ExpandCommand = new RelayCommand(() => IsExpanded = true);
            ToggleVisibilityCommand = new RelayCommand(() => IsMasterEnabled = !IsMasterEnabled);

            UpdatePresenceVisibility();
        }

        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand ToggleExpandCommand { get; }
        public ICommand TogglePinCommand { get; }
        public ICommand CollapseCommand { get; }
        public ICommand ExpandCommand { get; }
        public ICommand ToggleVisibilityCommand { get; }

        public AppSettings Settings => _settings;

        public MediaPlaybackInfo CurrentMedia
        {
            get => _currentMedia;
            private set
            {
                _currentMedia = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMedia));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(TrackTitle));
                OnPropertyChanged(nameof(ArtistName));
                OnPropertyChanged(nameof(AlbumTitle));
                OnPropertyChanged(nameof(AppName));
                OnPropertyChanged(nameof(AlbumArt));
                OnPropertyChanged(nameof(ProgressRatio));
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(PositionString));
                OnPropertyChanged(nameof(DurationString));
                OnPropertyChanged(nameof(PrimaryColor));
                OnPropertyChanged(nameof(SecondaryColor));
            }
        }

        public Color PrimaryColor => CurrentMedia.PrimaryColor;
        public Color SecondaryColor => CurrentMedia.SecondaryColor;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    if (_isExpanded)
                    {
                        _collapseTimer.Stop();
                    }
                }
            }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    _settings.IsPositionPinned = value;
                    if (!value)
                    {
                        _settings.PinnedLeft = null;
                        _settings.PinnedTop = null;
                        _settings.PinnedExpandDirection = null;
                    }
                    _settings.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PinButtonToolTip));
                }
            }
        }

        public string PinButtonToolTip => IsPinned ? "Position locked (Click to unlock moving)" : "Lock position on screen";

        public bool IsMasterEnabled
        {
            get => _isMasterEnabled;
            set
            {
                if (_isMasterEnabled != value)
                {
                    _isMasterEnabled = value;
                    _settings.IsMasterEnabled = value;
                    _settings.Save();
                    OnPropertyChanged();
                    UpdatePresenceVisibility();
                }
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            private set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Presence-based visibility logic with 15-second grace period:
        /// 1. When Master is OFF: Stays completely hidden. Cancels grace timer immediately.
        /// 2. When Master is ON & active media exists: Visible immediately. Cancels any ticking grace timer.
        /// 3. When Master is ON & active media stops: Keeps island visible and starts 15-second grace countdown.
        ///    If no media session restarts within 15 seconds, automatically collapses and hides the island.
        /// </summary>
        public void UpdatePresenceVisibility()
        {
            if (!_isMasterEnabled)
            {
                _noMediaGraceTimer.Stop();
                if (IsExpanded) IsExpanded = false;
                IsVisible = false;
                return;
            }

            if (HasMedia)
            {
                // New media session or playback active: cancel grace countdown and ensure visible
                _noMediaGraceTimer.Stop();
                IsVisible = true;
            }
            else
            {
                // No active media: if currently visible, start 15s grace countdown before auto-hiding
                if (IsVisible)
                {
                    if (!_noMediaGraceTimer.IsEnabled)
                    {
                        _noMediaGraceTimer.Start();
                    }
                }
                else
                {
                    _noMediaGraceTimer.Stop();
                }
            }
        }

        public string IslandPosition
        {
            get => _islandPosition;
            set
            {
                var normalized = string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase) ? "Top" : "Bottom";
                if (_islandPosition != normalized)
                {
                    _islandPosition = normalized;
                    _settings.IslandPosition = normalized;
                    _settings.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DistanceLabelText));
                    OnPropertyChanged(nameof(IsTopPosition));
                    OnPropertyChanged(nameof(IsBottomPosition));
                }
            }
        }

        public bool IsTopPosition
        {
            get => string.Equals(_islandPosition, "Top", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value) IslandPosition = "Top";
            }
        }

        public bool IsBottomPosition
        {
            get => !IsTopPosition;
            set
            {
                if (value) IslandPosition = "Bottom";
            }
        }

        public string DistanceLabelText =>
            string.Equals(IslandPosition, "Top", StringComparison.OrdinalIgnoreCase)
                ? "Distance from Top Screen Edge:"
                : "Distance from Bottom Screen Edge:";

        public double EdgeDistance
        {
            get => _edgeDistance;
            set
            {
                if (Math.Abs(_edgeDistance - value) > 0.01)
                {
                    _edgeDistance = value;
                    _settings.EdgeDistance = value;
                    _settings.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BottomMargin));
                }
            }
        }

        public double BottomMargin
        {
            get => EdgeDistance;
            set => EdgeDistance = value;
        }

        public bool HasMedia => CurrentMedia.HasMedia;
        public bool IsPlaying => CurrentMedia.IsPlaying;

        public string TrackTitle => string.IsNullOrWhiteSpace(CurrentMedia.Title) ? "No media playing" : CurrentMedia.Title;
        public string ArtistName => string.IsNullOrWhiteSpace(CurrentMedia.Artist) ? "Media Island" : CurrentMedia.Artist;
        public string AlbumTitle => CurrentMedia.AlbumTitle;
        public string AppName => CurrentMedia.FriendlyAppName;

        public ImageSource AlbumArt => CurrentMedia.AlbumArt ?? IconHelper.GetFallbackAlbumArt();

        public double ProgressRatio => CurrentMedia.ProgressRatio;
        public double ProgressPercent => CurrentMedia.ProgressRatio * 100.0;

        public string PositionString => FormatTime(CurrentMedia.Position);
        public string DurationString => FormatTime(CurrentMedia.Duration);

        public void OnMouseEnterIsland()
        {
            _collapseTimer.Stop();
            if (_settings.AutoExpandOnHover && !_isExpanded && HasMedia)
            {
                IsExpanded = true;
            }
        }

        public void OnMouseLeaveIsland()
        {
            if (IsExpanded)
            {
                _collapseTimer.Stop();
                IsExpanded = false;
            }
        }

        public void ResetPositionToDefault()
        {
            IsPinned = false;
            _settings.PinnedLeft = null;
            _settings.PinnedTop = null;
            _settings.PinnedExpandDirection = null;
            _settings.Save();
            RequestResetPosition?.Invoke();
        }

        public void CancelCollapseTimer()
        {
            _collapseTimer.Stop();
        }

        public async Task SeekToRatioAsync(double ratio)
        {
            if (CurrentMedia.Duration > TimeSpan.Zero)
            {
                var targetTicks = (long)(CurrentMedia.Duration.Ticks * Math.Clamp(ratio, 0.0, 1.0));
                await _mediaService.SeekAsync(new TimeSpan(targetTicks));
            }
        }

        private void OnMediaChanged(object? sender, MediaPlaybackInfo info)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                CurrentMedia = info;
                UpdatePresenceVisibility();
            });
        }

        private void OnTimelineUpdated(object? sender, TimeSpan position)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(ProgressRatio));
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(PositionString));
                OnPropertyChanged(nameof(DurationString));
            });
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
            }
            return $"{time.Minutes:D2}:{time.Seconds:D2}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
