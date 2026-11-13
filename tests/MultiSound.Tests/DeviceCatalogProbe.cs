using MultiSound.Audio.Devices;

namespace MultiSound.Tests;

/// <summary>Manual check against the real machine's devices; run with --treenode-filter "/*/*/DeviceCatalogProbe/*".</summary>
public class DeviceCatalogProbe
{
    [Test]
    [Explicit]
    public async Task PrintDevices()
    {
        using var catalog = new DeviceCatalog();
        Console.WriteLine($"default: {catalog.GetDefaultId()}");
        foreach (var device in catalog.GetRenderDevices())
            Console.WriteLine($"{device.Kind,-10} {device.Name} | {device.Id}");
        await Assert.That(catalog.GetRenderDevices()).IsNotNull();
    }
}
