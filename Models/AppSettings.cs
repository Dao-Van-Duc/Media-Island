using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DynamicIslandMedia.Models
{
    public class AppSettings
    {
        public const double DefaultEdgeDistance = 8.0;
        public const double DefaultBottomMargin = DefaultEdgeDistance;
        public const string PositionBottom = "Bottom";
        public const string PositionTop = "Top";

        public string IslandPosition { get; set; } = PositionBottom;
        public double EdgeDistance { get; set; } = DefaultEdgeDistance;

        // Backward compatibility: alias BottomMargin to EdgeDistance for existing saved settings
        [JsonPropertyName("BottomMargin")]
        public double? LegacyBottomMargin
        {
            get => null;
            set
            {
                if (value.HasValue && EdgeDistance == DefaultEdgeDistance)
                {
                    EdgeDistance = value.Value;
                }
            }
        }

        [JsonIgnore]
        public double BottomMargin
        {
            get => EdgeDistance;
            set => EdgeDistance = value;
        }

        public bool IsMasterEnabled { get; set; } = true;
        public bool AutoHideWhenIdle { get; set; } = false;
        public bool AutoExpandOnHover { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;

        public bool IsPositionPinned { get; set; } = false;
        public double? PinnedLeft { get; set; } = null;
        public double? PinnedTop { get; set; } = null;
        public string? PinnedExpandDirection { get; set; } = null;

        // Legacy support in case old settings.json has TopMargin
        [JsonPropertyName("TopMargin")]
        public double? LegacyTopMargin
        {
            get => null;
            set
            {
                if (value.HasValue && EdgeDistance == DefaultEdgeDistance)
                {
                    // keep DefaultEdgeDistance
                }
            }
        }

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIslandMedia"
        );
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "DynamicIslandMedia";

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        settings.SyncStartupRegistry();
                        return settings;
                    }
                }
            }
            catch
            {
                // Fallback to defaults
            }

            var defaultSettings = new AppSettings();
            defaultSettings.SyncStartupRegistry();
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                ApplyStartupRegistry(StartWithWindows);
            }
            catch
            {
                // Silently ignore settings write errors
            }
        }

        private void SyncStartupRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
                StartWithWindows = key?.GetValue(AppRegistryName) != null;
            }
            catch
            {
                // Registry read fallback
            }
        }

        private static void ApplyStartupRegistry(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "DynamicIslandMedia.exe");
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppRegistryName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                }
            }
            catch
            {
                // Registry write fallback
            }
        }
    }
}
