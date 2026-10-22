using System.Windows;
using Telemetry;

namespace Halo.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        HaloTelemetry.EnsureStartedOnce();
        HaloTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new HaloEffect();

        Assert.Equal(Texts.Halo, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
