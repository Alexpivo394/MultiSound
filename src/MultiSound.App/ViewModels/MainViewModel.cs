using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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
    private readonly SettingsStore _store;
    private readonly Action<Action> _dispatch;
    private readonly Timer _saveTimer;
    private string? _originalDefaultId;
    private bool _refreshing;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _clicksEnabled;
    [ObservableProperty] private string? _masterHint;
    [ObservableProperty] private string? _bluetoothLoadHint;
    [ObservableProperty] private string _trayStatus = "Остановлено";

    public MainViewModel(IAudioEngine engine, IDeviceCatalog catalog, IDefaultDeviceSwitcher switcher, SettingsStore store, Action<Action> dispatch)
    {
        _engine = engine;
        _catalog = catalog;
        _switcher = switcher;
        _store = store;
        _dispatch = dispatch;
        Settings = store.Load();
        _saveTimer = new Timer(_ => _dispatch(SaveNow));

        _catalog.Changed += OnCatalogChanged;
        _engine.OutputFaulted += OnOutputFaulted;
        Refresh();
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>Loaded settings; the window stores its size and position here and calls <see cref="SaveSoon"/>.</summary>
    public AppSettings Settings { get; }

    public void SaveSoon() => _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);

    /// <summary>Stops playback, saves, and gives back the main device the user had before.</summary>
    public void Shutdown()
    {
        _catalog.Changed -= OnCatalogChanged;
        _engine.OutputFaulted -= OnOutputFaulted;
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

    private void OnOutputFaulted(OutputFault fault) => _dispatch(() =>
    {
        if (Devices.FirstOrDefault(d => d.Id == fault.DeviceId) is { } device) device.Error = fault.Message;
        UpdateStatus();
    });

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
                    if (!device.IsPresent) device.Error = null;
                }
                device.IsPresent = true;
                device.IsMaster = info.Id == defaultId;
            }

            foreach (var device in Devices.Where(d => present.All(p => p.Id != d.Id)))
            {
                device.IsPresent = false;
                device.IsMaster = false;
            }

            if (_engine.IsRunning && defaultId is not null && _engine.MasterId != defaultId)
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
            device.Volume = (int)Math.Round(saved.Volume * 100);
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

        Settings.Devices[device.Id] = new DeviceSettings(device.IsEnabled, device.Volume / 100f, device.OffsetMs);
        SaveSoon();
        if (e.PropertyName == nameof(DeviceViewModel.IsEnabled)) device.Error = null;
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
