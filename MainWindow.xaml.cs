using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
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

		private void UpdateIslandAlignment()
		{
			bool isTop = string.Equals(this._viewModel.IslandPosition, "Top", StringComparison.OrdinalIgnoreCase);
			if (isTop)
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

		private void RestorePinnedPosition()
		{
			this.UpdateIslandAlignment();
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
					return;
				}
			}
			this.RepositionWindow();
		}

		public void RepositionWindow()
		{
			this._hasCustomPosition = false;
			this.UpdateIslandAlignment();
			Rect workArea = SystemParameters.WorkArea;
			base.Left = workArea.Left + (workArea.Width - base.Width) / 2.0;
			if (string.Equals(this._viewModel.IslandPosition, "Top", StringComparison.OrdinalIgnoreCase))
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
			if (this._viewModel.IsPinned)
			{
				this._settings.PinnedLeft = base.Left;
				this._settings.PinnedTop = base.Top;
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
						this._settings.IsPositionPinned = true;
						this._settings.Save();
					}
					else
					{
						this._settings.IsPositionPinned = false;
						this._settings.PinnedLeft = null;
						this._settings.PinnedTop = null;
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
