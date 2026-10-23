namespace Halo.Tests;

public sealed class HaloTableTests
{
    const double Degrees = 180.0 / Math.PI;
    const int Channels = HaloSettings.TexelChannels;

    static float Value(Half[] texels, int texel, int channel) => (float)texels[texel * Channels + channel];

    static float[] Filled(double value)
        => Enumerable.Repeat((float)value, ScatterGrid.Width * ScatterGrid.Height * HaloSettings.ColorChannels).ToArray();

    static float[] Empty() => Filled(0.0);

    static float[] Impulse(int row, int column, double scale)
    {
        var cells = Empty();
        var white = SolarSpectrum.White;
        var index = (row * ScatterGrid.Width + column) * HaloSettings.ColorChannels;
        cells[index] = (float)(scale * white.X);
        cells[index + 1] = (float)(scale * white.Y);
        cells[index + 2] = (float)(scale * white.Z);
        return cells;
    }

    static float Peak(Half[] texels)
    {
        var peak = 0f;
        for (var texel = 0; texel < texels.Length / Channels; texel++)
            peak = Math.Max(peak, Math.Max(Value(texels, texel, 0), Math.Max(Value(texels, texel, 1), Value(texels, texel, 2))));
        return peak;
    }

    static double RingLuminancePower(Half[] texels)
    {
        var step = HaloSettings.GetRingStepDegrees() / Degrees;
        var sum = 0.0;
        for (var index = 0; index < HaloSettings.RingTableWidth; index++)
        {
            var luminance = 0.2126 * Value(texels, index, 0) + 0.7152 * Value(texels, index, 1) + 0.0722 * Value(texels, index, 2);
            sum += luminance * 2.0 * Math.PI * Math.Sin(HaloSettings.GetRingAngleDegrees(index) / Degrees) * step;
        }

        return sum;
    }

    static double ScatterLuminancePower(Half[] texels, double sunElevation)
    {
        var texel = HaloSettings.ScatterTexelDegrees / Degrees;
        var sum = 0.0;
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            var elevation = Math.Min(Math.Abs(HaloSettings.GetScatterElevationDegrees(row, sunElevation)), 90.0) / Degrees;
            var solidAngle = Math.Max(Math.Cos(elevation), HaloSettings.ScatterMinimumCosine) * texel * texel;
            for (var column = 0; column < ScatterGrid.Width; column++)
            {
                var index = row * ScatterGrid.Width + column;
                sum += (0.2126 * Value(texels, index, 0) + 0.7152 * Value(texels, index, 1) + 0.0722 * Value(texels, index, 2)) * solidAngle;
            }
        }

