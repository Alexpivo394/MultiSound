using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiSound.Audio;
using MultiSound.Audio.Devices;

namespace MultiSound.App.ViewModels;

/// <summary>One device card.</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    private readonly Action<DeviceViewModel> _makeMaster;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBluetooth))]
    private DeviceKind _kind;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isPresent = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCopy))]
    private bool _isMaster;

    /// <summary>Copy volume, 0–100 %.</summary>
    [ObservableProperty] private int _volume = 100;

    /// <summary>Copy offset, 0–500 ms.</summary>
    [ObservableProperty] private int _offsetMs;

    public DeviceViewModel(AudioDeviceInfo device, Action<DeviceViewModel> makeMaster)
    {
        Id = device.Id;
        _name = device.Name;
        _kind = device.Kind;
        _makeMaster = makeMaster;
    }

    public string Id { get; }

    public bool IsBluetooth => Kind == DeviceKind.Bluetooth;

    public bool IsCopy => !IsMaster;

    public OutputSettings ToOutputSettings() => new(Volume / 100f, OffsetMs);

    partial void OnVolumeChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value) Volume = clamped;
    }

    partial void OnOffsetMsChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, (int)CopyStream.MaxOffsetMs);
        if (clamped != value) OffsetMs = clamped;
    }

    [RelayCommand]
    private void MakeMaster() => _makeMaster(this);

    [RelayCommand]
    private void NudgeOffset(int delta) => OffsetMs += delta;
}
