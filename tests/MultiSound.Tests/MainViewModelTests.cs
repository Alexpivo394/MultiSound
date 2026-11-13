using System.IO;
using MultiSound.App.Settings;
using MultiSound.App.ViewModels;
using MultiSound.Audio;
using MultiSound.Audio.Devices;
using MultiSound.Tests.Fakes;

namespace MultiSound.Tests;

public class MainViewModelTests
{
    private static readonly AudioDeviceInfo Bt = new("bt", "Sony WH-1000XM4", DeviceKind.Bluetooth);
    private static readonly AudioDeviceInfo Pods = new("pods", "AirPods", DeviceKind.Bluetooth);
    private static readonly AudioDeviceInfo Jack = new("jack", "Наушники", DeviceKind.Wired);
    private static readonly AudioDeviceInfo Usb = new("usb", "USB-гарнитура", DeviceKind.Usb);

    private sealed class Rig
    {
        public FakeCatalog Catalog { get; } = new();
        public FakeEngine Engine { get; } = new();
        public FakeSwitcher Switcher { get; }
        public FakeVolumes Volumes { get; } = new();
        public SettingsStore Store { get; } =
            new(Path.Combine(Path.GetTempPath(), "MultiSoundTests", Guid.NewGuid().ToString("N"), "settings.json"));

        public Rig(string defaultId, params AudioDeviceInfo[] devices)
        {
            Switcher = new FakeSwitcher(Catalog);
            Catalog.Devices.AddRange(devices);
            Catalog.DefaultId = defaultId;
        }

        public MainViewModel Create() =>
            new(Engine, Catalog, Switcher, Volumes, Store, action => { lock (this) action(); }, retryDelay: TimeSpan.FromMilliseconds(100));

        public DeviceViewModel Device(MainViewModel vm, string id) => vm.Devices.Single(d => d.Id == id);
    }

    [Test]
    public async Task Hints_WiredMasterWithBluetoothCopy_SuggestsBluetoothMaster()
    {
        await Assert.That(Hints.MasterHint(Jack, [Bt])).IsNotNull();
        await Assert.That(Hints.MasterHint(Bt, [Jack])).IsNull();
        await Assert.That(Hints.MasterHint(Jack, [Usb])).IsNull();
    }

    [Test]
    public async Task Hints_MoreThanTwoBluetooth_WarnsAboutAdapter()
    {
        await Assert.That(Hints.BluetoothLoadHint(3)).IsNotNull();
        await Assert.That(Hints.BluetoothLoadHint(2)).IsNull();
    }

    [Test]
    public async Task Load_AppliesSavedSettingsPerDevice()
    {
        var rig = new Rig("bt", Bt, Jack);
        var saved = new AppSettings();
        saved.Devices["jack"] = new DeviceSettings(Enabled: true, OffsetMs: 190);
        rig.Store.Save(saved);

        var vm = rig.Create();
        var jack = rig.Device(vm, "jack");
        await Assert.That(jack.IsEnabled).IsTrue();
        await Assert.That(jack.Volume).IsEqualTo(100); // no Windows level known: full
        await Assert.That(jack.OffsetMs).IsEqualTo(190);
        await Assert.That(rig.Device(vm, "bt").IsMaster).IsTrue();
    }

    [Test]
    public async Task ToggleRun_StartsEngineWithEnabledCopies()
    {
        var rig = new Rig("bt", Bt, Jack, Usb);
        var vm = rig.Create();
        rig.Device(vm, "bt").IsEnabled = true;
        rig.Device(vm, "jack").IsEnabled = true;
        rig.Device(vm, "jack").OffsetMs = 150;

        vm.ToggleRunCommand.Execute(null);

        await Assert.That(vm.IsRunning).IsTrue();
        await Assert.That(rig.Engine.MasterId).IsEqualTo("bt");
        await Assert.That(rig.Engine.Outputs.Keys).IsEquivalentTo(new[] { "jack" });
        await Assert.That(rig.Engine.Outputs["jack"].OffsetMs).IsEqualTo(150);
        await Assert.That(vm.TrayStatus).IsEqualTo("Играет на 2 устройствах");
    }

