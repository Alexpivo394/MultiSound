using MultiSound.Audio;
using MultiSound.Audio.Devices;

namespace MultiSound.Tests.Fakes;

internal sealed class FakeCatalog : IDeviceCatalog
{
    public List<AudioDeviceInfo> Devices { get; } = [];
    public string? DefaultId { get; set; }

    public event EventHandler? Changed;

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => Devices.ToList();
    public string? GetDefaultId() => DefaultId;
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { }
}

internal sealed class FakeSwitcher(FakeCatalog catalog) : IDefaultDeviceSwitcher
{
    public List<string> Calls { get; } = [];

    public void SetDefault(string deviceId)
    {
        Calls.Add(deviceId);
        catalog.DefaultId = deviceId;
        catalog.RaiseChanged();
    }
}

internal sealed class FakeEngine : IAudioEngine
{
    public Dictionary<string, OutputSettings> Outputs { get; } = new();
    public List<string> Removed { get; } = [];

    public bool IsRunning { get; private set; }
    public string? MasterId { get; private set; }
    public bool ClicksEnabled { get; set; }
    public IReadOnlyCollection<string> ActiveOutputs => IsRunning ? Outputs.Keys.Where(k => k != MasterId).ToList() : [];

    public event Action<OutputFault>? OutputFaulted;

    public void Start(string masterId)
    {
        IsRunning = true;
        MasterId = masterId;
    }

    public List<string> ChangeMasterCalls { get; } = [];

    public void ChangeMaster(string masterId)
    {
        ChangeMasterCalls.Add(masterId);
        MasterId = masterId;
    }
    public void Stop() => IsRunning = false;

    public void SetOutput(string deviceId, OutputSettings settings) => Outputs[deviceId] = settings;

    public void RemoveOutput(string deviceId)
    {
        Outputs.Remove(deviceId);
        Removed.Add(deviceId);
    }

    public void Fault(string deviceId, string message)
    {
        Outputs.Remove(deviceId);
        OutputFaulted?.Invoke(new OutputFault(deviceId, message));
    }

    public void Dispose() { }
}
