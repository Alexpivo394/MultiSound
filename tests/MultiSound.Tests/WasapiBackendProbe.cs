using MultiSound.Audio;
using MultiSound.Audio.Backend;
using MultiSound.Audio.Devices;

namespace MultiSound.Tests;

/// <summary>Manual check on real devices (silent: keep-alive only). Run with --treenode-filter "/*/*/WasapiBackendProbe/*".</summary>
public class WasapiBackendProbe
{
    [Test]
    [Explicit]
    public async Task EngineRunsOnRealDevices()
    {
        using var catalog = new DeviceCatalog();
        using var backend = new WasapiBackend();
        using var engine = new AudioEngine(backend);
        var faults = new List<OutputFault>();
        engine.OutputFaulted += faults.Add;

        var master = catalog.GetDefaultId()!;
        var other = catalog.GetRenderDevices().First(d => d.Id != master);
        engine.Start(master);
        engine.SetOutput(other.Id, new OutputSettings(1f, 100));
        await Task.Delay(3000);

        Console.WriteLine($"active: {string.Join(", ", engine.ActiveOutputs)}; faults: {string.Join("; ", faults)}");
        await Assert.That(faults).IsEmpty();
        await Assert.That(engine.ActiveOutputs).Contains(other.Id);
    }
}
