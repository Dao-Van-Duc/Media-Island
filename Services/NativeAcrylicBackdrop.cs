using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DynamicIslandMedia.Services
{
    // A real DWM Acrylic HWND, physically sized to the visible island rather than
    // to the 500x300 transparent presentation window. Windows 11 build 22621+.
    internal sealed class NativeAcrylicBackdrop : IDisposable
    {
        private const int DwmSystemBackdropType = 38;
        private const int DwmCornerPreference = 33;
        private const int TransientAcrylic = 3;
        private const int DoNotRound = 1;
        private const int GwlExStyle = -20;
        private const long WsExTransparent = 0x20;
        private const long WsExNoActivate = 0x08000000;
        private const long WsExToolWindow = 0x80;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private readonly Window _window;
        private IntPtr _hwnd;
        private bool _created;
        private bool _available;
        private int _lastX, _lastY, _lastW, _lastH, _lastRadius;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr obj);

        public NativeAcrylicBackdrop()
        {
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = false,
                Background = System.Windows.Media.Brushes.Transparent,
                Width = 240,
                Height = 38,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Focusable = false,
                Content = new System.Windows.Controls.Grid { Background = System.Windows.Media.Brushes.Transparent }
            };
        }

        private bool EnsureCreated()
        {
            if (_created) return _available;
            _created = true;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
            try
            {
                // SourceInitialized creates a real, non-layered HWND (essential for DWM backdrop).
                _window.Show();
                _hwnd = new WindowInteropHelper(_window).Handle;
                if (_hwnd == IntPtr.Zero) return false;
                long ex = IntPtr.Size == 8 ? GetWindowLongPtr64(_hwnd, GwlExStyle).ToInt64() : GetWindowLong32(_hwnd, GwlExStyle);
                long newEx = ex | WsExTransparent | WsExNoActivate | WsExToolWindow;
                if (IntPtr.Size == 8) SetWindowLongPtr64(_hwnd, GwlExStyle, new IntPtr(newEx));
                else SetWindowLong32(_hwnd, GwlExStyle, unchecked((int)newEx));

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);
                int acrylic = TransientAcrylic;
                int result = DwmSetWindowAttribute(_hwnd, DwmSystemBackdropType, ref acrylic, sizeof(int));
                int corner = DoNotRound; // We apply an exact rounded native window region ourselves.
                DwmSetWindowAttribute(_hwnd, DwmCornerPreference, ref corner, sizeof(int));
                _available = result >= 0;
                Debug.WriteLine($"[NativeAcrylic] DwmSetWindowAttribute result 0x{result:X8}");
                if (!_available) _window.Hide();
                return _available;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[NativeAcrylic] initialization failed: {e}");
                try { _window.Hide(); } catch { }
                return false;
            }
        }

        // All coordinates are physical screen pixels. The first argument is the
        // foreground WPF HWND, so SetWindowPos inserts Acrylic immediately BELOW it.
        public void Update(IntPtr foregroundHwnd, int x, int y, int width, int height, int radius)
        {
            if (!EnsureCreated() || foregroundHwnd == IntPtr.Zero) return;
            if (width < 1 || height < 1) { Hide(); return; }
            if (!_window.IsVisible) _window.Show();
            bool geometryChanged = x != _lastX || y != _lastY || width != _lastW || height != _lastH;
            bool regionChanged = width != _lastW || height != _lastH || radius != _lastRadius;
            if (geometryChanged)
            {
                _lastX = x; _lastY = y; _lastW = width; _lastH = height;
                SetWindowPos(_hwnd, foregroundHwnd, x, y, width, height, SwpNoActivate | SwpNoOwnerZOrder);
            }
            if (regionChanged)
            {
                _lastRadius = radius;
                // SetWindowRgn takes ownership when successful.
                var rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
                if (rgn != IntPtr.Zero && SetWindowRgn(_hwnd, rgn, true) == 0) DeleteObject(rgn);
            }
            // Maintain z order even after topmost foreground changes.
            SetWindowPos(_hwnd, foregroundHwnd, x, y, width, height, SwpNoActivate | SwpNoOwnerZOrder);
        }
        public void Hide() { if (_created && _window.IsVisible) _window.Hide(); }
        public void Dispose() { if (_created) _window.Close(); }
    }
}
