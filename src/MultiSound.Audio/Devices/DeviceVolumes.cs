using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MultiSound.Audio.Devices;

/// <summary>The Windows volume of each output device, as on the volume slider in Windows (0–1).</summary>
public interface IDeviceVolumes : IDisposable
{
    /// <summary>The device's volume, or null when the device cannot be reached.</summary>
    float? Get(string deviceId);

    void Set(string deviceId, float level);

    /// <summary>Raised on a COM thread when a device's volume changes, including changes made by <see cref="Set"/>.</summary>
    event Action<string, float>? Changed;
}

/// <summary>Device volumes through each endpoint's <see cref="AudioEndpointVolume"/>.</summary>
public sealed class DeviceVolumes : IDeviceVolumes
{
    private readonly object _sync = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, MMDevice> _devices = new();

    public event Action<string, float>? Changed;

    public float? Get(string deviceId)
    {
        lock (_sync)
        {
            return Use(deviceId, device => device.AudioEndpointVolume.MasterVolumeLevelScalar);
        }
    }

    public void Set(string deviceId, float level)
    {
        lock (_sync)
        {
            Use(deviceId, device => device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var device in _devices.Values) device.Dispose();
            _devices.Clear();
            _enumerator.Dispose();
        }
    }

    // A device that disconnected leaves a dead COM object behind: drop it and open the device again once.
    private T? Use<T>(string deviceId, Func<MMDevice, T> action) where T : struct
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return action(Open(deviceId));
            }
            catch (COMException)
            {
                Forget(deviceId);
            }
        }
        return null;
    }

    private MMDevice Open(string deviceId)
    {
        if (_devices.TryGetValue(deviceId, out var device)) return device;
        device = _enumerator.GetDevice(deviceId);
        device.AudioEndpointVolume.OnVolumeNotification += data => Changed?.Invoke(deviceId, data.MasterVolume);
        _devices[deviceId] = device;
        return device;
    }

    private void Forget(string deviceId)
    {
        if (!_devices.Remove(deviceId, out var device)) return;
        try
        {
            device.Dispose();
        }
        catch (COMException)
        {
            // Already gone.
        }
    }
}
