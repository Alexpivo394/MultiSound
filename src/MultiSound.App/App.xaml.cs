using System;
using System.Threading;
using System.Windows;
using MultiSound.App.Settings;
using MultiSound.App.Tray;
using MultiSound.App.ViewModels;
using MultiSound.App.Views;
using MultiSound.Audio;
using MultiSound.Audio.Backend;
using MultiSound.Audio.Devices;
using ToadDesign;

namespace MultiSound.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\MultiSound.SingleInstance";
    private const string ShowEventName = @"Local\MultiSound.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private DeviceCatalog? _catalog;
    private WasapiBackend? _backend;
    private AudioEngine? _engine;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var firstInstance);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!firstInstance)
        {
            // Already running in the tray: bring that window up instead.
            _showEvent.Set();
            Shutdown();
            return;
        }

        ToadTheme.Mode = ToadThemeMode.System;

        _catalog = new DeviceCatalog();
        _backend = new WasapiBackend();
        _engine = new AudioEngine(_backend);
        _viewModel = new MainViewModel(_engine, _catalog, new DefaultDeviceSwitcher(), new SettingsStore(SettingsStore.DefaultPath),
            action => Dispatcher.BeginInvoke(action));

        _window = new MainWindow(_viewModel);
        _tray = new TrayIcon(_viewModel, ShowWindow, ExitApp);
        _window.HiddenToTray += (_, _) => _tray.ShowHiddenHintOnce();
        _window.Show();

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private void ShowWindow() => _window?.ShowAndActivate();

    private void ExitApp()
    {
        _viewModel?.Shutdown();
        _tray?.Dispose();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _engine?.Dispose();
        _catalog?.Dispose();
        _backend?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        Shutdown();
    }
}
