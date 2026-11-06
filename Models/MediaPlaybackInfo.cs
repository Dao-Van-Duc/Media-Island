using System;
using System.Windows.Media;
using Windows.Media.Control;
using Color = System.Windows.Media.Color;
using DynamicIslandMedia.Helpers;

namespace DynamicIslandMedia.Models
{
    public class MediaPlaybackInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumTitle { get; set; } = string.Empty;
        public string SourceAppId { get; set; } = string.Empty;
        public bool IsPlaying { get; set; }
        public GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus { get; set; } =
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
        public ImageSource? AlbumArt { get; set; }
        public Color PrimaryColor { get; set; } = AlbumColorExtractor.FallbackColor1;
        public Color SecondaryColor { get; set; } = AlbumColorExtractor.FallbackColor2;
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public bool HasActiveSession { get; set; } = false;

        // Active media exists if a session is present and has metadata (or is playing)
        public bool HasMedia => HasActiveSession && (!string.IsNullOrWhiteSpace(Title) || IsPlaying);

        public double ProgressRatio
        {
            get
            {
                if (Duration.TotalSeconds <= 0) return 0;
                var ratio = Position.TotalSeconds / Duration.TotalSeconds;
                return Math.Clamp(ratio, 0.0, 1.0);
            }
        }

        public string FriendlyAppName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SourceAppId)) return "Now Playing";
                var id = SourceAppId.ToLowerInvariant();
                if (id.Contains("spotify")) return "Spotify";
                if (id.Contains("chrome")) return "Chrome";
                if (id.Contains("msedge") || id.Contains("edge")) return "Microsoft Edge";
                if (id.Contains("firefox")) return "Firefox";
                if (id.Contains("brave")) return "Brave";
                if (id.Contains("applemusic") || id.Contains("itunes")) return "Apple Music";
                if (id.Contains("musicbee")) return "MusicBee";
                if (id.Contains("aimp")) return "AIMP";
                if (id.Contains("foobar")) return "foobar2000";
                if (id.Contains("vlc")) return "VLC";
                if (id.Contains("wmplayer") || id.Contains("zune") || id.Contains("mediaplayer")) return "Media Player";

                // Strip common file extensions if it's an exe name
                var name = System.IO.Path.GetFileNameWithoutExtension(SourceAppId);
                return string.IsNullOrWhiteSpace(name) ? "Media" : name;
            }
        }
    }
}
