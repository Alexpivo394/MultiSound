using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiSound.App.Settings;
using MultiSound.Audio;
using MultiSound.Audio.Devices;

namespace MultiSound.App.ViewModels;

/// <summary>The device list and the playback state; keeps the engine and the saved settings in step with the cards.</summary>
/// <remarks>All members run on the dispatcher; engine and catalog events are marshalled through <c>dispatch</c>.</remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IAudioEngine _engine;
    private readonly IDeviceCatalog _catalog;
    private readonly IDefaultDeviceSwitcher _switcher;
    private readonly IDeviceVolumes _volumes;
    private readonly SettingsStore _store;
    private readonly Action<Action> _dispatch;
    private readonly Timer _saveTimer;
    private readonly TimeSpan _retryDelay;
    private readonly HashSet<string> _retried = new();
    private string? _originalDefaultId;
    private bool _refreshing;
    private bool _shutDown;
    private bool _applyingOutsideVolume;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _clicksEnabled;
    [ObservableProperty] private string? _masterHint;
    [ObservableProperty] private string? _bluetoothLoadHint;
    [ObservableProperty] private string _trayStatus = "Остановлено";

    public MainViewModel(IAudioEngine engine, IDeviceCatalog catalog, IDefaultDeviceSwitcher switcher, IDeviceVolumes volumes, SettingsStore store, Action<Action> dispatch, TimeSpan? retryDelay = null)
    {
        _engine = engine;
        _catalog = catalog;
        _switcher = switcher;
        _volumes = volumes;
        _store = store;
        _dispatch = dispatch;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        Settings = store.Load();
        _saveTimer = new Timer(_ => _dispatch(SaveNow));

        _catalog.Changed += OnCatalogChanged;
        _engine.OutputFaulted += OnOutputFaulted;
        _volumes.Changed += OnVolumeChangedOutside;
        Refresh();
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>Loaded settings; the window stores its size and position here and calls <see cref="SaveSoon"/>.</summary>
    public AppSettings Settings { get; }

    public void SaveSoon() => _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

    /// <summary>Stops playback, saves, and gives back the main device the user had before.</summary>
    public void Shutdown()
    {
        _shutDown = true;
        _catalog.Changed -= OnCatalogChanged;
        _engine.OutputFaulted -= OnOutputFaulted;
        _volumes.Changed -= OnVolumeChangedOutside;
        _engine.Stop();
        _saveTimer.Dispose();
        SaveNow();

        if (_originalDefaultId is { } original &&
            _catalog.GetDefaultId() != original &&
            _catalog.GetRenderDevices().Any(d => d.Id == original))
        {
            try
            {
                _switcher.SetDefault(original);
            }
            catch (Exception)
            {
                // Nothing left to tell the user on exit; Windows keeps the current default.
            }
        }
    }

    partial void OnClicksEnabledChanged(bool value) => _engine.ClicksEnabled = value;

    [RelayCommand]
    private void ToggleRun()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
        }
        else if (_catalog.GetDefaultId() is { } masterId)
        {
            _engine.ClicksEnabled = ClicksEnabled;
            _engine.Start(masterId);
            SyncEngine();
        }
        UpdateStatus();
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => _dispatch(Refresh);

    private void OnVolumeChangedOutside(string deviceId, float level) => _dispatch(() =>
    {
        if (Devices.FirstOrDefault(d => d.Id == deviceId) is not { } device) return;
        var percent = ToPercent(level);
        if (device.Volume == percent) return;
        _applyingOutsideVolume = true;
        try
        {
            device.Volume = percent;
        }
        finally
        {
            _applyingOutsideVolume = false;
        }
    });

    private static int ToPercent(float level) => (int)Math.Round(level * 100);

    private void OnOutputFaulted(OutputFault fault) => _dispatch(() =>
    {
        if (Devices.FirstOrDefault(d => d.Id == fault.DeviceId) is { } device)
        {
            device.Error = fault.Message;
            ScheduleRetry(device);
        }
        UpdateStatus();
    });

    // A Bluetooth endpoint can report Active a moment before it can be opened, so a copy that failed on a device
    // still present gets one more try; the next reconnect or toggle allows another.
    private void ScheduleRetry(DeviceViewModel device)
    {
        if (device.IsMaster || !device.IsPresent || !device.IsEnabled || !_retried.Add(device.Id)) return;
        Task.Delay(_retryDelay).ContinueWith(_ => _dispatch(() =>
        {
            if (_shutDown || !_engine.IsRunning || device.IsMaster || !device.IsPresent || !device.IsEnabled) return;
            device.Error = null;
            SyncDevice(device);
            UpdateStatus();
        }), TaskScheduler.Default);
    }

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var present = _catalog.GetRenderDevices();
            var defaultId = _catalog.GetDefaultId();

            foreach (var info in present)
            {
                var device = Devices.FirstOrDefault(d => d.Id == info.Id);
                if (device is null)
                {
                    device = CreateDevice(info);
                    Devices.Add(device);
                }
                else
                {
                    device.Name = info.Name;
                    device.Kind = info.Kind;
                    if (!device.IsPresent)
                    {
                        device.Error = null;
                        _retried.Remove(device.Id);
                    }
                }
                device.IsPresent = true;
                if (_volumes.Get(info.Id) is { } level) device.Volume = ToPercent(level);
                device.IsMaster = info.Id == defaultId;
            }

            foreach (var device in Devices.Where(d => present.All(p => p.Id != d.Id)))
            {
                device.IsPresent = false;
                device.IsMaster = false;
            }

            // Also when the default is unchanged: the engine recaptures a main device that dropped and came back.
            if (_engine.IsRunning && defaultId is not null)
                _engine.ChangeMaster(defaultId);
        }
        finally
        {
            _refreshing = false;
        }
        SyncEngine();
        UpdateStatus();
    }

    private DeviceViewModel CreateDevice(AudioDeviceInfo info)
    {
        var device = new DeviceViewModel(info, MakeMaster);
        if (Settings.Devices.TryGetValue(info.Id, out var saved))
        {
            device.IsEnabled = saved.Enabled;
            device.OffsetMs = (int)Math.Round(saved.OffsetMs);
        }
        device.PropertyChanged += OnDevicePropertyChanged;
        return device;
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_refreshing || sender is not DeviceViewModel device) return;
        if (e.PropertyName is not (nameof(DeviceViewModel.IsEnabled) or nameof(DeviceViewModel.Volume) or nameof(DeviceViewModel.OffsetMs)))
            return;

        // Volume is the device's own Windows volume: Windows keeps it, nothing to save or to tell the engine.
        if (e.PropertyName == nameof(DeviceViewModel.Volume))
        {
            if (!_applyingOutsideVolume) _volumes.Set(device.Id, device.Volume / 100f);
            return;
        }

        Settings.Devices[device.Id] = new DeviceSettings(device.IsEnabled, device.OffsetMs);
        SaveSoon();
        if (e.PropertyName == nameof(DeviceViewModel.IsEnabled))
        {
            device.Error = null;
            _retried.Remove(device.Id);
        }
        SyncDevice(device);
        UpdateStatus();
    }

    private void MakeMaster(DeviceViewModel device)
    {
        try
        {
            _originalDefaultId ??= _catalog.GetDefaultId();
            _switcher.SetDefault(device.Id);
            device.Error = null;
        }
        catch (Exception e)
        {
            device.Error = $"Не удалось сделать основным: {e.Message}";
        }
        Refresh();
    }

    private void SyncEngine()
    {
        foreach (var device in Devices) SyncDevice(device);
    }

    private void SyncDevice(DeviceViewModel device)
    {
        if (!_engine.IsRunning || device.IsMaster) return;
        if (device.IsPresent && device.IsEnabled)
            _engine.SetOutput(device.Id, device.ToOutputSettings());
        else if (_engine.ActiveOutputs.Contains(device.Id))
            _engine.RemoveOutput(device.Id);
    }

    private void UpdateStatus()
    {
        IsRunning = _engine.IsRunning;

        var master = Devices.FirstOrDefault(d => d.IsMaster);
        var copies = Devices.Where(d => d.IsPresent && d.IsEnabled && !d.IsMaster).ToList();
        MasterHint = Hints.MasterHint(master is null ? null : ToInfo(master), copies.Select(ToInfo));
        BluetoothLoadHint = Hints.BluetoothLoadHint(copies.Count(d => d.IsBluetooth) + (master?.IsBluetooth == true ? 1 : 0));

        TrayStatus = IsRunning ? PlayingOn(1 + _engine.ActiveOutputs.Count) : "Остановлено";
    }

    private static AudioDeviceInfo ToInfo(DeviceViewModel device) => new(device.Id, device.Name, device.Kind);

    private static string PlayingOn(int count)
    {
        var lastTwo = count % 100;
        var last = count % 10;
        var noun = last == 1 && lastTwo != 11 ? "устройстве" : "устройствах";
        return $"Играет на {count} {noun}";
    }

    private void SaveNow()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; playback goes on.
        }
    }
}
