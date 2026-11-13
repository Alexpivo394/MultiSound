using MultiSound.Audio;

namespace MultiSound.Tests;

public class DriftControllerTests
{
    [Test]
    public async Task AboveTarget_PositiveCorrection()
    {
        var correction = new DriftController().Update(fillMs: 130, targetMs: 120, dtSeconds: 0.01);
        await Assert.That(correction).IsGreaterThan(0);
    }

    [Test]
    public async Task BelowTarget_NegativeCorrection()
    {
        var correction = new DriftController().Update(fillMs: 110, targetMs: 120, dtSeconds: 0.01);
        await Assert.That(correction).IsLessThan(0);
    }

    [Test]
    public async Task Correction_Clamped()
    {
        var controller = new DriftController();
        await Assert.That(controller.Update(1120, 120, 0.01)).IsEqualTo(DriftController.MaxCorrection);
        controller.Reset();
        await Assert.That(controller.Update(-880, 120, 0.01)).IsEqualTo(-DriftController.MaxCorrection);
    }
}
