using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MultiSound.Audio.Devices;

public interface IDeviceCatalog : IDisposable
{
    /// <summary>Active output devices.</summary>
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    /// <summary>ID of the Windows default output device (multimedia role), or null when there is none.</summary>
    string? GetDefaultId();

    /// <summary>Raised on a COM thread, debounced, after a device appears, disappears, or becomes the default.</summary>
    event EventHandler? Changed;
}

/// <summary>Windows output devices, read through the MMDevice API.</summary>
public sealed class DeviceCatalog : IDeviceCatalog, IMMNotificationClient
{
    private static readonly PropertyKey EnumeratorNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Timer _debounce;

    public DeviceCatalog()
    {
        _debounce = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        var result = new List<AudioDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, DeviceKindClassifier.Classify(ReadEnumerator(device))));
            }
        }
        return result;
    }

    public string? GetDefaultId()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (COMException)
        {
            return null; // No output device at all.
        }
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _debounce.Dispose();
        _enumerator.Dispose();
    }

    private static string? ReadEnumerator(MMDevice device)
    {
        try
        {
            var properties = device.Properties;
            return properties.Contains(EnumeratorNameKey) ? properties[EnumeratorNameKey].Value as string : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void Signal() => _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => Signal();
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => Signal();
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Signal();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Signal();
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }
}
