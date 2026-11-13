using MultiSound.Audio.Devices;

namespace MultiSound.Tests;

public class DeviceKindClassifierTests
{
    [Test]
    [Arguments("BTHENUM", DeviceKind.Bluetooth)]
    [Arguments("BTHHFENUM", DeviceKind.Bluetooth)]
    [Arguments("BTHLEDEVICE", DeviceKind.Bluetooth)]
    [Arguments("BTHLE", DeviceKind.Bluetooth)]
    [Arguments("bthenum", DeviceKind.Bluetooth)]
    [Arguments("USB", DeviceKind.Usb)]
    [Arguments("HDAUDIO", DeviceKind.Wired)]
    [Arguments("", DeviceKind.Wired)]
    [Arguments(null, DeviceKind.Wired)]
    public async Task Classify(string? enumerator, DeviceKind expected)
    {
        await Assert.That(DeviceKindClassifier.Classify(enumerator)).IsEqualTo(expected);
    }
}
