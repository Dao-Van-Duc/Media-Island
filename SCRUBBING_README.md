# Smooth progress scrubbing

- Click any point in the expanded progress track to seek.
- Hold mouse button and drag left/right to preview progress in real time. Release to commit one seek request.
- During drag, island movement is temporarily blocked. **Saved Pin setting does not change.**
- If Windows cancels mouse capture, preview is cancelled safely.
- No seek requests are sent on each mouse move.
- `Services/WindowsMediaService.cs`: `ProgressUiIntervalMs = 40` controls automatic playback progress interpolation, separate from mouse scrubbing.

## Test

1. Without Pin, drag the progress: island should not move.
2. Click a point on the progress and release: one seek occurs.
3. Drag outside the card and release: progress clamps to 0% or 100%.
4. With Pin enabled, scrub still works.
5. Try a live stream with unknown duration: scrubbing should not begin.

The changed files are `MainWindow.xaml`, `MainWindow.xaml.cs`. Windows WPF build/runtime verification is required.
