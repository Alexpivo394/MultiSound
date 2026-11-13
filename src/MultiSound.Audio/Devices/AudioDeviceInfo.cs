namespace MultiSound.Audio.Devices;

public enum DeviceKind
{
    Wired,
    Usb,
    Bluetooth,
}

/// <summary>An active Windows audio output device.</summary>
/// <param name="Id">Windows endpoint ID; stable across restarts and reconnects.</param>
public sealed record AudioDeviceInfo(string Id, string Name, DeviceKind Kind);
