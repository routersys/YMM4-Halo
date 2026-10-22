namespace Halo.Tests;

public sealed class RingProfileTests
{
    const double Degrees = 180.0 / Math.PI;

    static readonly Lazy<double[]> Reference = new(() => ReferenceTracer.Ring(4_000_000, 5));

    static double Power(double[] profile, double start, double end)
    {
        var step = HaloSettings.GetRingStepDegrees();
        var sum = 0.0;
        for (var index = 0; index < HaloSettings.RingTableWidth; index++)
        {
            var angle = HaloSettings.GetRingAngleDegrees(index);
            if (angle >= start && angle < end)
                sum += profile[index * HaloSettings.ColorChannels + 1] * 2.0 * Math.PI * Math.Sin(angle / Degrees) * step / Degrees;
        }

        return sum;
    }

    static double Edge(double[] profile, int channel, double start, double end)
    {
        var peak = 0.0;
        for (var index = 0; index < HaloSettings.RingTableWidth; index++)
        {
            var angle = HaloSettings.GetRingAngleDegrees(index);
            if (angle >= start && angle < end)
                peak = Math.Max(peak, profile[index * HaloSettings.ColorChannels + channel]);
        }

        for (var index = 0; index < HaloSettings.RingTableWidth; index++)
        {
            var angle = HaloSettings.GetRingAngleDegrees(index);
            if (angle >= start && angle < end && profile[index * HaloSettings.ColorChannels + channel] > 0.15 * peak)
                return angle;
        }

        return double.NaN;
    }

    [Fact]
    public void TheProfileHasOneThreeChannelValuePerTableEntryAndNoNegativeValues()
    {
        var profile = RingProfile.Radiance;

        Assert.Equal(HaloSettings.RingTableWidth * HaloSettings.ColorChannels, profile.Length);
        Assert.All(profile, value => Assert.True(double.IsFinite(value) && value >= 0.0));
    }

    [Fact]
    public void TheProfileIsTheSameObjectOnEveryRead()
    {
        Assert.Same(RingProfile.Radiance, RingProfile.Radiance);
    }

    [Theory]
    [InlineData(21, 24)]
    [InlineData(24, 30)]
    [InlineData(30, 44)]
    [InlineData(44, 50)]
    [InlineData(50, 60)]
    public void ThePowerInEachAngleRangeMatchesAnIndependentMonteCarloTrace(int start, int end)
    {
        var table = Power(RingProfile.Radiance, start, end);
        var reference = 0.0;
        for (var angle = start; angle < end; angle++)
            reference += Reference.Value[angle];

        Assert.True(reference > 0.0);
        Assert.Equal(reference, table, reference * 0.02);
    }

    [Fact]
    public void NoLightLandsInsideTheInnerHaloOrBetweenTheSunAndIt()
    {
        var inside = Power(RingProfile.Radiance, HaloSettings.RingStartDegrees, 21.0);
        var ring = Power(RingProfile.Radiance, 21.0, 24.0);

        Assert.True(inside < 1e-3 * ring);
    }

    [Fact]
    public void TheInnerEdgeOfEachColorSitsAtTheMinimumDeviationOfTheSixtyDegreePrism()
    {
        var profile = RingProfile.Radiance;

        double Expected(double wavelength) => 2.0 * Math.Asin(IceOptics.RefractiveIndex(wavelength) * 0.5) * Degrees - 60.0;

        var red = Edge(profile, 0, 15.0, 30.0);
        var blue = Edge(profile, 2, 15.0, 30.0);

        Assert.InRange(red, Expected(700.0) - 0.25, Expected(600.0) + 0.25);
        Assert.InRange(blue, Expected(480.0) - 0.25, Expected(440.0) + 0.25);
        Assert.True(blue - red > 0.3);
    }

    [Fact]
    public void TheOuterHaloIsFainterThanTheInnerHaloAndStartsAtTheNinetyDegreeMinimumDeviation()
    {
        var profile = RingProfile.Radiance;
        var inner = Enumerable.Range(0, HaloSettings.RingTableWidth).Where(index => HaloSettings.GetRingAngleDegrees(index) is > 20.0 and < 26.0).Max(index => profile[index * HaloSettings.ColorChannels + 1]);
        var outer = Enumerable.Range(0, HaloSettings.RingTableWidth).Where(index => HaloSettings.GetRingAngleDegrees(index) is > 44.0 and < 50.0).Max(index => profile[index * HaloSettings.ColorChannels + 1]);

        var expected = 2.0 * Math.Asin(IceOptics.RefractiveIndex(HaloSettings.ReferenceWavelengthNanometers) * Math.Sin(45.0 / Degrees)) * Degrees - 90.0;
        var edge = Edge(profile, 1, 40.0, 56.0);

        Assert.True(outer < 0.25 * inner);
        Assert.Equal(expected, edge, 0.35);
    }
}
