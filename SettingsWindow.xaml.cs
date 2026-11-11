using System;
using System.Windows;
using System.Windows.Input;
using DynamicIslandMedia.Models;
using DynamicIslandMedia.ViewModels;

namespace DynamicIslandMedia
{
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly AppSettings _settings;
        private bool _isInitializing;

        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _settings = viewModel.Settings;
            DataContext = viewModel;

            _isInitializing = true;
            try
            {
                // Load values
                if (string.Equals(_settings.IslandPosition, AppSettings.PositionTop, StringComparison.OrdinalIgnoreCase))
                {
                    TopPositionRadio.IsChecked = true;
                }
                else
                {
                    BottomPositionRadio.IsChecked = true;
                }

                BottomMarginSlider.Value = _settings.EdgeDistance;
                AutoExpandCheckBox.IsChecked = _settings.AutoExpandOnHover;
                HoverExpandDelaySlider.Value = _settings.HoverExpandDelayMs;
                HoverCollapseDelaySlider.Value = _settings.HoverCollapseDelayMs;
                AutoHideCheckBox.IsChecked = _settings.AutoHideWhenIdle;
                StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
                EnableGradientCheckBox.IsChecked = _settings.EnableGradient;
                EnableProgressGlowCheckBox.IsChecked = _settings.EnableProgressGlow;
                CompactOpacitySlider.Value = _settings.CompactBackgroundOpacityPercent;
                ExpandedOpacitySlider.Value = _settings.ExpandedBackgroundOpacityPercent;

                UpdateDistanceLabel();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PositionRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateDistanceLabel();
            _viewModel.IslandPosition = (TopPositionRadio?.IsChecked == true) ? AppSettings.PositionTop : AppSettings.PositionBottom;
        }

        private void BottomMarginSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            _viewModel.EdgeDistance = e.NewValue;
        }

        private void UpdateDistanceLabel()
        {
            if (DistanceLabel == null) return;
            DistanceLabel.Text = (TopPositionRadio?.IsChecked == true)
                ? "Distance from Top Screen Edge:"
                : "Distance from Bottom Screen Edge:";
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            BottomPositionRadio.IsChecked = true;
            _viewModel.IslandPosition = AppSettings.PositionBottom;
            BottomMarginSlider.Value = AppSettings.DefaultEdgeDistance;
            _viewModel.EdgeDistance = AppSettings.DefaultEdgeDistance;
            AutoExpandCheckBox.IsChecked = true;
            HoverExpandDelaySlider.Value = 150;
            HoverCollapseDelaySlider.Value = 350;
            AutoHideCheckBox.IsChecked = false;
            StartWithWindowsCheckBox.IsChecked = false;
            EnableGradientCheckBox.IsChecked = true;
            EnableProgressGlowCheckBox.IsChecked = true;
            CompactOpacitySlider.Value = 85;
            ExpandedOpacitySlider.Value = 20;
            UpdateDistanceLabel();
            _viewModel.ResetPositionToDefault();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.IslandPosition = (TopPositionRadio.IsChecked == true) ? AppSettings.PositionTop : AppSettings.PositionBottom;
            _settings.EdgeDistance = BottomMarginSlider.Value;
            _settings.AutoExpandOnHover = AutoExpandCheckBox.IsChecked ?? true;
            _settings.HoverExpandDelayMs = (int)HoverExpandDelaySlider.Value;
            _settings.HoverCollapseDelayMs = (int)HoverCollapseDelaySlider.Value;
            _settings.AutoHideWhenIdle = AutoHideCheckBox.IsChecked ?? false;
            _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;
            _settings.EnableGradient = EnableGradientCheckBox.IsChecked ?? true;
            _settings.EnableProgressGlow = EnableProgressGlowCheckBox.IsChecked ?? true;
            _settings.CompactBackgroundOpacityPercent = CompactOpacitySlider.Value;
            _settings.ExpandedBackgroundOpacityPercent = ExpandedOpacitySlider.Value;

            _settings.Save();
            _viewModel.RefreshHoverDelaysFromSettings();
            _viewModel.IslandPosition = _settings.IslandPosition;
            _viewModel.EdgeDistance = _settings.EdgeDistance;
            _viewModel.UpdatePresenceVisibility();

            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                mw.RefreshAppearanceFromSettings();
            }

            Close();
        }
    }
}
