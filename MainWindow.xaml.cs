using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIslandMedia.Models;
using DynamicIslandMedia.Rendering;
using DynamicIslandMedia.Services;
using DynamicIslandMedia.ViewModels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DynamicIslandMedia
{
	public partial class MainWindow : Window
	{
		public MainWindow(MainViewModel viewModel)
		{
			this.InitializeComponent();
			this._viewModel = viewModel;
			this._settings = viewModel.Settings;
			base.DataContext = this._viewModel;
			this._fullscreenDetector = new FullscreenDetectionService();
			this._fullscreenDetector.FullscreenChanged += this.FullscreenDetector_FullscreenChanged;
			base.Loaded += this.MainWindow_Loaded;
			base.Closed += this.MainWindow_Closed;
			this._viewModel.PropertyChanged += this.ViewModel_PropertyChanged;
			this._viewModel.RequestResetPosition += () => { this.RepositionWindow(); };
		}

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
		private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
		private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
		private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
		private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

		private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
		{
			if (IntPtr.Size != 8)
			{
				return (IntPtr)MainWindow.GetWindowLong32(hWnd, nIndex);
			}
			return MainWindow.GetWindowLongPtr64(hWnd, nIndex);
		}

		private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
		{
			if (IntPtr.Size != 8)
			{
				return (IntPtr)MainWindow.SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
			}
			return MainWindow.SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			IntPtr handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				long num = MainWindow.GetWindowLongPtr(handle, -20).ToInt64();
				IntPtr dwNewLong = new IntPtr(num | 128L);
				MainWindow.SetWindowLongPtr(handle, -20, dwNewLong);
			}
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			if (this._viewModel.IsPinned && this._settings.PinnedLeft.HasValue && this._settings.PinnedTop.HasValue)
			{
				this._hasCustomPosition = true;
				this.RestorePinnedPosition();
			}
			else
			{
				this.RepositionWindow();
			}
			this._fullscreenDetector.Start();
			this.UpdateVisualizer(this._viewModel.IsPlaying);
			this.IsVisibleChanged += (_, _) => { this.EnsureGlowAnimating(); this.EnsureEqualizerAnimating(); };
			this.UpdateProgressFill();
            this.StartAmbientGradient();
            this.ApplyAppearanceSettings();
			if (!this._viewModel.IsVisible)
			{
				base.Visibility = Visibility.Collapsed;
				base.Opacity = 0.0;
				return;
			}
			base.Visibility = Visibility.Visible;
			base.Opacity = 1.0;
		}

		private void ProgressTrackBorder_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			this.UpdateProgressFill();
		}

		private void ApplyExpandDirection(bool expandDownwards)
		{
			this._isExpandingDownwards = expandDownwards;
			if (expandDownwards)
			{
				if (this.IslandRootGrid != null)
				{
					this.IslandRootGrid.VerticalAlignment = VerticalAlignment.Top;
				}
				if (this.IslandHitBox != null)
				{
					this.IslandHitBox.VerticalAlignment = VerticalAlignment.Top;
					this.IslandHitBox.Margin = new Thickness(0, 16, 0, 0);
				}
				if (this.BottomHoverBuffer != null)
				{
					this.BottomHoverBuffer.Visibility = Visibility.Visible;
					this.BottomHoverBuffer.Height = 40;
					this.BottomHoverBuffer.VerticalAlignment = VerticalAlignment.Top;
				}
				if (this.ShadowBorder != null)
				{
					this.ShadowBorder.VerticalAlignment = VerticalAlignment.Top;
					this.ShadowBorder.Margin = new Thickness(0, 40, 0, 0);
				}
				if (this.IslandBorder != null)
				{
					this.IslandBorder.VerticalAlignment = VerticalAlignment.Top;
					this.IslandBorder.Margin = new Thickness(0, 40, 0, 0);
				}
			}
			else
			{
				if (this.IslandRootGrid != null)
				{
					this.IslandRootGrid.VerticalAlignment = VerticalAlignment.Bottom;
				}
				if (this.IslandHitBox != null)
				{
					this.IslandHitBox.VerticalAlignment = VerticalAlignment.Bottom;
					this.IslandHitBox.Margin = new Thickness(0, 0, 0, 16);
				}
				if (this.BottomHoverBuffer != null)
				{
					this.BottomHoverBuffer.Visibility = Visibility.Visible;
					this.BottomHoverBuffer.Height = 40;
					this.BottomHoverBuffer.VerticalAlignment = VerticalAlignment.Bottom;
				}
				if (this.ShadowBorder != null)
				{
					this.ShadowBorder.VerticalAlignment = VerticalAlignment.Bottom;
					this.ShadowBorder.Margin = new Thickness(0, 0, 0, 40);
				}
				if (this.IslandBorder != null)
				{
					this.IslandBorder.VerticalAlignment = VerticalAlignment.Bottom;
					this.IslandBorder.Margin = new Thickness(0, 0, 0, 40);
				}
			}
		}

		private void UpdateIslandAlignment()
		{
			bool isTop = string.Equals(this._viewModel.IslandPosition, "Top", StringComparison.OrdinalIgnoreCase);
			this.ApplyExpandDirection(isTop);
		}

		private void AutoDetectAndApplyExpandDirection()
		{
			try
			{
				IntPtr handle = new WindowInteropHelper(this).Handle;
				var screen = (handle != IntPtr.Zero)
					? System.Windows.Forms.Screen.FromHandle(handle)
					: System.Windows.Forms.Screen.PrimaryScreen;

				if (screen == null) return;
				var wa = screen.WorkingArea;
				double waLeft = wa.Left;
				double waTop = wa.Top;
				double waRight = wa.Right;
				double waBottom = wa.Bottom;

				double cardHeight = (this.IslandBorder != null && this.IslandBorder.ActualHeight > 0)
					? this.IslandBorder.ActualHeight
					: (this._viewModel.IsExpanded ? 155.0 : 38.0);

				double cardWidth = (this.IslandBorder != null && this.IslandBorder.ActualWidth > 0)
					? this.IslandBorder.ActualWidth
					: (this._viewModel.IsExpanded ? 390.0 : 240.0);

				// Determine current card visual position on screen
				double cardScreenTop;
				double cardScreenBottom;

				if (this._isExpandingDownwards)
				{
					cardScreenTop = base.Top + 40.0;
					cardScreenBottom = cardScreenTop + cardHeight;
				}
				else
				{
					cardScreenBottom = base.Top + 260.0;
					cardScreenTop = cardScreenBottom - cardHeight;
				}

				// Calculate available space in both directions
				double spaceBelow = waBottom - cardScreenTop;
				double spaceAbove = cardScreenBottom - waTop;

				const double requiredSpace = 165.0; // 155px expanded height + 10px screen edge buffer
				bool hasSpaceBelow = spaceBelow >= requiredSpace;
				bool hasSpaceAbove = spaceAbove >= requiredSpace;

				bool shouldExpandDownwards;

				if (hasSpaceBelow && !hasSpaceAbove)
				{
					shouldExpandDownwards = true;
				}
				else if (!hasSpaceBelow && hasSpaceAbove)
				{
					shouldExpandDownwards = false;
				}
				else if (hasSpaceBelow && hasSpaceAbove)
				{
					// Both have space: pick whichever has more space / natural hemisphere
					double screenCenterY = waTop + (waBottom - waTop) / 2.0;
					double cardCenterY = (cardScreenTop + cardScreenBottom) / 2.0;
					shouldExpandDownwards = cardCenterY < screenCenterY;
				}
				else
				{
					// Constrained on both sides: pick whichever has more space
					shouldExpandDownwards = spaceBelow >= spaceAbove;
				}

				// If direction changed, adjust base.Top so the card remains in the EXACT same visual position
				if (shouldExpandDownwards != this._isExpandingDownwards)
				{
					if (shouldExpandDownwards)
					{
						// Switching to Downward expansion (Top mode): anchor at cardScreenTop
						base.Top = cardScreenTop - 40.0;
					}
					else
					{
						// Switching to Upward expansion (Bottom mode): anchor at cardScreenBottom
						base.Top = cardScreenBottom - 260.0;
					}

					this.ApplyExpandDirection(shouldExpandDownwards);
				}

				// Clamp to ensure the card stays within monitor working area bounds
				this.ClampWindowToWorkArea(waLeft, waTop, waRight, waBottom, cardWidth);
			}
			catch
			{
			}
		}

		private void ClampWindowToWorkArea(double waLeft, double waTop, double waRight, double waBottom, double cardWidth)
		{
			// Horizontal clamp:
			double cardLeftOffset = (base.Width - cardWidth) / 2.0;
			double cardScreenLeft = base.Left + cardLeftOffset;
			double cardScreenRight = cardScreenLeft + cardWidth;

			if (cardScreenLeft < waLeft + 8.0)
			{
				base.Left = waLeft + 8.0 - cardLeftOffset;
			}
			else if (cardScreenRight > waRight - 8.0)
			{
				base.Left = waRight - 8.0 - cardLeftOffset - cardWidth;
			}

			// Vertical clamp:
			if (this._isExpandingDownwards)
			{
				double cardScreenTop = base.Top + 40.0;
				double expandedScreenBottom = cardScreenTop + 155.0;

				if (cardScreenTop < waTop + 8.0)
				{
					base.Top = waTop + 8.0 - 40.0;
				}
				else if (expandedScreenBottom > waBottom - 8.0)
				{
					base.Top = waBottom - 8.0 - 195.0;
				}
			}
			else
			{
				double cardScreenBottom = base.Top + 260.0;
				double expandedScreenTop = cardScreenBottom - 155.0;

				if (expandedScreenTop < waTop + 8.0)
				{
					base.Top = waTop + 8.0 - 105.0;
				}
				else if (cardScreenBottom > waBottom - 8.0)
				{
					base.Top = waBottom - 8.0 - 260.0;
				}
			}
		}

		private void RestorePinnedPosition()
		{
			if (!string.IsNullOrEmpty(this._settings.PinnedExpandDirection))
			{
				bool isTop = string.Equals(this._settings.PinnedExpandDirection, "Top", StringComparison.OrdinalIgnoreCase);
				this.ApplyExpandDirection(isTop);
			}
			else
			{
				this.UpdateIslandAlignment();
			}

			if (this._settings.PinnedLeft.HasValue && this._settings.PinnedTop.HasValue)
			{
				double left = this._settings.PinnedLeft.Value;
				double top = this._settings.PinnedTop.Value;

				double vLeft = SystemParameters.VirtualScreenLeft;
				double vTop = SystemParameters.VirtualScreenTop;
				double vWidth = SystemParameters.VirtualScreenWidth;
				double vHeight = SystemParameters.VirtualScreenHeight;

				if (left >= vLeft - 100 && left <= vLeft + vWidth - 100 &&
					top >= vTop - 50 && top <= vTop + vHeight - 50)
				{
					base.Left = left;
					base.Top = top;
					this.AutoDetectAndApplyExpandDirection();
					return;
				}
			}
			this.RepositionWindow();
		}

		public void RepositionWindow()
		{
			this._hasCustomPosition = false;
			bool isTop = string.Equals(this._viewModel.IslandPosition, "Top", StringComparison.OrdinalIgnoreCase);
			this.ApplyExpandDirection(isTop);
			Rect workArea = SystemParameters.WorkArea;
			base.Left = workArea.Left + (workArea.Width - base.Width) / 2.0;
			if (isTop)
			{
				base.Top = workArea.Top + this._viewModel.EdgeDistance - IslandTopPadding;
			}
			else
			{
				base.Top = workArea.Bottom - this._viewModel.EdgeDistance - (base.Height - IslandBottomPadding);
			}
		}

		private void OnWindowDragged()
		{
			this._hasCustomPosition = true;
			this.AutoDetectAndApplyExpandDirection();
			if (this._viewModel.IsPinned)
			{
				this._settings.PinnedLeft = base.Left;
				this._settings.PinnedTop = base.Top;
				this._settings.PinnedExpandDirection = this._isExpandingDownwards ? "Top" : "Bottom";
				this._settings.Save();
			}
		}

		private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			base.Dispatcher.Invoke(delegate()
			{
				string? propertyName = e.PropertyName;
				if (propertyName == "IsExpanded")
				{
					this.AnimateIslandState(this._viewModel.IsExpanded);
					return;
				}
				if (propertyName == "IsPlaying")
				{
					this.UpdateVisualizer(this._viewModel.IsPlaying);
					return;
				}
				if (propertyName == "IsVisible")
				{
					this.UpdateVisibility(this._viewModel.IsVisible);
					return;
				}
				if (propertyName == "IsPinned")
				{
					if (this._viewModel.IsPinned)
					{
						this._settings.PinnedLeft = base.Left;
						this._settings.PinnedTop = base.Top;
						this._settings.PinnedExpandDirection = this._isExpandingDownwards ? "Top" : "Bottom";
						this._settings.IsPositionPinned = true;
						this._settings.Save();
					}
					else
					{
						this._settings.IsPositionPinned = false;
						this._settings.PinnedLeft = null;
						this._settings.PinnedTop = null;
						this._settings.PinnedExpandDirection = null;
						this._settings.Save();
						// DO NOT call RepositionWindow(): Keep the island placed right at its current location
					}
					return;
				}
				if (propertyName == "IsMasterEnabled")
				{
					if (this._viewModel.IsMasterEnabled && !this._viewModel.IsPinned && this._hasCustomPosition)
					{
						this.RepositionWindow();
					}
					return;
				}
				if (propertyName == "EdgeDistance" || propertyName == "BottomMargin" || propertyName == "IslandPosition")
				{
					this.RepositionWindow();
					return;
				}
				if (propertyName == "ProgressRatio")
				{
					this.UpdateProgressFill();
					return;
				}
				if (!(propertyName == "HasMedia"))
				{
					return;
				}
				if (!this._viewModel.IsExpanded)
				{
					this.AnimateCompactWidth();
				}
			});
		}

		private void MainWindow_Closed(object? sender, EventArgs e)
		{
			this.StopGlowAnimating();
			if (_eqHooked) { CompositionTarget.Rendering -= OnEqualizerRendering; _eqHooked = false; }
			this._fullscreenDetector.Dispose();
		}

		private void FullscreenDetector_FullscreenChanged(object? sender, bool isFullscreen)
		{
			base.Dispatcher.Invoke(delegate()
			{
				this._isHiddenByFullscreen = isFullscreen;
				if (isFullscreen)
				{
					this.Visibility = Visibility.Collapsed;
					return;
				}
				if (this._viewModel.IsVisible)
				{
					this.Visibility = Visibility.Visible;
					DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200.0));
					this.BeginAnimation(UIElement.OpacityProperty, animation);
					this.EnsureGlowAnimating();
					this.EnsureEqualizerAnimating();
				}
			});
		}

		private void UpdateVisibility(bool isVisible)
		{
			if (this._isHiddenByFullscreen)
			{
				return;
			}
			if (isVisible)
			{
				base.Visibility = Visibility.Visible;
				DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200.0));
				base.BeginAnimation(UIElement.OpacityProperty, animation);
				this.EnsureGlowAnimating();
				this.EnsureEqualizerAnimating();
				return;
			}
			DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200.0));
			doubleAnimation.Completed += delegate(object? s, EventArgs e)
			{
				base.Visibility = Visibility.Collapsed;
			};
			base.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		}

		private void AnimateIslandState(bool expand)
		{
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			};
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(260.0);
			if (expand)
			{
                this.AnimateAmbientOpacity(true);
                this.UpdateBackgroundOpacity(true);

				DoubleAnimation doubleAnimation = new DoubleAnimation(this.CompactContentGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(100.0));
				doubleAnimation.Completed += delegate(object? s, EventArgs e)
				{
					this.CompactContentGrid.Visibility = Visibility.Collapsed;
					this.ExpandedContentGrid.Visibility = Visibility.Visible;
					DoubleAnimation animation5 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180.0));
					this.ExpandedContentGrid.BeginAnimation(UIElement.OpacityProperty, animation5);
					this.UpdateProgressFill();
				};
				this.CompactContentGrid.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				DoubleAnimation animation = new DoubleAnimation(this.IslandBorder.ActualWidth, 390.0, timeSpan)
				{
					EasingFunction = easingFunction
				};
				DoubleAnimation animation2 = new DoubleAnimation(this.IslandBorder.ActualHeight, 155.0, timeSpan)
				{
					EasingFunction = easingFunction
				};
				this.IslandBorder.CornerRadius = new CornerRadius(22.0);
                this.AmbientGradientLayer.CornerRadius = new CornerRadius(0.0, 0.0, 20.0, 20.0);
                this.UpdateAmbientGradientClip();
				this.ShadowBorder.CornerRadius = new CornerRadius(22.0);
				this.IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
				this.IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, animation2);
				this.ShadowBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
				this.ShadowBorder.BeginAnimation(FrameworkElement.HeightProperty, animation2);
				this.IslandHitBox.BeginAnimation(FrameworkElement.WidthProperty, animation);
				return;
			}
            this.AnimateAmbientOpacity(false);
            this.UpdateBackgroundOpacity(false);

			DoubleAnimation doubleAnimation2 = new DoubleAnimation(this.ExpandedContentGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(100.0));
			doubleAnimation2.Completed += delegate(object? s, EventArgs e)
			{
				this.ExpandedContentGrid.Visibility = Visibility.Collapsed;
				this.CompactContentGrid.Visibility = Visibility.Visible;
				this.EnsureEqualizerAnimating();
				DoubleAnimation animation5 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180.0));
				this.CompactContentGrid.BeginAnimation(UIElement.OpacityProperty, animation5);
			};
			this.ExpandedContentGrid.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
			double toValue = (double)(this._viewModel.HasMedia ? 240 : 180);
			DoubleAnimation animation3 = new DoubleAnimation(this.IslandBorder.ActualWidth, toValue, timeSpan)
			{
				EasingFunction = easingFunction
			};
			DoubleAnimation animation4 = new DoubleAnimation(this.IslandBorder.ActualHeight, 38.0, timeSpan)
			{
				EasingFunction = easingFunction
			};
			this.IslandBorder.CornerRadius = new CornerRadius(19.0);
            this.AmbientGradientLayer.CornerRadius = new CornerRadius(0.0, 0.0, 17.0, 17.0);
            this.UpdateAmbientGradientClip();
			this.ShadowBorder.CornerRadius = new CornerRadius(19.0);
			this.IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, animation3);
			this.IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, animation4);
			this.ShadowBorder.BeginAnimation(FrameworkElement.WidthProperty, animation3);
			this.ShadowBorder.BeginAnimation(FrameworkElement.HeightProperty, animation4);
			this.IslandHitBox.BeginAnimation(FrameworkElement.WidthProperty, animation3);
		}

		private void AnimateCompactWidth()
		{
			if (this._viewModel.IsExpanded)
			{
				return;
			}
			double toValue = (double)(this._viewModel.HasMedia ? 240 : 180);
			DoubleAnimation animation = new DoubleAnimation(this.IslandBorder.ActualWidth, toValue, TimeSpan.FromMilliseconds(220.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			this.IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
			this.ShadowBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
			this.IslandHitBox.BeginAnimation(FrameworkElement.WidthProperty, animation);
		}

		// COMPACT EQUALIZER. Each bar is a damped spring chasing a smooth target:
		// playing -> target = a layered-sine "music" level, paused -> target 0 (bars settle into round 3 px dots
		// with a small bounce). Heights are fractional (layout rounding is off for the bar strip), so motion
		// is continuous instead of stepping pixel by pixel. Frames run only while the compact pill shows.
		private const double EqDotSize = 3.0;
		private const double EqOmega = 19.0, EqZeta = 0.62;   // spring stiffness / damping: lively, soft overshoot
		private static readonly double[] EqAmplitude = { 10.0, 15.0, 12.5, 9.0 };
		private static readonly double[] EqRateA = { 1.15, 1.45, 1.30, 1.00 };   // Hz
		private static readonly double[] EqRateB = { 2.35, 2.90, 2.55, 2.10 };   // Hz
		private static readonly double[] EqRateC = { 0.43, 0.37, 0.51, 0.47 };   // Hz, slow level drift
		private static readonly double[] EqPhase = { 0.0, 1.3, 2.9, 4.4 };
		private readonly Stopwatch _eqClock = Stopwatch.StartNew();
		private readonly double[] _eqPos = new double[4], _eqVel = new double[4];
		private double _eqLastSeconds;
		private bool _eqPlaying, _eqHooked;

		private void UpdateVisualizer(bool isPlaying)
		{
			_eqPlaying = isPlaying;
			EnsureEqualizerAnimating();
		}

		private bool EqualizerSettled()
		{
			if (_eqPlaying) return false;
			for (int i = 0; i < 4; i++)
				if (Math.Abs(_eqPos[i]) > 0.01 || Math.Abs(_eqVel[i]) > 0.05) return false;
			return true;
		}

		private bool ShouldAnimateEqualizer =>
			this.IsVisible && !_isHiddenByFullscreen &&
			this.CompactContentGrid.Visibility == Visibility.Visible && !EqualizerSettled();

		private void EnsureEqualizerAnimating()
		{
			if (_eqHooked || !ShouldAnimateEqualizer) return;
			_eqLastSeconds = _eqClock.Elapsed.TotalSeconds;
			CompositionTarget.Rendering += OnEqualizerRendering;
			_eqHooked = true;
		}

		private void OnEqualizerRendering(object? sender, EventArgs e)
		{
			if (!ShouldAnimateEqualizer)
			{
				CompositionTarget.Rendering -= OnEqualizerRendering;
				_eqHooked = false;
				return;
			}
			double now = _eqClock.Elapsed.TotalSeconds;
			double dt = Math.Clamp(now - _eqLastSeconds, 0.0, 0.1);
			_eqLastSeconds = now;

			int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004)); // small fixed sub-steps keep the spring stable
			double h = dt / steps;
			Border[] bars = { this.WaveBar1, this.WaveBar2, this.WaveBar3, this.WaveBar4 };
			for (int i = 0; i < 4; i++)
			{
				double pos = _eqPos[i], vel = _eqVel[i];
				for (int s = 0; s < steps; s++)
				{
					double t = now - dt + h * (s + 1);
					double target = _eqPlaying ? EqualizerTarget(i, t) : 0.0;
					double acc = EqOmega * EqOmega * (target - pos) - 2.0 * EqZeta * EqOmega * vel;
					vel += acc * h;      // semi-implicit Euler
					pos += vel * h;
				}
				if (pos < 0.0) { pos = 0.0; if (vel < 0.0) vel = 0.0; }
				_eqPos[i] = pos;
				_eqVel[i] = vel;
				bars[i].Height = EqDotSize + pos;
			}
		}

		// Music-like level in [0.18, 1] x amplitude: two beating sines plus a slow drift, no hard edges.
		private static double EqualizerTarget(int i, double t)
		{
			double w = 0.5 + 0.5 * (0.45 * Math.Sin(2.0 * Math.PI * EqRateA[i] * t + EqPhase[i])
			                       + 0.35 * Math.Sin(2.0 * Math.PI * EqRateB[i] * t + 1.7 * EqPhase[i])
			                       + 0.20 * Math.Sin(2.0 * Math.PI * EqRateC[i] * t + 0.6 * EqPhase[i]));
			return EqAmplitude[i] * (0.18 + 0.82 * w);
		}

        // Visual tuning: GlowOpacity (0..1), BlurEffect Radius in XAML, HeadGlow Opacity in XAML.
        // Timeline smoothing comes from WindowsMediaService.ProgressUiIntervalMs, not a width animation.
        // A WPF Border's CornerRadius does NOT clip its children. The animated
        // wave fields extend beyond their layout bounds, so a rounded geometry
        // must explicitly clip the ambient strip at its actual rendered size.
        private void AmbientGradientLayer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateAmbientGradientClip();
        }

        public void RefreshAppearanceFromSettings()
        {
            this.ApplyAppearanceSettings();
            this.UpdateProgressFill();
        }

        private void UpdateBackgroundOpacity(bool expanded)
        {
            double percentage = expanded ? this._settings.ExpandedBackgroundOpacityPercent
                                         : this._settings.CompactBackgroundOpacityPercent;
            double opacity = Math.Clamp(percentage / 100.0, 0.0, 1.0);
            byte alpha = (byte)Math.Round(255.0 * opacity);
            this.IslandBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, 0x17, 0x18, 0x1C));

            // Keep the drop shadow, but prevent the shadow backing from acting as
            // a second nearly-opaque layer underneath a transparent island.
            byte shadowAlpha = (byte)Math.Round(10.0 * opacity);
            this.ShadowBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(shadowAlpha, 0x0D, 0x0D, 0x11));
        }

        private void ApplyAppearanceSettings()
        {
            this.UpdateBackgroundOpacity(this._viewModel.IsExpanded);


            if (!this._settings.EnableGradient)
            {
                this.AmbientGradientLayer.BeginAnimation(UIElement.OpacityProperty, null);
                this.AmbientGradientLayer.Opacity = 0.0;
            }
            else if (this._viewModel.IsExpanded)
            {
                this.AnimateAmbientOpacity(true);
            }

            this.ProgressGlowBorder.Visibility = (!this._settings.EnableProgressGlow || this.ProgressGlowBorder.Width < 1.0)
                ? Visibility.Hidden
                : Visibility.Visible;
        }


        private void UpdateAmbientGradientClip()
        {
            if (this.AmbientGradientLayer == null) return;

            double width = this.AmbientGradientLayer.ActualWidth;
            double height = this.AmbientGradientLayer.ActualHeight;
            if (width <= 0 || height <= 0) return;

            // Both top corners are fully transparent under the opacity mask;
            // bottom corners follow the expanded/compact card's rounded edge.
            double radius = this._viewModel.IsExpanded ? 20.0 : 17.0;
            radius = Math.Min(radius, Math.Min(width, height) / 2.0);
            this.AmbientGradientLayer.Clip = new RectangleGeometry(
                new Rect(0, 0, width, height), radius, radius);
        }

        private void UpdateProgressFill()
        {
            if (this.ProgressTrackBorder == null || this.ProgressTrackBorder.ActualWidth <= 0.0)
                return;

            double width = Math.Clamp(this.ProgressTrackBorder.ActualWidth * this._viewModel.ProgressRatio,
                                      0.0, this.ProgressTrackBorder.ActualWidth);
            if (_isScrubbing || _committingScrub) width = this.ProgressTrackBorder.ActualWidth * _scrubRatio;
            this.ProgressFillBorder.Width = width;
            this.ProgressGlowBorder.Width = width;
            this.ProgressGlowBorder.Visibility = (!this._settings.EnableProgressGlow || width < 1.0)
                ? Visibility.Hidden
                : Visibility.Visible;
        }

        // Only show the ambient color when expanded. Fade it in/out while the island morphs.
        private void AnimateAmbientOpacity(bool expanded)
        {
            var animation = new DoubleAnimation
            {
                To = (expanded && this._settings.EnableGradient) ? 0.82 : 0.0,
                Duration = TimeSpan.FromMilliseconds(expanded ? 350 : 160),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            this.AmbientGradientLayer.BeginAnimation(UIElement.OpacityProperty, animation,
                HandoffBehavior.SnapshotAndReplace);
            if (expanded) this.EnsureGlowAnimating(); // collapse: the handler unhooks once faded out
        }

        // AMBIENT GLOW - soft drifting colour mesh (Rendering/AmbientGlowRenderer).
        // Rendered at device resolution and dithered; driven by CompositionTarget.Rendering
        // only while the expanded gradient is actually on screen.
        private readonly AmbientGlowRenderer _glowRenderer = new AmbientGlowRenderer();
        private readonly Stopwatch _glowClock = new Stopwatch();
        private bool _glowHooked;
        private TimeSpan _lastGlowRenderingTime = TimeSpan.MinValue;
        private (int W, int H) _pendingGlowSize;
        private int _pendingGlowFrames;

        private void StartAmbientGradient()
        {
            _glowClock.Restart();
            EnsureGlowAnimating();
        }

        private bool ShouldAnimateGlow =>
            _settings.EnableGradient && this.IsVisible && !_isHiddenByFullscreen &&
            (_viewModel.IsExpanded || this.AmbientGradientLayer.Opacity > 0.001);

        private void EnsureGlowAnimating()
        {
            if (_glowHooked || !ShouldAnimateGlow) return;
            CompositionTarget.Rendering += OnGlowRendering;
            _glowHooked = true;
        }

        private void StopGlowAnimating()
        {
            if (!_glowHooked) return;
            CompositionTarget.Rendering -= OnGlowRendering;
            _glowHooked = false;
        }

        private void OnGlowRendering(object? sender, EventArgs e)
        {
            if (!ShouldAnimateGlow)
            {
                StopGlowAnimating(); // collapsed and faded out, hidden, fullscreen or disabled
                return;
            }
            // One render per composed frame (Rendering can fire more than once per frame). No fps cap:
            // a cap that is not a divisor of the refresh rate judders (75 Hz -> 37.5 fps); cost is ~0.5 ms.
            TimeSpan renderingTime = ((RenderingEventArgs)e).RenderingTime;
            if (renderingTime == _lastGlowRenderingTime) return;
            _lastGlowRenderingTime = renderingTime;
            if (!EnsureGlowBitmap()) return;
            _glowRenderer.Render(_glowClock.Elapsed.TotalSeconds);
        }

        // Bitmap matches the layer's device-pixel size so it is never resampled (resampling blurs the dither).
        // While the island morphs, the size changes every frame: keep stretching the current bitmap
        // (the layer is still fading in) and rebuild only once the size has held for 3 frames.
        private bool EnsureGlowBitmap()
        {
            double w = this.AmbientGradientLayer.ActualWidth, h = this.AmbientGradientLayer.ActualHeight;
            if (w < 1 || h < 1) return false;
            DpiScale dpi = VisualTreeHelper.GetDpi(this.AmbientGradientLayer);
            int pw = (int)Math.Ceiling(w * dpi.DpiScaleX), ph = (int)Math.Ceiling(h * dpi.DpiScaleY);
            var current = _glowRenderer.Bitmap;
            if (current != null && (current.PixelWidth != pw || current.PixelHeight != ph))
            {
                if (_pendingGlowSize != (pw, ph)) { _pendingGlowSize = (pw, ph); _pendingGlowFrames = 0; }
                if (++_pendingGlowFrames < 3) return true;
            }
            if (_glowRenderer.EnsureSize(pw, ph, dpi.DpiScaleX, dpi.DpiScaleY))
                this.AmbientWaveImage.Source = _glowRenderer.Bitmap;
            return true;
        }

		private void IslandHitBox_MouseEnter(object sender, MouseEventArgs e)
		{
			this._viewModel.OnMouseEnterIsland();
		}

		private void IslandHitBox_MouseLeave(object sender, MouseEventArgs e)
		{
            if (_isScrubbing) return;
			this._viewModel.OnMouseLeaveIsland();
		}

		private void IslandHitBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
            // Scrubbing temporarily blocks window dragging without changing saved Pin state.
            if (_isScrubbing || ProgressScrubArea.IsMouseCaptureWithin ||
                IsDescendantOf(e.OriginalSource as DependencyObject, ProgressScrubArea))
            {
                e.Handled = true;
                return;
            }
			if (!this._viewModel.IsExpanded)
			{
				this._viewModel.IsExpanded = true;
				e.Handled = true;
				return;
			}
			this._viewModel.CancelCollapseTimer();
			if (this._viewModel.IsPinned)
			{
				// User explicitly requested: when pin button is ON, do not let users move it by cursor
				return;
			}
			if (e.ButtonState == MouseButtonState.Pressed)
			{
				try
				{
					base.DragMove();
					this.OnWindowDragged();
				}
				catch
				{
				}
			}
		}

		private void IslandBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			this._viewModel.IsExpanded = !this._viewModel.IsExpanded;
			e.Handled = true;
		}

        // Scrubbing is a temporary interaction lock; it never modifies IsPinned or settings.json.
        private bool _isScrubbing;
        private bool _committingScrub;
        private double _scrubRatio;
        private double _scrubOriginalRatio;

        private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, ancestor)) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private double RatioFromMouse(MouseEventArgs e)
        {
            if (this.ProgressTrackBorder.ActualWidth <= 0) return 0;
            Point point = e.GetPosition(this.ProgressTrackBorder);
            return Math.Clamp(point.X / this.ProgressTrackBorder.ActualWidth, 0.0, 1.0);
        }

        private void ProgressScrubArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (this.ProgressTrackBorder.ActualWidth <= 0 ||
                this._viewModel.CurrentMedia.Duration <= TimeSpan.Zero || _committingScrub)
                return;

            this._viewModel.CancelCollapseTimer();
            _scrubOriginalRatio = _viewModel.ProgressRatio;
            _scrubRatio = RatioFromMouse(e);
            _isScrubbing = true;
            ProgressScrubArea.CaptureMouse();
            UpdateProgressFill();
            e.Handled = true; // prevent the bubbling handler from invoking DragMove()
        }

        private void ProgressScrubArea_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isScrubbing) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelScrub();
                return;
            }
            _scrubRatio = RatioFromMouse(e);
            UpdateProgressFill();
            e.Handled = true;
        }

        private async void ProgressScrubArea_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isScrubbing) return;
            _scrubRatio = RatioFromMouse(e);
            double target = _scrubRatio;
            _isScrubbing = false;
            _committingScrub = true;
            // Release capture before awaiting the media service. Prevent a LostCapture cancellation.
            if (ProgressScrubArea.IsMouseCaptured) ProgressScrubArea.ReleaseMouseCapture();
            e.Handled = true;
            try
            {
                await _viewModel.SeekToRatioAsync(target);
            }
            finally
            {
                _committingScrub = false;
                UpdateProgressFill();
                // If released outside, a captured mouse may have suppressed MouseLeave.
                if (!this.IslandHitBox.IsMouseOver)
                    this._viewModel.OnMouseLeaveIsland();
            }
        }

        private void ProgressScrubArea_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isScrubbing) CancelScrub();
        }

        private void CancelScrub()
        {
            if (!_isScrubbing) return;
            _isScrubbing = false;
            _scrubRatio = _scrubOriginalRatio;
            if (ProgressScrubArea.IsMouseCaptured) ProgressScrubArea.ReleaseMouseCapture();
            UpdateProgressFill();
            if (!this.IslandHitBox.IsMouseOver)
                this._viewModel.OnMouseLeaveIsland();
        }

		private readonly MainViewModel _viewModel;
		private readonly AppSettings _settings;
		private readonly FullscreenDetectionService _fullscreenDetector;
		private bool _isHiddenByFullscreen;
		private bool _hasCustomPosition;
		private bool _isExpandingDownwards;

		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 128;
		public const double IslandBottomPadding = 56.0;
		public const double IslandTopPadding = 56.0;
	}
}
