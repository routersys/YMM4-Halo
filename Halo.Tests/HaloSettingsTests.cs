using System.Numerics;

namespace Halo.Tests;

public sealed class HaloSettingsTests
{
    static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-6f);
        Assert.Equal(expected.Y, actual.Y, 1e-6f);
        Assert.Equal(expected.Z, actual.Z, 1e-6f);
    }

    static double NormalCdf(double value)
    {
        var t = 1.0 / (1.0 + 0.2316419 * Math.Abs(value));
        var density = Math.Exp(-0.5 * value * value) / Math.Sqrt(2.0 * Math.PI);
        var tail = density * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return value >= 0.0 ? 1.0 - tail : tail;
    }

    [Fact]
    public void TheSunFacesForwardWhenItIsOnTheHorizonInFrontOfTheCamera()
    {
        AssertVector(new Vector3(0f, 0f, 1f), HaloSettings.GetSunDirection(0.0, 0.0, 0.0));
    }

    [Fact]
    public void TheSunLiesAboveTheHorizonByItsElevation()
    {
        var direction = HaloSettings.GetSunDirection(30.0, 0.0, 0.0);

        AssertVector(new Vector3(0f, -0.5f, MathF.Cos(MathF.PI / 6f)), direction);
    }

    [Theory]
    [InlineData(90.0, 1f, 0f, 0f)]
    [InlineData(-90.0, -1f, 0f, 0f)]
    [InlineData(180.0, 0f, 0f, -1f)]
    public void TheDirectionTurnsTheSunAroundTheCamera(double direction, float x, float y, float z)
    {
        AssertVector(new Vector3(x, y, z), HaloSettings.GetSunDirection(0.0, direction, 0.0));
    }

    [Fact]
    public void LookingUpToTheSunPutsItAtTheCenter()
    {
        AssertVector(new Vector3(0f, 0f, 1f), HaloSettings.GetSunDirection(30.0, 0.0, 30.0));
    }

    [Fact]
    public void LookingDownMovesTheSunUpTheImage()
    {
        var direction = HaloSettings.GetSunDirection(0.0, 0.0, -30.0);

        AssertVector(new Vector3(0f, -0.5f, MathF.Cos(MathF.PI / 6f)), direction);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(25.0, 40.0, -15.0)]
    [InlineData(90.0, 170.0, 60.0)]
    [InlineData(45.0, -120.0, 20.0)]
    public void TheDirectionIsAUnitVector(double elevation, double direction, double pitch)
    {
        Assert.Equal(1f, HaloSettings.GetSunDirection(elevation, direction, pitch).Length(), 1e-6f);
    }

    [Fact]
    public void TheElevationIsHeldBetweenZeroAndNinety()
    {
        AssertVector(HaloSettings.GetSunDirection(90.0, 0.0, 0.0), HaloSettings.GetSunDirection(400.0, 0.0, 0.0));
        AssertVector(HaloSettings.GetSunDirection(0.0, 0.0, 0.0), HaloSettings.GetSunDirection(-40.0, 0.0, 0.0));
    }

    [Fact]
    public void ThePitchIsHeldBetweenItsLimits()
    {
        AssertVector(HaloSettings.GetSunDirection(20.0, 0.0, 90.0), HaloSettings.GetSunDirection(20.0, 0.0, 400.0));
        AssertVector(HaloSettings.GetSunDirection(20.0, 0.0, -60.0), HaloSettings.GetSunDirection(20.0, 0.0, -400.0));
    }

    [Theory]
    [InlineData(60.0, 0.5773503f)]
    [InlineData(90.0, 1f)]
    [InlineData(10.0, 0.0874887f)]
    [InlineData(1.0, 0.0874887f)]
    [InlineData(150.0, 3.7320508f)]
    [InlineData(179.0, 3.7320508f)]
    public void TheFieldOfViewIsHeldBetweenTenAndOneHundredFiftyDegrees(double field, float expected)
    {
        Assert.Equal(expected, HaloSettings.GetHalfFieldOfViewTangent(field), 1e-5f);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.1, 0.0)]
    [InlineData(0.13, 0.25)]
    [InlineData(20.37, 20.25)]
    [InlineData(20.38, 20.5)]
    [InlineData(89.9, 90.0)]
    [InlineData(250.0, 90.0)]
    [InlineData(-5.0, 0.0)]
    public void TheSunElevationIsRoundedToTheNearestQuarterDegree(double elevation, double expected)
    {
        Assert.Equal(expected, HaloSettings.QuantizeSunElevation(elevation), 1e-12);
    }

    [Fact]
    public void TheRingTableCoversTheInnerAndOuterHalosWithAMarginOnBothSides()
    {
        Assert.True(HaloSettings.RingStartDegrees < 20.0);
        Assert.True(HaloSettings.RingEndDegrees - HaloSettings.RingOuterTaperDegrees > 48.0);
        Assert.Equal(HaloSettings.RingStartDegrees, HaloSettings.GetRingAngleDegrees(0), 1e-12);
        Assert.Equal(HaloSettings.RingEndDegrees, HaloSettings.GetRingAngleDegrees(HaloSettings.RingTableWidth - 1), 1e-9);
    }

    [Fact]
    public void TheScatterTableSpansHalfTheSkyInAzimuthAndTheWholeElevationSpan()
    {
        Assert.Equal(180.0, HaloSettings.ScatterWidth * HaloSettings.ScatterTexelDegrees, 1e-9);
        Assert.True(HaloSettings.ScatterHeight * HaloSettings.ScatterTexelDegrees >= 2.0 * HaloSettings.ScatterElevationHalfSpanDegrees);
        Assert.True((HaloSettings.ScatterHeight - 1) * HaloSettings.ScatterTexelDegrees < 2.0 * HaloSettings.ScatterElevationHalfSpanDegrees);
    }

    [Fact]
    public void TheScatterRowsAreCenteredOnTheirTexels()
    {
        Assert.Equal(0.15, HaloSettings.GetScatterAzimuthDegrees(0), 1e-12);
        Assert.Equal(179.85, HaloSettings.GetScatterAzimuthDegrees(HaloSettings.ScatterWidth - 1), 1e-9);
        Assert.Equal(20.0 - 40.0 + 0.15, HaloSettings.GetScatterElevationDegrees(0, 20.0), 1e-12);
    }

    [Fact]
    public void TheSpectrumIsSampledEveryTwentyNanometersFromVioletToRed()
    {
        Assert.Equal(20, HaloSettings.GetWavelengthCount());
        Assert.Equal(HaloSettings.WavelengthEndNanometers, HaloSettings.GetWavelengthNanometers(19), 1e-9);
        Assert.Equal(HaloSettings.WavelengthStartNanometers, HaloSettings.GetWavelengthNanometers(0), 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.5)]
    [InlineData(2.0, 1.0)]
    [InlineData(5.0, 1.0)]
    [InlineData(-1.0, 0.0)]
    public void TheSunRadiusIsHalfTheDiameterWithinTheLimit(double diameter, double expected)
    {
        Assert.Equal(expected, HaloSettings.GetSunRadiusDegrees(diameter), 1e-12);
    }

    [Fact]
    public void TheColumnTiltNodesAreTheMidpointQuantilesOfTheNormalDistribution()
    {
        var nodes = HaloSettings.ColumnTiltQuantiles;

        for (var index = 0; index < nodes.Length; index++)
            Assert.Equal((index + 0.5) / nodes.Length, NormalCdf(nodes[index]), 1e-6);
        Assert.Equal(0.0, nodes[nodes.Length / 2], 1e-12);
    }

    [Fact]
    public void TheSunSpectrumFollowsPlancksLaw()
    {
        var blue = SolarSpectrum.Radiance(450.0, HaloSettings.SolarTemperature);
        var red = SolarSpectrum.Radiance(650.0, HaloSettings.SolarTemperature);
        var wien = 2.897771955e-3 / HaloSettings.SolarTemperature * 1e9;

        Assert.True(blue > red);
        Assert.Equal(501.9, wien, 0.5);
        Assert.True(SolarSpectrum.Radiance(wien, HaloSettings.SolarTemperature) > SolarSpectrum.Radiance(wien + 50.0, HaloSettings.SolarTemperature));
        Assert.True(SolarSpectrum.Radiance(wien, HaloSettings.SolarTemperature) > SolarSpectrum.Radiance(wien - 50.0, HaloSettings.SolarTemperature));
    }

    [Theory]
    [InlineData(555.0, 1.0, 0.05)]
    [InlineData(450.0, 0.04, 0.03)]
    public void TheColorMatchingFunctionsFollowTheCieLuminousEfficiency(double wavelength, double expectedY, double tolerance)
    {
        var (_, y, _) = SolarSpectrum.ColorMatching(wavelength);

        Assert.Equal(expectedY, y, tolerance);
    }

    [Fact]
    public void TheSpectralWeightsHaveUnitLuminanceAndGiveAWhiteSun()
    {
        var white = SolarSpectrum.White;
        var (red, green, blue) = SolarSpectrum.ToLinearSrgb(white);

        Assert.Equal(1.0, white.Y, 1e-12);
        Assert.InRange(red / green, 0.85, 1.25);
        Assert.InRange(blue / green, 0.8, 1.25);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SpectralBandsKeepTheTotalWeightAndStayInsideTheSpectrum(int stride)
    {
        var bands = SolarSpectrum.Bands(stride);
        var total = new Tristimulus();
        foreach (var band in bands)
            total += band.Weight;

        Assert.Equal(SolarSpectrum.White.X, total.X, 1e-12);
        Assert.Equal(SolarSpectrum.White.Y, total.Y, 1e-12);
        Assert.Equal(SolarSpectrum.White.Z, total.Z, 1e-12);
        Assert.Equal((HaloSettings.GetWavelengthCount() + stride - 1) / stride, bands.Length);
        Assert.All(bands, band => Assert.InRange(band.Wavelength, HaloSettings.WavelengthStartNanometers, HaloSettings.WavelengthEndNanometers));
        Assert.Equal(bands.OrderBy(band => band.Wavelength).Select(band => band.Wavelength), bands.Select(band => band.Wavelength));
    }

    [Fact]
    public void SingleWavelengthBandsAreTheSamplesThemselves()
    {
        var bands = SolarSpectrum.Bands(1);

        for (var index = 0; index < bands.Length; index++)
        {
            Assert.Equal(HaloSettings.GetWavelengthNanometers(index), bands[index].Wavelength, 1e-9);
            Assert.Equal(SolarSpectrum.Weights[index], bands[index].Weight);
        }
    }
}
