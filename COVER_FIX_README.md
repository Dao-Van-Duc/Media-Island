# Media Island — rounded covers + correct aspect ratio

Both compact and expanded covers use `Controls/AlbumCover.cs` from `MainWindow.xaml`. Keep the control
and XAML changes together when building this version.

The control calculates the image dimensions from its natural aspect ratio, centers the oversized image
in a clipped canvas, and applies a rounded rectangle to the cover bounds. This prevents stretching while
filling the square cover area.

Tune the `CornerRadius` and dimensions on the AlbumCover instances in MainWindow.xaml.

The handoff build and a direct control measurement/cropping check passed. Interactive checks with different
album images and monitor DPI remain part of the regression checklist in [docs/VERIFICATION.md](docs/VERIFICATION.md).