        return sum;
    }

    [Fact]
    public void TheRingTableHasOneOpaqueTexelPerAngle()
    {
        var texels = HaloTable.ComposeRing(HaloSettings.ReferenceSunDiameterDegrees, 1.0, 1.0);

        Assert.Equal(HaloSettings.RingTableWidth * Channels, texels.Length);
        Assert.All(Enumerable.Range(0, HaloSettings.RingTableWidth), texel => Assert.Equal(1f, Value(texels, texel, 3)));
    }

    [Fact]
    public void TheBrightestRingTexelHasThePeakBrightnessAtFullShare()
    {
        var texels = HaloTable.ComposeRing(HaloSettings.ReferenceSunDiameterDegrees, 1.0, 1.0);

        Assert.Equal(HaloSettings.RingPeakBrightness, Peak(texels), HaloSettings.RingPeakBrightness * 2e-3);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.25)]
    public void TheRingBrightnessIsProportionalToTheShare(double share)
    {
        var full = HaloTable.ComposeRing(1.0, 1.0, 1.0);
        var part = HaloTable.ComposeRing(1.0, share, 1.0);

        Assert.Equal(share * Peak(full), Peak(part), Peak(full) * 3e-3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void ARingWithoutShareIsDark(double share)
    {
        var texels = HaloTable.ComposeRing(0.53, share, 1.0);

        Assert.All(Enumerable.Range(0, HaloSettings.RingTableWidth * Channels).Where(index => index % Channels != 3), index => Assert.Equal(0f, (float)texels[index]));
    }

    [Fact]
    public void TheShareIsHeldAtOne()
    {
        var full = HaloTable.ComposeRing(0.53, 1.0, 1.0);
        var more = HaloTable.ComposeRing(0.53, 3.0, 1.0);

        Assert.True(full.AsSpan().SequenceEqual(more));
    }

    [Fact]
    public void WithoutSaturationEveryColorBecomesGray()
    {
        var texels = HaloTable.ComposeRing(0.53, 1.0, 0.0);

        Assert.All(Enumerable.Range(0, HaloSettings.RingTableWidth), texel =>
        {
            Assert.Equal(Value(texels, texel, 0), Value(texels, texel, 1));
            Assert.Equal(Value(texels, texel, 1), Value(texels, texel, 2));
        });
    }

    [Fact]
    public void DoubleSaturationDoublesTheDistanceFromTheLuminanceWhereNoChannelIsCut()
    {
        var normal = HaloTable.ComposeRing(0.53, 1.0, 1.0);
        var vivid = HaloTable.ComposeRing(0.53, 1.0, 2.0);
        var checkedTexels = 0;

        for (var texel = 0; texel < HaloSettings.RingTableWidth; texel++)
        {
            var luminance = 0.2126 * Value(normal, texel, 0) + 0.7152 * Value(normal, texel, 1) + 0.0722 * Value(normal, texel, 2);
            var expected = Enumerable.Range(0, 3).Select(channel => luminance + 2.0 * (Value(normal, texel, channel) - luminance)).ToArray();
            if (expected.Any(value => value < 1e-3))
                continue;

            checkedTexels++;
            for (var channel = 0; channel < 3; channel++)
                Assert.Equal(expected[channel], Value(vivid, texel, channel), 2e-3 + 0.01 * expected[channel]);
        }

        Assert.True(checkedTexels > 20);
        Assert.Contains(Enumerable.Range(0, HaloSettings.RingTableWidth), texel => Value(normal, texel, 0) - Value(normal, texel, 2) > 0.01f);
    }

    [Fact]
    public void TheSaturationIsHeldBetweenZeroAndItsMaximum()
    {
        Assert.True(HaloTable.ComposeRing(0.53, 1.0, 9.0).AsSpan().SequenceEqual(HaloTable.ComposeRing(0.53, 1.0, HaloSettings.MaximumSaturation)));
        Assert.True(HaloTable.ComposeRing(0.53, 1.0, -1.0).AsSpan().SequenceEqual(HaloTable.ComposeRing(0.53, 1.0, 0.0)));
    }

    [Fact]
    public void TheRingFadesToNothingAtItsOuterEnd()
    {
        var texels = HaloTable.ComposeRing(0.0, 1.0, 1.0);
        var last = HaloSettings.RingTableWidth - 1;

        Assert.Equal(0f, Value(texels, last, 1));
        Assert.True(Value(texels, last - 20, 1) < Value(texels, last - 200, 1) * 2.0);
    }

    [Fact]
    public void AWiderSunLowersTheRingPeakAndKeepsItsPower()
    {
        var sharp = HaloTable.ComposeRing(0.0, 1.0, 1.0);
        var wide = HaloTable.ComposeRing(2.0, 1.0, 1.0);

        Assert.True(Peak(wide) < 0.9 * Peak(sharp));
        Assert.Equal(RingLuminancePower(sharp), RingLuminancePower(wide), RingLuminancePower(sharp) * 0.02);
    }

    [Fact]
    public void TheSunSizeIsHeldBetweenZeroAndItsMaximum()
    {
        Assert.True(HaloTable.ComposeRing(9.0, 1.0, 1.0).AsSpan().SequenceEqual(HaloTable.ComposeRing(HaloSettings.MaximumSunDiameterDegrees, 1.0, 1.0)));
        Assert.True(HaloTable.ComposeRing(-1.0, 1.0, 1.0).AsSpan().SequenceEqual(HaloTable.ComposeRing(0.0, 1.0, 1.0)));
    }

    [Fact]
    public void TheRingTableIsTheSameForTheSameInput()
    {
        Assert.True(HaloTable.ComposeRing(0.7, 0.6, 1.3).AsSpan().SequenceEqual(HaloTable.ComposeRing(0.7, 0.6, 1.3)));
    }

    [Fact]
    public void TheScatterTableHasOneOpaqueTexelPerCell()
    {
        var texels = HaloTable.ComposeScatter(Empty(), Empty(), 1.0, 1.0, 20.0, 0.53, 1.0);

        Assert.Equal(ScatterGrid.Width * ScatterGrid.Height * Channels, texels.Length);
        Assert.All(Enumerable.Range(0, ScatterGrid.Width * ScatterGrid.Height), texel => Assert.Equal(1f, Value(texels, texel, 3)));
    }

    [Fact]
    public void WithoutAnyShareTheScatterTableIsDark()
    {
        var texels = HaloTable.ComposeScatter(Filled(1.0), Filled(1.0), 0.0, 0.0, 20.0, 0.53, 1.0);

        Assert.All(Enumerable.Range(0, ScatterGrid.Width * ScatterGrid.Height * Channels).Where(index => index % Channels != 3), index => Assert.Equal(0f, (float)texels[index]));
    }

    [Fact]
    public void TheTwoFamiliesAddUpInTheScatterTable()
    {
        var plate = Impulse(100, 120, 5.0);
        var column = Impulse(180, 60, 3.0);

        var both = HaloTable.ComposeScatter(plate, column, 1.0, 1.0, 20.0, 0.53, 1.0);
        var first = HaloTable.ComposeScatter(plate, column, 1.0, 0.0, 20.0, 0.53, 1.0);
        var second = HaloTable.ComposeScatter(plate, column, 0.0, 1.0, 20.0, 0.53, 1.0);

        Assert.All(Enumerable.Range(0, ScatterGrid.Width * ScatterGrid.Height), texel =>
            Assert.Equal(Value(first, texel, 1) + Value(second, texel, 1), Value(both, texel, 1), 2e-3 + 1e-3 * Value(both, texel, 1)));
        Assert.True(Peak(first) > 0f && Peak(second) > 0f);
    }

    [Fact]
    public void TheShareOfAFamilyScalesItsLight()
    {
        var plate = Impulse(100, 120, 5.0);

        var full = HaloTable.ComposeScatter(plate, Empty(), 1.0, 0.0, 20.0, 0.0, 1.0);
        var half = HaloTable.ComposeScatter(plate, Empty(), 0.5, 0.0, 20.0, 0.0, 1.0);

        Assert.Equal(0.5 * Peak(full), Peak(half), Peak(full) * 3e-3);
    }

    [Fact]
    public void ASunColoredImpulseStaysWhiteInTheScatterTable()
    {
        var texels = HaloTable.ComposeScatter(Impulse(133, 120, 2.0), Empty(), 1.0, 0.0, 20.0, 0.0, 1.0);
        var texel = 133 * ScatterGrid.Width + 120;

        Assert.True(Value(texels, texel, 1) > 0.01f);
        Assert.Equal(Value(texels, texel, 1), Value(texels, texel, 0), Value(texels, texel, 1) * 5e-3);
        Assert.Equal(Value(texels, texel, 1), Value(texels, texel, 2), Value(texels, texel, 1) * 5e-3);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void BlurringTheSunKeepsThePowerOfTheScatterTable(double diameter)
    {
        var impulse = Impulse(133, 300, 40.0);

        var sharp = HaloTable.ComposeScatter(impulse, Empty(), 1.0, 0.0, 20.0, 0.0, 1.0);
        var blurred = HaloTable.ComposeScatter(impulse, Empty(), 1.0, 0.0, 20.0, diameter, 1.0);

        Assert.Equal(ScatterLuminancePower(sharp, 20.0), ScatterLuminancePower(blurred, 20.0), ScatterLuminancePower(sharp, 20.0) * 0.02);
        Assert.True(Peak(blurred) <= Peak(sharp));
    }

    [Fact]
    public void ABlurSpreadsAnImpulseOverNeighbouringTexels()
    {
        var sharp = HaloTable.ComposeScatter(Impulse(133, 300, 40.0), Empty(), 1.0, 0.0, 20.0, 0.0, 1.0);
        var blurred = HaloTable.ComposeScatter(Impulse(133, 300, 40.0), Empty(), 1.0, 0.0, 20.0, 2.0, 1.0);

        int Lit(Half[] texels) => Enumerable.Range(0, ScatterGrid.Width * ScatterGrid.Height).Count(texel => Value(texels, texel, 1) > 1e-4f);

        Assert.Equal(1, Lit(sharp));
        Assert.True(Lit(blurred) > 10);
    }

    [Fact]
    public void TheScatterTableFadesToNothingAtTheTopAndBottomRows()
    {
        var texels = HaloTable.ComposeScatter(Filled(0.01), Empty(), 1.0, 0.0, 20.0, 0.0, 1.0);
        var middle = Value(texels, 133 * ScatterGrid.Width + 300, 1);

        Assert.True(middle > 0f);
        Assert.True(Value(texels, 0 * ScatterGrid.Width + 300, 1) < 0.01f * middle);
        Assert.True(Value(texels, (ScatterGrid.Height - 1) * ScatterGrid.Width + 300, 1) < 0.01f * middle);
        Assert.Equal(middle, Value(texels, 100 * ScatterGrid.Width + 300, 1), middle * 0.01f);
    }

    [Fact]
    public void ALightOnTheAzimuthEdgeIsReflectedIntoTheNeighbouringTexel()
    {
        var edge = Impulse(133, 0, 40.0);

        var texels = HaloTable.ComposeScatter(edge, Empty(), 1.0, 0.0, 20.0, 2.0, 1.0);

        Assert.True(Value(texels, 133 * ScatterGrid.Width + 1, 1) > 0f);
        Assert.True(Value(texels, 133 * ScatterGrid.Width, 1) > Value(texels, 133 * ScatterGrid.Width + 3, 1));
    }

    [Fact]
    public void TheScatterTableIsTheSameForTheSameInput()
    {
        var plate = Impulse(120, 200, 3.0);

        Assert.True(HaloTable.ComposeScatter(plate, Empty(), 0.7, 0.2, 33.0, 1.1, 0.8).AsSpan().SequenceEqual(HaloTable.ComposeScatter(plate, Empty(), 0.7, 0.2, 33.0, 1.1, 0.8)));
    }

    [Fact]
    public void ASaturationOfZeroTurnsTheScatterTableGray()
    {
        var texels = HaloTable.ComposeScatter(Impulse(133, 120, 2.0), Empty(), 1.0, 0.0, 20.0, 0.5, 0.0);

        Assert.All(Enumerable.Range(0, ScatterGrid.Width * ScatterGrid.Height), texel =>
            Assert.Equal(Value(texels, texel, 0), Value(texels, texel, 2)));
    }
}
