# Native Acrylic (experimental)

`Services/NativeAcrylicBackdrop.cs` contains an experimental helper using Windows 11 DWM
`DWMWA_SYSTEMBACKDROP_TYPE=38`, `DWMSBT_TRANSIENTWINDOW=3`. Its intended API minimum is Windows 11 22H2
(build 22621).

**Current status:** the application does not instantiate this helper, and Settings has no acrylic toggle.
The current island uses the existing transparent WPF window and per-state background opacity. Native
acrylic is not an enabled feature of this package.

The helper is retained for future evaluation. Its design uses a second non-layered click-through HWND
behind the island. Integrating lifecycle, DPI, sizing and visibility would require a separate scoped task.

## Important caveats
- The helper compiles with the application; its native backdrop behavior has not been exercised.
- DWM may decline to render acrylic depending on Windows version, transparency accessibility preferences, power saving, theme, GPU or composition state. In unsupported environments the app will show the existing translucent fallback.
- Before any future integration, verify native rounding, click-through, dragging, hover, Snipping Tool,
  Alt+Tab, fullscreen and multi-monitor DPI, including cleanup of the extra HWND.
- This does not turn the full-sized presentation window into Acrylic. It uses an independent blur HWND corresponding to the actual card position.