    [Test]
    public async Task OffsetChange_WhileRunning_UpdatesEngine()
    {
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "jack").IsEnabled = true;
        vm.ToggleRunCommand.Execute(null);

        rig.Device(vm, "jack").NudgeOffsetCommand.Execute(10);
        rig.Device(vm, "jack").Volume = 50;

        await Assert.That(rig.Engine.Outputs["jack"]).IsEqualTo(new OutputSettings(1f, 10));
        await Assert.That(rig.Volumes.Levels["jack"]).IsEqualTo(0.5f);
    }

    [Test]
    public async Task Volume_ComesFromWindowsDeviceVolume()
    {
        var rig = new Rig("bt", Bt, Jack);
        rig.Volumes.Levels["bt"] = 0.6f;
        rig.Volumes.Levels["jack"] = 0.3f;

        var vm = rig.Create();
        await Assert.That(rig.Device(vm, "bt").Volume).IsEqualTo(60);
        await Assert.That(rig.Device(vm, "jack").Volume).IsEqualTo(30);
    }

    [Test]
    public async Task VolumeSlider_SetsWindowsVolume_ForMasterToo()
    {
        var rig = new Rig("bt", Bt, Jack);
        rig.Volumes.Levels["bt"] = 0.6f;
        var vm = rig.Create();

        rig.Device(vm, "bt").Volume = 75;
        await Assert.That(rig.Volumes.Levels["bt"]).IsEqualTo(0.75f);
    }

    [Test]
    public async Task VolumeChangedOutside_SliderFollows()
    {
        var rig = new Rig("bt", Bt, Jack);
        rig.Volumes.Levels["jack"] = 0.3f;
        var vm = rig.Create();

        rig.Volumes.ChangeOutside("jack", 0.9f);
        await Assert.That(rig.Device(vm, "jack").Volume).IsEqualTo(90);
    }

    [Test]
    public async Task Disable_WhileRunning_RemovesFromEngine()
    {
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "jack").IsEnabled = true;
        vm.ToggleRunCommand.Execute(null);

        rig.Device(vm, "jack").IsEnabled = false;
        await Assert.That(rig.Engine.Outputs).IsEmpty();
    }

    [Test]
    public async Task DeviceGone_RemovedFromEngine_SettingsKept()
    {
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        var jack = rig.Device(vm, "jack");
        jack.IsEnabled = true;
        jack.OffsetMs = 120;
        vm.ToggleRunCommand.Execute(null);

        rig.Catalog.Devices.Remove(Jack);
        rig.Catalog.RaiseChanged();

        await Assert.That(rig.Engine.Outputs.ContainsKey("jack")).IsFalse();
        await Assert.That(jack.IsPresent).IsFalse();
        vm.Shutdown();
        await Assert.That(rig.Store.Load().Devices["jack"]).IsEqualTo(new DeviceSettings(true, 120));
    }

    [Test]
    public async Task DeviceReturns_OutputResumed()
    {
        var rig = new Rig("bt", Bt, Pods);
        var vm = rig.Create();
        rig.Device(vm, "pods").IsEnabled = true;
        rig.Device(vm, "pods").OffsetMs = 40;
        vm.ToggleRunCommand.Execute(null);

        rig.Engine.Fault("pods", "Устройство отключено");
        rig.Catalog.Devices.Remove(Pods);
        rig.Catalog.RaiseChanged();
        rig.Catalog.Devices.Add(Pods);
        rig.Catalog.RaiseChanged();

        await Assert.That(rig.Engine.Outputs["pods"]).IsEqualTo(new OutputSettings(1f, 40));
        await Assert.That(rig.Device(vm, "pods").Error).IsNull();
    }

    [Test]
    public async Task MasterChangedInWindows_EngineFollows()
    {
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "bt").IsEnabled = true;
        vm.ToggleRunCommand.Execute(null);

        rig.Catalog.DefaultId = "jack";
        rig.Catalog.RaiseChanged();

        await Assert.That(rig.Engine.MasterId).IsEqualTo("jack");
        await Assert.That(rig.Device(vm, "jack").IsMaster).IsTrue();
        await Assert.That(rig.Engine.Outputs.ContainsKey("bt")).IsTrue();
    }

    [Test]
    public async Task MasterReturns_SameDefault_EngineAskedToRecapture()
    {
        // The main headset dropped (engine lost capture) and came back as the default again.
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        vm.ToggleRunCommand.Execute(null);

        rig.Catalog.RaiseChanged();
        await Assert.That(rig.Engine.ChangeMasterCalls).Contains("bt");
    }

    [Test]
    public async Task MakeMaster_ThenShutdown_RestoresOriginal()
    {
        var rig = new Rig("jack", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "bt").MakeMasterCommand.Execute(null);
        rig.Device(vm, "jack").MakeMasterCommand.Execute(null);
        rig.Device(vm, "bt").MakeMasterCommand.Execute(null);

        vm.Shutdown();
        await Assert.That(rig.Switcher.Calls).IsEquivalentTo(new[] { "bt", "jack", "bt", "jack" });
    }

    [Test]
    public async Task Shutdown_OriginalGone_NoRestore()
    {
        var rig = new Rig("jack", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "bt").MakeMasterCommand.Execute(null);
        rig.Catalog.Devices.Remove(Jack);

        vm.Shutdown();
        await Assert.That(rig.Switcher.Calls).IsEquivalentTo(new[] { "bt" });
    }

    [Test]
    public async Task Shutdown_NeverSwitched_NoRestore()
    {
        var rig = new Rig("jack", Bt, Jack);
        var vm = rig.Create();
        vm.Shutdown();
        await Assert.That(rig.Switcher.Calls).IsEmpty();
    }

    [Test]
    public async Task Fault_ShowsErrorOnCard()
    {
        var rig = new Rig("bt", Bt, Jack);
        var vm = rig.Create();
        rig.Device(vm, "jack").IsEnabled = true;
        vm.ToggleRunCommand.Execute(null);

        rig.Engine.Fault("jack", "Устройство отключено");

        await Assert.That(rig.Device(vm, "jack").Error).IsEqualTo("Устройство отключено");
        await Assert.That(vm.TrayStatus).IsEqualTo("Играет на 1 устройстве");
    }

    [Test]
    public async Task Fault_OnPresentDevice_RetriedOnce()
    {
        // A Bluetooth endpoint can report Active before it can actually be opened.
        var rig = new Rig("bt", Bt, Pods);
        var vm = rig.Create();
        rig.Device(vm, "pods").IsEnabled = true;
        vm.ToggleRunCommand.Execute(null);

        rig.Engine.Fault("pods", "not ready");
        await Task.Delay(400);
        await Assert.That(rig.Engine.Outputs.ContainsKey("pods")).IsTrue();

        rig.Engine.Fault("pods", "still broken");
        await Task.Delay(400);
        await Assert.That(rig.Engine.Outputs.ContainsKey("pods")).IsFalse();
        await Assert.That(rig.Device(vm, "pods").Error).IsEqualTo("still broken");
    }

    [Test]
    public async Task HintsFollowSelection()
    {
        var rig = new Rig("jack", Bt, Pods, Jack);
        var vm = rig.Create();
        await Assert.That(vm.MasterHint).IsNull();

        rig.Device(vm, "bt").IsEnabled = true;
        await Assert.That(vm.MasterHint).IsNotNull();

        rig.Device(vm, "bt").MakeMasterCommand.Execute(null);
        rig.Device(vm, "pods").IsEnabled = true;
        await Assert.That(vm.MasterHint).IsNull();
        await Assert.That(vm.BluetoothLoadHint).IsNull();
    }
}
