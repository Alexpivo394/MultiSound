using System.IO;
using MultiSound.App.Settings;

namespace MultiSound.Tests;

public class SettingsStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "MultiSoundTests", Guid.NewGuid().ToString("N"), "settings.json");

    [Test]
    public async Task Missing_ReturnsDefaults()
    {
        var settings = new SettingsStore(TempPath()).Load();
        await Assert.That(settings.Devices).IsEmpty();
        await Assert.That(settings.WindowWidth).IsNull();
        await Assert.That(settings.TrayHintShown).IsFalse();
    }

    [Test]
    public async Task RoundTrip_PreservesDevicesAndWindow()
    {
        var store = new SettingsStore(TempPath());
        var settings = new AppSettings { WindowLeft = 10, WindowTop = 20, WindowWidth = 600, WindowHeight = 500, TrayHintShown = true };
        settings.Devices["{0.0.0.00000000}.{abc}"] = new DeviceSettings(Enabled: true, Volume: 0.8f, OffsetMs: 140);
        store.Save(settings);

        var loaded = store.Load();
        await Assert.That(loaded.Devices["{0.0.0.00000000}.{abc}"]).IsEqualTo(new DeviceSettings(true, 0.8f, 140));
        await Assert.That(loaded.WindowLeft).IsEqualTo(10);
        await Assert.That(loaded.WindowHeight).IsEqualTo(500);
        await Assert.That(loaded.TrayHintShown).IsTrue();
    }

    [Test]
    public async Task Corrupt_ReturnsDefaultsAndKeepsBackup()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json");

        var settings = new SettingsStore(path).Load();
        await Assert.That(settings.Devices).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(path + ".bak")).IsEqualTo("{ not json");
    }

    [Test]
    public async Task Save_CreatesDirectory()
    {
        var path = TempPath();
        new SettingsStore(path).Save(new AppSettings());
        await Assert.That(File.Exists(path)).IsTrue();
    }
}
