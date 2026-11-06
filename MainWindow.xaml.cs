using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using DynamicIslandMedia.Models;
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
			this._wave1 = (base.TryFindResource("WaveAnimation1") as Storyboard);
			this._wave2 = (base.TryFindResource("WaveAnimation2") as Storyboard);
			this._wave3 = (base.TryFindResource("WaveAnimation3") as Storyboard);
			this._wave4 = (base.TryFindResource("WaveAnimation4") as Storyboard);
			this.UpdateVisualizer(this._viewModel.IsPlaying);
			this.UpdateProgressFill();
			this.UpdateExpandedBackgroundColors(this._viewModel.PrimaryColor, this._viewModel.SecondaryColor, 0);
			this.StartWaterMotion();
			if (this.ExpandedIslandBackground != null)
			{
				this.ExpandedIslandBackground.Opacity = this._viewModel.IsExpanded ? 1.0 : 0.0;
				this.ExpandedIslandBackground.CornerRadius = this._viewModel.IsExpanded ? new CornerRadius(21.0) : new CornerRadius(18.0);
			}
			if (this.ExpandedWaterLayer2 != null)
			{
				this.ExpandedWaterLayer2.Opacity = this._viewModel.IsExpanded ? 0.9 : 0.0;
				this.ExpandedWaterLayer2.CornerRadius = this._viewModel.IsExpanded ? new CornerRadius(21.0) : new CornerRadius(18.0);
			}
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
				if (propertyName == "PrimaryColor" || propertyName == "SecondaryColor")
				{
					this.UpdateExpandedBackgroundColors(this._viewModel.PrimaryColor, this._viewModel.SecondaryColor);
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
				this.ShadowBorder.CornerRadius = new CornerRadius(22.0);
				if (this.ExpandedIslandBackground != null)
				{
					this.ExpandedIslandBackground.CornerRadius = new CornerRadius(21.0);
					this.ExpandedIslandBackground.BeginAnimation(UIElement.OpacityProperty,
						new DoubleAnimation(this.ExpandedIslandBackground.Opacity, 1.0, TimeSpan.FromMilliseconds(240.0)) { EasingFunction = easingFunction });
				}
				if (this.ExpandedWaterLayer2 != null)
				{
					this.ExpandedWaterLayer2.CornerRadius = new CornerRadius(21.0);
					this.ExpandedWaterLayer2.BeginAnimation(UIElement.OpacityProperty,
						new DoubleAnimation(this.ExpandedWaterLayer2.Opacity, 0.9, TimeSpan.FromMilliseconds(300.0)) { EasingFunction = easingFunction });
				}
				this.IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
				this.IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, animation2);
				this.ShadowBorder.BeginAnimation(FrameworkElement.WidthProperty, animation);
				this.ShadowBorder.BeginAnimation(FrameworkElement.HeightProperty, animation2);
				this.IslandHitBox.BeginAnimation(FrameworkElement.WidthProperty, animation);
				return;
			}
			DoubleAnimation doubleAnimation2 = new DoubleAnimation(this.ExpandedContentGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(100.0));
			doubleAnimation2.Completed += delegate(object? s, EventArgs e)
			{
				this.ExpandedContentGrid.Visibility = Visibility.Collapsed;
				this.CompactContentGrid.Visibility = Visibility.Visible;
				DoubleAnimation animation5 = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180.0));
				this.CompactContentGrid.BeginAnimation(UIElement.OpacityProperty, animation5);
			};
			this.ExpandedContentGrid.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
			if (this.ExpandedIslandBackground != null)
			{
				this.ExpandedIslandBackground.BeginAnimation(UIElement.OpacityProperty,
					new DoubleAnimation(this.ExpandedIslandBackground.Opacity, 0.0, TimeSpan.FromMilliseconds(160.0)) { EasingFunction = easingFunction });
			}
			if (this.ExpandedWaterLayer2 != null)
			{
				this.ExpandedWaterLayer2.BeginAnimation(UIElement.OpacityProperty,
					new DoubleAnimation(this.ExpandedWaterLayer2.Opacity, 0.0, TimeSpan.FromMilliseconds(140.0)) { EasingFunction = easingFunction });
			}
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
			this.ShadowBorder.CornerRadius = new CornerRadius(19.0);
			if (this.ExpandedIslandBackground != null)
			{
				this.ExpandedIslandBackground.CornerRadius = new CornerRadius(18.0);
			}
			if (this.ExpandedWaterLayer2 != null)
			{
				this.ExpandedWaterLayer2.CornerRadius = new CornerRadius(18.0);
			}
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

		private void UpdateExpandedBackgroundColors(Color c1, Color c2, int durationMs = 800)
		{
			if (this.ExpandedColorStop1 == null || this.ExpandedColorStop2 == null)
			{
				return;
			}

			// Balanced liquid blend:
			// Left side (~35%): Color 1 (Dominant, e.g. White) with clean, soft alpha
			// Middle zone (~25%): Wide, smooth organic blend between Color 1 and Color 2
			// Right side (40%+): Color 2 (Secondary, e.g. Pink) with rich, vibrant alpha and floating swell
			Color target1    = Color.FromArgb(115, c1.R, c1.G, c1.B);
			Color targetMid1 = Color.FromArgb(115, c1.R, c1.G, c1.B);
			Color targetMid2 = Color.FromArgb(160, c2.R, c2.G, c2.B);
			Color target2    = Color.FromArgb(165, c2.R, c2.G, c2.B);

			// Radial swell: rich, luminous pool of Color 2 on the right side (~40% of island)
			Color radCenter  = Color.FromArgb(120, c2.R, c2.G, c2.B);
			Color radEdge    = Color.FromArgb(0,   c2.R, c2.G, c2.B);

			if (durationMs <= 0)
			{
				this.ExpandedColorStop1.Color = target1;
				if (this.ExpandedColorStopMid1 != null) this.ExpandedColorStopMid1.Color = targetMid1;
				if (this.ExpandedColorStopMid2 != null) this.ExpandedColorStopMid2.Color = targetMid2;
				this.ExpandedColorStop2.Color = target2;
				if (this.RadialColorStop1 != null) this.RadialColorStop1.Color = radCenter;
				if (this.RadialColorStop2 != null) this.RadialColorStop2.Color = radEdge;
				return;
			}

			CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
			TimeSpan dur = TimeSpan.FromMilliseconds(durationMs);

			this.ExpandedColorStop1.BeginAnimation(GradientStop.ColorProperty,    new ColorAnimation(target1,    dur) { EasingFunction = ease });
			if (this.ExpandedColorStopMid1 != null)
			{
				this.ExpandedColorStopMid1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(targetMid1, dur) { EasingFunction = ease });
			}
			if (this.ExpandedColorStopMid2 != null)
			{
				this.ExpandedColorStopMid2.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(targetMid2, dur) { EasingFunction = ease });
			}
			this.ExpandedColorStop2.BeginAnimation(GradientStop.ColorProperty,    new ColorAnimation(target2,    dur) { EasingFunction = ease });
			if (this.RadialColorStop1 != null)
			{
				this.RadialColorStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(radCenter, dur) { EasingFunction = ease });
			}
			if (this.RadialColorStop2 != null)
			{
				this.RadialColorStop2.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(radEdge,   dur) { EasingFunction = ease });
			}
		}

		private void StartWaterMotion()
		{
			SineEase sine = new SineEase { EasingMode = EasingMode.EaseInOut };

			// 1. Organic angle rotation — slow wave tilt (10.5s period)
			if (this.GradientRotate != null)
			{
				var anim = new DoubleAnimation(-18.0, 18.0, TimeSpan.FromSeconds(10.5))
				{
					AutoReverse = true,
					RepeatBehavior = RepeatBehavior.Forever,
					EasingFunction = sine
				};
				this.GradientRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
			}

			// 2. Horizontal liquid drift (7.2s), vertical swell (8.8s) — incommensurate for no-repeat pattern
			if (this.GradientTranslate != null)
			{
				this.GradientTranslate.BeginAnimation(TranslateTransform.XProperty,
					new DoubleAnimation(-0.14, 0.14, TimeSpan.FromSeconds(7.2))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
				this.GradientTranslate.BeginAnimation(TranslateTransform.YProperty,
					new DoubleAnimation(-0.10, 0.10, TimeSpan.FromSeconds(8.8))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
			}

			// 3. Breathing scale (8.0s) — gentle expansion and contraction
			if (this.GradientScale != null)
			{
				var breathe = new DoubleAnimation(1.0, 1.20, TimeSpan.FromSeconds(8.0))
				{
					AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
				};
				this.GradientScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
				this.GradientScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
			}

			// 4. Undulating wave blend offsets — StopMid1 (Color 1) and StopMid2 (Color 2)
			// Mid1 waves between 0.24 and 0.40; Mid2 waves between 0.50 and 0.68.
			// This ensures Color 2 commands at least 40% (up to 55%) of the island space while smoothly blending in the middle
			if (this.ExpandedColorStopMid1 != null)
			{
				this.ExpandedColorStopMid1.BeginAnimation(GradientStop.OffsetProperty,
					new DoubleAnimation(0.24, 0.40, TimeSpan.FromSeconds(6.2))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
			}
			if (this.ExpandedColorStopMid2 != null)
			{
				this.ExpandedColorStopMid2.BeginAnimation(GradientStop.OffsetProperty,
					new DoubleAnimation(0.50, 0.68, TimeSpan.FromSeconds(7.8))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
			}

			// 5. Radial swell over the Color 2 zone (right side) counter-drifts organically
			if (this.RadialTranslate != null)
			{
				this.RadialTranslate.BeginAnimation(TranslateTransform.XProperty,
					new DoubleAnimation(0.14, -0.14, TimeSpan.FromSeconds(8.4))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
				this.RadialTranslate.BeginAnimation(TranslateTransform.YProperty,
					new DoubleAnimation(-0.10, 0.10, TimeSpan.FromSeconds(6.6))
					{
						AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
					});
			}
			if (this.RadialScale != null)
			{
				var radScaleAnim = new DoubleAnimation(0.95, 1.25, TimeSpan.FromSeconds(9.2))
				{
					AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = sine
				};
				this.RadialScale.BeginAnimation(ScaleTransform.ScaleXProperty, radScaleAnim);
				this.RadialScale.BeginAnimation(ScaleTransform.ScaleYProperty, radScaleAnim);
			}
		}

		private void UpdateVisualizer(bool isPlaying)
		{
			if (!isPlaying)
			{
				Storyboard? wave = this._wave1;
				if (wave != null)
				{
					wave.Stop(this.WaveBar1);
				}
				Storyboard? wave2 = this._wave2;
				if (wave2 != null)
				{
					wave2.Stop(this.WaveBar2);
				}
				Storyboard? wave3 = this._wave3;
				if (wave3 != null)
				{
					wave3.Stop(this.WaveBar3);
				}
				Storyboard? wave4 = this._wave4;
				if (wave4 != null)
				{
					wave4.Stop(this.WaveBar4);
				}
				this.WaveBar1.Height = 4.0;
				this.WaveBar2.Height = 8.0;
				this.WaveBar3.Height = 4.0;
				this.WaveBar4.Height = 6.0;
				return;
			}
			Storyboard? wave5 = this._wave1;
			if (wave5 != null)
			{
				wave5.Begin(this.WaveBar1, true);
			}
			Storyboard? wave6 = this._wave2;
			if (wave6 != null)
			{
				wave6.Begin(this.WaveBar2, true);
			}
			Storyboard? wave7 = this._wave3;
			if (wave7 != null)
			{
				wave7.Begin(this.WaveBar3, true);
			}
			Storyboard? wave8 = this._wave4;
			if (wave8 == null)
			{
				return;
			}
			wave8.Begin(this.WaveBar4, true);
		}

		private void UpdateProgressFill()
		{
			if (this.ProgressFillBorder != null && this.ProgressTrackBorder.ActualWidth > 0.0)
			{
				double val = this.ProgressTrackBorder.ActualWidth * this._viewModel.ProgressRatio;
				this.ProgressFillBorder.Width = Math.Max(0.0, val);
			}
		}

		private void IslandHitBox_MouseEnter(object sender, MouseEventArgs e)
		{
			this._viewModel.OnMouseEnterIsland();
		}

		private void IslandHitBox_MouseLeave(object sender, MouseEventArgs e)
		{
			this._viewModel.OnMouseLeaveIsland();
		}

		private void IslandHitBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
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

		private async void ProgressBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (this.ProgressTrackBorder != null && this.ProgressTrackBorder.ActualWidth > 0.0)
			{
				Point pos = e.GetPosition(this.ProgressTrackBorder);
				double ratio = Math.Clamp(pos.X / this.ProgressTrackBorder.ActualWidth, 0.0, 1.0);
				await this._viewModel.SeekToRatioAsync(ratio);
			}
		}

		private readonly MainViewModel _viewModel;
		private readonly AppSettings _settings;
		private readonly FullscreenDetectionService _fullscreenDetector;
		private bool _isHiddenByFullscreen;
		private bool _hasCustomPosition;
		private bool _isExpandingDownwards;
		private Storyboard? _wave1;
		private Storyboard? _wave2;
		private Storyboard? _wave3;
		private Storyboard? _wave4;

		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 128;
		public const double IslandBottomPadding = 56.0;
		public const double IslandTopPadding = 56.0;
	}
}
