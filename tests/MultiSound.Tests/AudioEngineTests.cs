using MultiSound.Audio;
using MultiSound.Audio.Backend;
using MultiSound.Tests.Fakes;

namespace MultiSound.Tests;

public class AudioEngineTests
{
    private const string Master = "master";

    private static readonly OutputSettings Default = new(Volume: 1f, OffsetMs: 0);

    /// <summary>Feeds the capture and pulls a render in lockstep until the copy has passed its delay.</summary>
    private static float[] Exchange(FakeCapture capture, FakeRender render, int blocks = 20)
    {
        var sine = Signals.StereoSine(440, 48000, 480);
        float[] last = [];
        for (var i = 0; i < blocks; i++)
        {
            capture.Push(sine);
            last = render.Pull(480);
        }
        return last;
    }

    [Test]
    public async Task Start_CreatesLoopbackAndMasterSignalOnMaster()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);

        await Assert.That(engine.IsRunning).IsTrue();
        await Assert.That(backend.LastCapture.DeviceId).IsEqualTo(Master);
        await Assert.That(backend.LastCapture.Started).IsTrue();
        await Assert.That(backend.Live(Master).Single().Source).IsTypeOf<MasterSignal>();
    }

    [Test]
    public async Task SetOutput_CreatesRenderAndReceivesCapturedAudio()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("a", Default);

        var output = Exchange(backend.LastCapture, backend.Live("a").Single());
        await Assert.That(output.Max(Math.Abs)).IsGreaterThan(0.5f);
        await Assert.That(engine.ActiveOutputs).IsEquivalentTo(new[] { "a" });
    }

    [Test]
    public async Task SetOutput_BeforeStart_AppliedOnStart()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.SetOutput("a", Default);
        await Assert.That(backend.Renders).IsEmpty();

        engine.Start(Master);
        await Assert.That(backend.Live("a").Count()).IsEqualTo(1);
    }

    [Test]
    public async Task SetOutput_ForMaster_Ignored()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput(Master, Default);

        await Assert.That(backend.Live(Master).Count()).IsEqualTo(1);
        await Assert.That(engine.ActiveOutputs).IsEmpty();
    }

    [Test]
    public async Task SetOutput_Existing_UpdatesInPlace()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("a", Default);
        engine.SetOutput("a", new OutputSettings(0.3f, 120));

        var render = backend.Live("a").Single();
        var stream = (CopyStream)render.Source!;
        await Assert.That(stream.OffsetMs).IsEqualTo(120);
        await Assert.That(stream.Volume).IsEqualTo(0.3f);
    }

    [Test]
    public async Task RenderFault_OtherOutputsKeepRunning()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        var faults = new List<OutputFault>();
        engine.OutputFaulted += faults.Add;
        engine.Start(Master);
        engine.SetOutput("a", Default);
        engine.SetOutput("b", Default);

        var a = backend.Live("a").Single();
        a.Fail(new InvalidOperationException("Устройство отключено"));
        await Task.Delay(100); // disposal runs off the failing thread

        await Assert.That(faults.Single()).IsEqualTo(new OutputFault("a", "Устройство отключено"));
        await Assert.That(a.Disposed).IsTrue();
        await Assert.That(engine.ActiveOutputs).IsEquivalentTo(new[] { "b" });
        var output = Exchange(backend.LastCapture, backend.Live("b").Single());
        await Assert.That(output.Max(Math.Abs)).IsGreaterThan(0.5f);
    }

    [Test]
    public async Task CreateRenderFailure_ReportedAsFault()
    {
        var backend = new FakeBackend();
        backend.FailingDevices.Add("a");
        using var engine = new AudioEngine(backend);
        var faults = new List<OutputFault>();
        engine.OutputFaulted += faults.Add;
        engine.Start(Master);
        engine.SetOutput("a", Default);

        await Assert.That(faults.Single().DeviceId).IsEqualTo("a");
        await Assert.That(engine.ActiveOutputs).IsEmpty();
    }

    [Test]
    public async Task MasterChanged_NewMasterIsRemovedFromCopies()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("a", Default);
        engine.SetOutput("b", Default);
        var oldCapture = backend.LastCapture;
        var oldA = backend.Live("a").Single();

        engine.ChangeMaster("a");

        await Assert.That(oldCapture.Disposed).IsTrue();
        await Assert.That(backend.LastCapture.DeviceId).IsEqualTo("a");
        await Assert.That(oldA.Disposed).IsTrue();
        await Assert.That(backend.Live("a").Single().Source).IsTypeOf<MasterSignal>();
        await Assert.That(engine.ActiveOutputs).IsEquivalentTo(new[] { "b" });
        await Assert.That(engine.MasterId).IsEqualTo("a");
    }

    [Test]
    public async Task MasterChanged_OldMasterWantedAsCopy_BecomesCopy()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.SetOutput(Master, Default); // wanted as a copy, ignored while it is the master
        engine.Start(Master);
        engine.ChangeMaster("a");

        await Assert.That(engine.ActiveOutputs).IsEquivalentTo(new[] { Master });
    }

    [Test]
    public async Task CaptureFault_RestartsOnce()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        var faults = new List<OutputFault>();
        engine.OutputFaulted += faults.Add;
        engine.Start(Master);

        backend.LastCapture.Fail(new InvalidOperationException("glitch"));
        await Task.Delay(100); // failures are handled on the thread pool
        await Assert.That(backend.Captures.Count).IsEqualTo(2);
        await Assert.That(engine.IsRunning).IsTrue();

        backend.LastCapture.Fail(new InvalidOperationException("dead"));
        await Task.Delay(100);
        await Assert.That(backend.Captures.Count).IsEqualTo(2);
        await Assert.That(faults.Single().DeviceId).IsEqualTo(Master);
    }

    [Test]
    public async Task MasterLost_CopiesSurviveUntilNewMaster()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("b", Default);
        var b = backend.Live("b").Single();

        // The main Bluetooth headset drops: capture fails, and its restart fails too.
        backend.LastCapture.Fail(new InvalidOperationException("device invalidated"));
        await Task.Delay(100);
        backend.LastCapture.Fail(new InvalidOperationException("device invalidated"));
        await Task.Delay(100);

        await Assert.That(engine.IsRunning).IsTrue();
        await Assert.That(b.Disposed).IsFalse();

        // Windows picks a new default, the view model follows it.
        engine.ChangeMaster("a");
        await Assert.That(backend.LastCapture.DeviceId).IsEqualTo("a");
        await Assert.That(backend.LastCapture.Disposed).IsFalse();
        await Assert.That(engine.ActiveOutputs).IsEquivalentTo(new[] { "b" });
    }

    [Test]
    public async Task MasterLost_SameMasterReturns_CaptureRestarts()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        backend.LastCapture.Fail(new InvalidOperationException("gone"));
        await Task.Delay(100);
        backend.LastCapture.Fail(new InvalidOperationException("gone"));
        await Task.Delay(100);
        var before = backend.Captures.Count;

        engine.ChangeMaster(Master);
        await Assert.That(backend.Captures.Count).IsEqualTo(before + 1);
        await Assert.That(backend.LastCapture.Disposed).IsFalse();
    }

    [Test]
    public async Task MasterChanged_DifferentRate_CopiesFollowNewRate()
    {
        var backend = new FakeBackend();
        backend.CaptureRates["a"] = 44100;
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("b", Default);

        engine.ChangeMaster("a");

        var stream = (CopyStream)backend.Live("b").Single().Source!;
        await Assert.That(stream.InputSampleRate).IsEqualTo(44100);
    }

    [Test]
    public async Task RenderFault_WhileStopHoldsEngine_DoesNotDeadlock()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("a", Default);
        var a = backend.Live("a").Single();

        // Like WasapiOut: disposing waits for the playback thread, which is reporting a failure right then.
        a.OnDispose = () =>
        {
            var reporter = new Thread(() => a.Fail(new InvalidOperationException("gone")));
            reporter.Start();
            reporter.Join();
        };

        var stop = Task.Run(engine.Stop);
        await Assert.That(await Task.WhenAny(stop, Task.Delay(2000))).IsEqualTo(stop);
    }

    [Test]
    public async Task Stop_DisposesEverything()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.Start(Master);
        engine.SetOutput("a", Default);
        engine.Stop();

        await Assert.That(engine.IsRunning).IsFalse();
        await Assert.That(backend.Captures.All(c => c.Disposed)).IsTrue();
        await Assert.That(backend.Renders.All(r => r.Disposed)).IsTrue();
        await Assert.That(engine.ActiveOutputs).IsEmpty();
    }

    [Test]
    public async Task ClicksEnabled_ReachesMasterSignal()
    {
        var backend = new FakeBackend();
        using var engine = new AudioEngine(backend);
        engine.ClicksEnabled = true;
        engine.Start(Master);

        var signal = (MasterSignal)backend.Live(Master).Single().Source!;
        await Assert.That(signal.ClicksEnabled).IsTrue();
        engine.ClicksEnabled = false;
        await Assert.That(signal.ClicksEnabled).IsFalse();
    }
}
