# Appearance tuning

The sliders in Settings > Appearance independently control:
- **Compact pill opacity:** default 85% opacity (15% transparent)
- **Expanded card opacity:** default 20% opacity (80% transparent)

Change defaults in `Models/AppSettings.cs` via `CompactBackgroundOpacityPercent` and `ExpandedBackgroundOpacityPercent`.
Values are applied when entering/leaving the expanded state, and on Apply & Close.
Gradient and progress glow retain separate toggles.
The gradient remains visible irrespective of background opacity, by design.


## Ambient mesh
- `Rendering/AmbientGlowRenderer.cs` contains the accepted blue/aqua palette, drift cycles and bottom-up fade.
- `FadeTop`, `FadeBottom`, `FadeCurve` and `PeakAlpha` control the vertical profile; the per-layer opacity
  animation is in `MainWindow.xaml.cs` > `AnimateAmbientOpacity`.
- The renderer uses a device-resolution premultiplied bitmap and fixed spatial dither. Preserve those
  properties when tuning the palette to avoid reintroducing dark-gradient banding.
- Rendering is hooked only for the expanded glow/fade; physical bitmap size is refreshed after morphing.
- Native acrylic is currently an unwired experiment, described in [NATIVE_ACRYLIC_README.md](NATIVE_ACRYLIC_README.md).
