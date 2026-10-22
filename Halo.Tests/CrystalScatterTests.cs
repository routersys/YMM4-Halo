namespace Halo.Tests;

public sealed class CrystalScatterTests
{
    const double Degrees = 180.0 / Math.PI;
    const int Samples = 3_200_000;

    static readonly Lazy<float[]> Plate = new(() => CrystalScatter.Integrate(CrystalFamily.Plate, 20.0, 0.4, 1.5));
    static readonly Lazy<float[]> Column = new(() => CrystalScatter.Integrate(CrystalFamily.Column, 20.0, 2.0, 1.5));
    static readonly Lazy<double[]> PlateReference = new(() => ReferenceTracer.Sky(CrystalFamily.Plate, 20.0, 0.4, 1.5, Samples, 11));
    static readonly Lazy<double[]> ColumnReference = new(() => ReferenceTracer.Sky(CrystalFamily.Column, 20.0, 2.0, 1.5, Samples, 23));

    static double Power(float[] radiance, double sunElevation, double azimuthStart, double azimuthEnd, double offsetStart, double offsetEnd)
    {
        var texel = HaloSettings.ScatterTexelDegrees / Degrees;
        var sum = 0.0;
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            var elevation = HaloSettings.GetScatterElevationDegrees(row, sunElevation);
            var offset = elevation - sunElevation;
            if (offset < offsetStart || offset >= offsetEnd)
                continue;

            var solidAngle = Math.Max(Math.Cos(Math.Min(Math.Abs(elevation), 90.0) / Degrees), HaloSettings.ScatterMinimumCosine) * texel * texel;
            for (var column = 0; column < ScatterGrid.Width; column++)
            {
                var azimuth = HaloSettings.GetScatterAzimuthDegrees(column);
                if (azimuth >= azimuthStart && azimuth < azimuthEnd)
                    sum += radiance[(row * ScatterGrid.Width + column) * HaloSettings.ColorChannels + 1] * 2.0 * solidAngle;
            }
        }

