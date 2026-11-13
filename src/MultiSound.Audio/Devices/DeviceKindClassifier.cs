namespace MultiSound.Audio.Devices;

/// <summary>Tells Bluetooth, USB, and built-in outputs apart by the bus enumerator that created the device.</summary>
public static class DeviceKindClassifier
{
    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHHFENUM", "BTHLEDEVICE", "BTHLE"];

    public static DeviceKind Classify(string? enumeratorName)
    {
        if (string.IsNullOrEmpty(enumeratorName)) return DeviceKind.Wired;
        if (BluetoothEnumerators.Any(e => string.Equals(e, enumeratorName, StringComparison.OrdinalIgnoreCase)))
            return DeviceKind.Bluetooth;
        return string.Equals(enumeratorName, "USB", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Usb : DeviceKind.Wired;
    }
}
