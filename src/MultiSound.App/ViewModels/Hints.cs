using System.Collections.Generic;
using System.Linq;
using MultiSound.Audio.Devices;

namespace MultiSound.App.ViewModels;

/// <summary>Advice shown above the device list.</summary>
public static class Hints
{
    /// <summary>A copy cannot play earlier than the main device, so the slowest device (Bluetooth) should be the main one.</summary>
    public static string? MasterHint(AudioDeviceInfo? master, IEnumerable<AudioDeviceInfo> selectedCopies) =>
        master is not null && master.Kind != DeviceKind.Bluetooth && selectedCopies.Any(d => d.Kind == DeviceKind.Bluetooth)
            ? "Сделайте основным Bluetooth-устройство — оно самое медленное, остальные подстроятся под него."
            : null;

    /// <param name="bluetoothSelectedCount">Bluetooth devices playing, the main device included.</param>
    public static string? BluetoothLoadHint(int bluetoothSelectedCount) =>
        bluetoothSelectedCount > 2 ? "Больше двух Bluetooth-наушников может заикаться — это ограничение адаптера." : null;
}