        return sum;
    }

    static double ReferencePower(double[] histogram, int azimuthStart, int azimuthEnd, int offsetStart, int offsetEnd)
    {
        var sum = 0.0;
        for (var row = offsetStart + ReferenceTracer.ElevationBins / 2; row < offsetEnd + ReferenceTracer.ElevationBins / 2; row++)
        {
            for (var column = azimuthStart; column < azimuthEnd; column++)
                sum += histogram[row * ReferenceTracer.AzimuthBins + column];
        }

        return sum;
    }

    static double RowAverage(float[] radiance, int channel, int column, int firstRow, int lastRow)
    {
        var sum = 0.0;
        for (var row = firstRow; row <= lastRow; row++)
            sum += radiance[(row * ScatterGrid.Width + column) * HaloSettings.ColorChannels + channel];
        return sum / (lastRow - firstRow + 1);
    }

    static double PeakAzimuth(float[] radiance, int channel, double sunElevation, double start, double end)
    {
        var row = (int)Math.Round(HaloSettings.ScatterElevationHalfSpanDegrees / HaloSettings.ScatterTexelDegrees - 0.5);
        var best = double.MinValue;
        var peak = 0.0;
        for (var column = 0; column < ScatterGrid.Width; column++)
        {
            var azimuth = HaloSettings.GetScatterAzimuthDegrees(column);
            if (azimuth < start || azimuth > end)
                continue;

            var value = RowAverage(radiance, channel, column, row - 1, row + 1);
            if (value > best)
            {
                best = value;
                peak = azimuth;
            }
        }

        return peak;
    }

    static double ExpectedParhelion(double sunElevation, double wavelength)
    {
        var index = IceOptics.RefractiveIndex(wavelength);
        var sine = Math.Sin(sunElevation / Degrees);
        var effective = Math.Sqrt(index * index - sine * sine) / Math.Cos(sunElevation / Degrees);
        return 2.0 * Math.Asin(effective / 2.0) * Degrees - 60.0;
    }

    static double VerticalSpread(float[] radiance, double sunElevation, double azimuthStart, double azimuthEnd)
    {
        var total = 0.0;
        var moment = 0.0;
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            var offset = HaloSettings.GetScatterElevationDegrees(row, sunElevation) - sunElevation;
            for (var column = 0; column < ScatterGrid.Width; column++)
            {
                var azimuth = HaloSettings.GetScatterAzimuthDegrees(column);
                if (azimuth < azimuthStart || azimuth > azimuthEnd)
                    continue;

                var value = radiance[(row * ScatterGrid.Width + column) * HaloSettings.ColorChannels + 1];
                total += value;
                moment += value * offset * offset;
            }
        }

        return Math.Sqrt(moment / total);
    }

    [Fact]
    public void ATableIsMadeOfFiniteNonNegativeValuesOverTheWholeGrid()
    {
        foreach (var table in new[] { Plate.Value, Column.Value })
        {
            Assert.Equal(ScatterGrid.Width * ScatterGrid.Height * HaloSettings.ColorChannels, table.Length);
            Assert.All(table, value => Assert.True(float.IsFinite(value) && value >= 0f));
            Assert.Contains(table, value => value > 0f);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSameInputGivesTheSameTableBitForBit(bool plate)
    {
        var family = plate ? CrystalFamily.Plate : CrystalFamily.Column;
        var first = CrystalScatter.Integrate(family, 31.75, 0.6, 2.0);
        var second = CrystalScatter.Integrate(family, 31.75, 0.6, 2.0);

        Assert.True(first.AsSpan().SequenceEqual(second));
    }

    [Fact]
    public void TheTotalPowerOfPlatesMatchesAnIndependentMonteCarloTrace()
    {
        var table = Power(Plate.Value, 20.0, 0.0, 180.0, -40.0, 40.0);
        var reference = ReferencePower(PlateReference.Value, 0, 180, -40, 40);

        Assert.Equal(reference, table, reference * 0.015);
    }

    [Theory]
    [InlineData(20, 36, -5, 5)]
    [InlineData(60, 120, -8, 8)]
    [InlineData(0, 20, -5, 5)]
    public void ThePowerOfPlatesInEachSkyRegionMatchesAnIndependentMonteCarloTrace(int azimuthStart, int azimuthEnd, int offsetStart, int offsetEnd)
    {
        var table = Power(Plate.Value, 20.0, azimuthStart, azimuthEnd, offsetStart, offsetEnd);
        var reference = ReferencePower(PlateReference.Value, azimuthStart, azimuthEnd, offsetStart, offsetEnd);

        Assert.True(reference > 0.0);
        Assert.Equal(reference, table, reference * 0.03);
    }

    [Fact]
    public void TheTotalPowerOfColumnsMatchesAnIndependentMonteCarloTrace()
    {
        var table = Power(Column.Value, 20.0, 0.0, 180.0, -40.0, 40.0);
        var reference = ReferencePower(ColumnReference.Value, 0, 180, -40, 40);

        Assert.Equal(reference, table, reference * 0.015);
    }

    [Theory]
    [InlineData(0, 15, 18, 28)]
    [InlineData(0, 15, -28, -18)]
    [InlineData(15, 40, 0, 25)]
    public void ThePowerOfColumnsInEachSkyRegionMatchesAnIndependentMonteCarloTrace(int azimuthStart, int azimuthEnd, int offsetStart, int offsetEnd)
    {
        var table = Power(Column.Value, 20.0, azimuthStart, azimuthEnd, offsetStart, offsetEnd);
        var reference = ReferencePower(ColumnReference.Value, azimuthStart, azimuthEnd, offsetStart, offsetEnd);

        Assert.True(reference > 0.0);
        Assert.Equal(reference, table, reference * 0.03);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(20.0)]
    [InlineData(30.0)]
    public void TheSunDogSitsWhereTheMinimumDeviationOfTheSlantedPrismPutsIt(double elevation)
    {
        var table = CrystalScatter.Integrate(CrystalFamily.Plate, elevation, 0.4, HaloSettings.MinimumTiltDegrees);

        var peak = PeakAzimuth(table, 1, elevation, 12.0, 60.0);

        Assert.Equal(ExpectedParhelion(elevation, HaloSettings.ReferenceWavelengthNanometers), peak, 0.45);
    }

    [Fact]
    public void TheSunDogIsRedOnTheSideNearTheSunAndBlueOnTheFarSide()
    {
        var table = CrystalScatter.Integrate(CrystalFamily.Plate, 20.0, 0.4, HaloSettings.MinimumTiltDegrees);

        var red = PeakAzimuth(table, 0, 20.0, 12.0, 60.0);
        var blue = PeakAzimuth(table, 2, 20.0, 12.0, 60.0);

        Assert.True(blue - red >= 0.3);
        Assert.InRange(red, ExpectedParhelion(20.0, 780.0) - 0.45, ExpectedParhelion(20.0, 600.0) + 0.45);
    }

    [Theory]
    [InlineData(10.0, 0.0)]
    [InlineData(40.0, 0.0)]
    public void TheParhelicCircleLiesAtTheHeightOfTheSun(double elevation, double expectedOffset)
    {
        var table = CrystalScatter.Integrate(CrystalFamily.Plate, elevation, 0.4, HaloSettings.MinimumTiltDegrees);
        var column = (int)Math.Round(100.0 / HaloSettings.ScatterTexelDegrees - 0.5);
        var best = double.MinValue;
        var peakOffset = 0.0;
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            var value = table[(row * ScatterGrid.Width + column) * HaloSettings.ColorChannels + 1];
            if (value > best)
            {
                best = value;
                peakOffset = HaloSettings.GetScatterElevationDegrees(row, elevation) - elevation;
            }
        }

        Assert.Equal(expectedOffset, peakOffset, 0.4);
    }

    [Fact]
    public void ALargerTiltSpreadWidensTheSunDogVertically()
    {
        var narrow = CrystalScatter.Integrate(CrystalFamily.Plate, 20.0, 0.4, 0.5);
        var wide = CrystalScatter.Integrate(CrystalFamily.Plate, 20.0, 0.4, 3.0);

        var narrowSpread = VerticalSpread(narrow, 20.0, 24.0, 30.0);
        var wideSpread = VerticalSpread(wide, 20.0, 24.0, 30.0);

        Assert.True(wideSpread > 2.5 * narrowSpread);
    }

    [Fact]
    public void ThickerPlatesKeepTheSunDogBrightWhenTheSunIsHigh()
    {
        var thin = CrystalScatter.Integrate(CrystalFamily.Plate, 40.0, 0.1, 1.0);
        var thick = CrystalScatter.Integrate(CrystalFamily.Plate, 40.0, 1.0, 1.0);

        var thinPower = Power(thin, 40.0, 22.0, 45.0, -3.0, 3.0);
        var thickPower = Power(thick, 40.0, 22.0, 45.0, -3.0, 3.0);

        Assert.True(thickPower > 5.0 * thinPower);
    }

    [Fact]
    public void TheSunDogFadesAsTheSunClimbs()
    {
        var low = CrystalScatter.Integrate(CrystalFamily.Plate, 10.0, 0.4, 1.0);
        var high = CrystalScatter.Integrate(CrystalFamily.Plate, 40.0, 0.4, 1.0);

        var lowPower = Power(low, 10.0, 20.0, 45.0, -3.0, 3.0);
        var highPower = Power(high, 40.0, 20.0, 50.0, -3.0, 3.0);

        Assert.True(lowPower > 3.0 * highPower);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(20.0)]
    public void TheTangentArcsTouchTheInnerHaloAboveAndBelowTheSun(double elevation)
    {
        var table = CrystalScatter.Integrate(CrystalFamily.Column, elevation, 2.0, HaloSettings.MinimumTiltDegrees);
        var expected = 2.0 * Math.Asin(IceOptics.RefractiveIndex(HaloSettings.ReferenceWavelengthNanometers) * 0.5) * Degrees - 60.0;

        double Apex(double lower, double upper)
        {
            var best = double.MinValue;
            var peak = 0.0;
            for (var row = 0; row < ScatterGrid.Height; row++)
            {
                var offset = HaloSettings.GetScatterElevationDegrees(row, elevation) - elevation;
                if (offset < lower || offset > upper)
                    continue;

                var value = RowAverage(table, 1, 0, row, row);
                if (value > best)
                {
                    best = value;
                    peak = offset;
                }
            }

            return peak;
        }

        Assert.Equal(expected, Apex(15.0, 30.0), 0.6);
        Assert.Equal(-expected, Apex(-30.0, -15.0), 0.6);
    }

    [Fact]
    public void NoLightLandsBetweenTheSunAndTheInnerHaloFromColumns()
    {
        var inside = Power(Column.Value, 20.0, 0.0, 10.0, -15.0, 15.0);
        var arc = Power(Column.Value, 20.0, 0.0, 15.0, 18.0, 28.0);

        Assert.True(inside < 0.02 * arc);
    }
}
