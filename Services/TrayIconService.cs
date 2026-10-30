using System;
using System.Drawing;
using System.Windows.Forms;
using DynamicIslandMedia.Helpers;
using DynamicIslandMedia.Models;

namespace DynamicIslandMedia.Services
{
    public class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _toggleMasterItem;
        private readonly ToolStripMenuItem _toggleExpandItem;
        private readonly ToolStripMenuItem _resetPositionItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _exitItem;

        public event Action? ToggleIslandRequested;
        public event Action? ToggleExpandRequested;
        public event Action? ResetPositionRequested;
        public event Action? ShowSettingsRequested;
        public event Action? ExitRequested;

        public TrayIconService()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = IconHelper.GetTrayIcon(true),
                Text = "Media Island",
                Visible = true
            };

            // Modern dark styled ContextMenuStrip
            _contextMenu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false
            };

            _statusItem = new ToolStripMenuItem("Media Island")
            {
                Enabled = false,
                ForeColor = Color.FromArgb(160, 160, 175)
            };

            _toggleMasterItem = new ToolStripMenuItem("Turn Island OFF", null, (s, e) => ToggleIslandRequested?.Invoke());
            _toggleExpandItem = new ToolStripMenuItem("Expand / Collapse", null, (s, e) => ToggleExpandRequested?.Invoke());
            _resetPositionItem = new ToolStripMenuItem("Reset Position to Default", null, (s, e) => ResetPositionRequested?.Invoke());
            _settingsItem = new ToolStripMenuItem("Settings...", null, (s, e) => ShowSettingsRequested?.Invoke());
            _exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitRequested?.Invoke());

            _contextMenu.Items.Add(_statusItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_toggleMasterItem);
            _contextMenu.Items.Add(_toggleExpandItem);
            _contextMenu.Items.Add(_resetPositionItem);
            _contextMenu.Items.Add(_settingsItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_exitItem);

            _notifyIcon.ContextMenuStrip = _contextMenu;

            // Single click toggles master on/off switch
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleIslandRequested?.Invoke();
                }
            };
        }

        public void UpdateState(MediaPlaybackInfo media, bool isMasterEnabled, bool isVisible, bool isExpanded)
        {
            try
            {
                // Dynamic icon: clean note when ON, note with crisp white slash when OFF
                _notifyIcon.Icon = IconHelper.GetTrayIcon(isMasterEnabled);

                _toggleMasterItem.Text = isMasterEnabled ? "Turn Island OFF" : "Turn Island ON";
                _toggleExpandItem.Text = isExpanded ? "Collapse to Pill" : "Expand Island";
                _toggleExpandItem.Enabled = isMasterEnabled && isVisible;
                _resetPositionItem.Enabled = isMasterEnabled;

                if (!isMasterEnabled)
                {
                    _statusItem.Text = "Media Island: OFF (Disabled)";
                    _notifyIcon.Text = "Media Island - OFF";
                }
                else if (media.HasMedia && !string.IsNullOrWhiteSpace(media.Title))
                {
                    var title = media.Title.Length > 25 ? media.Title[..22] + "..." : media.Title;
                    var artist = !string.IsNullOrWhiteSpace(media.Artist) ? $" - {media.Artist}" : "";
                    _statusItem.Text = $"🎵 {title}{artist}";

                    var fullTooltip = $"{media.Title}{artist}";
                    if (fullTooltip.Length > 63)
                        fullTooltip = fullTooltip[..60] + "...";
                    _notifyIcon.Text = fullTooltip;
                }
                else
                {
                    _statusItem.Text = "Media Island (Waiting for media)";
                    _notifyIcon.Text = "Media Island - Idle";
                }
            }
            catch
            {
                // Fallback for tooltip length limits
            }
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }

        private class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable())
            {
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (!e.Item.Enabled)
                {
                    e.TextColor = Color.FromArgb(140, 140, 155);
                }
                else
                {
                    e.TextColor = Color.FromArgb(245, 245, 250);
                }
                base.OnRenderItemText(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var pen = new Pen(Color.FromArgb(48, 48, 56), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using var pen = new Pen(Color.FromArgb(45, 45, 54), 1);
                var y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }
        }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(24, 24, 30);
            public override Color MenuBorder => Color.FromArgb(48, 48, 56);
            public override Color MenuItemSelected => Color.FromArgb(45, 45, 58);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(45, 45, 58);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(45, 45, 58);
            public override Color MenuItemBorder => Color.Transparent;
            public override Color SeparatorDark => Color.FromArgb(45, 45, 54);
            public override Color SeparatorLight => Color.Transparent;
        }
    }
}
