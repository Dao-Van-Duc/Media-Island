using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using DynamicIslandMedia.Models;
using DynamicIslandMedia.Services;
using DynamicIslandMedia.ViewModels;

namespace DynamicIslandMedia
{
    public partial class App : Application
    {
        private const string AppMutexName = "DynamicIslandMedia_SingleInstance_Mutex";
        private Mutex? _appMutex;
        private WindowsMediaService? _mediaService;
        private TrayIconService? _trayIconService;
        private MainViewModel? _viewModel;
        private MainWindow? _mainWindow;
        private SettingsWindow? _settingsWindow;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Ensure single instance
            _appMutex = new Mutex(true, AppMutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Media Island is already running in the system tray.", "Media Island", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            try
            {
                // 1. Load persistent user settings
                var settings = AppSettings.Load();

                // 2. Initialize media transport controls service
                _mediaService = new WindowsMediaService();

                // 3. Initialize view model
                _viewModel = new MainViewModel(_mediaService, settings);

                // 4. Initialize main window
                _mainWindow = new MainWindow(_viewModel);

                // 5. Initialize system tray icon service
                _trayIconService = new TrayIconService();

                // Connect tray icon events
                _trayIconService.ToggleIslandRequested += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _viewModel.IsMasterEnabled = !_viewModel.IsMasterEnabled;
                    });
                };

                _trayIconService.ToggleExpandRequested += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_viewModel.IsMasterEnabled && _viewModel.IsVisible)
                        {
                            _viewModel.IsExpanded = !_viewModel.IsExpanded;
                        }
                    });
                };

                _trayIconService.ResetPositionRequested += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _viewModel.ResetPositionToDefault();
                    });
                };

                _trayIconService.ShowSettingsRequested += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                        {
                            _settingsWindow = new SettingsWindow(_viewModel);
                            _settingsWindow.Closed += (s, ev) => _settingsWindow = null;
                            _settingsWindow.Show();
                        }
                        else
                        {
                            _settingsWindow.Activate();
                        }
                    });
                };

                _trayIconService.ExitRequested += () =>
                {
                    Dispatcher.Invoke(Shutdown);
                };

                // Sync tray icon state with media
                _mediaService.MediaChanged += (s, media) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _trayIconService.UpdateState(media, _viewModel.IsMasterEnabled, _viewModel.IsVisible, _viewModel.IsExpanded);
                    });
                };

                _viewModel.PropertyChanged += (s, ev) =>
                {
                    if (ev.PropertyName is nameof(MainViewModel.IsMasterEnabled) or nameof(MainViewModel.IsVisible) or nameof(MainViewModel.IsExpanded))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _trayIconService.UpdateState(_viewModel.CurrentMedia, _viewModel.IsMasterEnabled, _viewModel.IsVisible, _viewModel.IsExpanded);
                        });
                    }
                };

                // Initial tray icon state
                _trayIconService.UpdateState(_viewModel.CurrentMedia, _viewModel.IsMasterEnabled, _viewModel.IsVisible, _viewModel.IsExpanded);

                // Show main window anchored at bottom of primary screen
                _mainWindow.Show();

                // Start media listener
                await _mediaService.InitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App Startup Exception] {ex}");
                MessageBox.Show($"Failed to initialize Media Island: {ex.Message}", "Media Island Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _mediaService?.Dispose();

            if (_appMutex != null)
            {
                try
                {
                    _appMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore mutex release errors on app exit
                }
                _appMutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}
