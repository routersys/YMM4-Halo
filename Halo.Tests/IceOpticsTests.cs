namespace Halo.Tests;

public sealed class IceOpticsTests
{
    [Theory]
    [InlineData(400.0, 1.3194)]
    [InlineData(450.0, 1.3157)]
    [InlineData(500.0, 1.3130)]
    [InlineData(550.0, 1.3110)]
    [InlineData(589.0, 1.30973)]
    [InlineData(650.0, 1.3080)]
    [InlineData(700.0, 1.3069)]
    [InlineData(780.0, 1.3053)]
    [InlineData(800.0, 1.3049)]
    public void TheRefractiveIndexMatchesTheTabulatedValuesOfIce(double wavelength, double expected)
    {
        Assert.Equal(expected, IceOptics.RefractiveIndex(wavelength), 1e-9);
    }

    [Fact]
    public void TheRefractiveIndexIsInterpolatedLinearlyBetweenTheTabulatedWavelengths()
    {
        Assert.Equal(0.5 * (1.3110 + 1.3106), IceOptics.RefractiveIndex(555.0), 1e-12);
        Assert.Equal(1.3110 + 0.25 * (1.3106 - 1.3110), IceOptics.RefractiveIndex(552.5), 1e-12);
    }

    [Theory]
    [InlineData(300.0, 400.0)]
    [InlineData(900.0, 800.0)]
    public void TheRefractiveIndexIsHeldAtTheEndsOfTheTable(double wavelength, double limit)
    {
        Assert.Equal(IceOptics.RefractiveIndex(limit), IceOptics.RefractiveIndex(wavelength), 1e-12);
    }

    [Fact]
    public void TheRefractiveIndexFallsAsTheWavelengthGrows()
    {
        var indices = Enumerable.Range(0, 81).Select(step => IceOptics.RefractiveIndex(400.0 + step * 5.0)).ToArray();

        Assert.All(indices.Zip(indices.Skip(1)), pair => Assert.True(pair.First > pair.Second));
        Assert.InRange(indices[0], 1.318, 1.321);
        Assert.InRange(indices[^1], 1.304, 1.306);
    }

    [Theory]
    [InlineData(1.0, 1.3110)]
    [InlineData(1.3110, 1.0)]
    public void ReflectanceAtNormalIncidenceIsTheFresnelValue(double incident, double transmitted)
    {
        var expected = Math.Pow((incident - transmitted) / (incident + transmitted), 2.0);

        Assert.Equal(expected, IceOptics.Reflectance(1.0, incident, transmitted), 1e-12);
        Assert.Equal(0.0177, IceOptics.Reflectance(1.0, incident, transmitted), 5e-4);
    }

    [Fact]
    public void ReflectanceAtGrazingIncidenceIsTotal()
    {
        Assert.Equal(1.0, IceOptics.Reflectance(0.0, 1.0, 1.3110), 1e-12);
    }

    [Fact]
    public void ReflectanceAtTheBrewsterAngleIsHalfOfThePerpendicularComponent()
    {
        const double Index = 1.3110;
        var cosine = 1.0 / Math.Sqrt(1.0 + Index * Index);
        var perpendicular = Math.Pow((Index * Index - 1.0) / (Index * Index + 1.0), 2.0);

        Assert.Equal(0.5 * perpendicular, IceOptics.Reflectance(cosine, 1.0, Index), 1e-12);
    }

    [Fact]
    public void ReflectanceFromInsideIsTotalBeyondTheCriticalAngleAndPartialBelowIt()
    {
        const double Index = 1.3110;
        var critical = Math.Sqrt(1.0 - 1.0 / (Index * Index));

        Assert.Equal(1.0, IceOptics.Reflectance(critical - 1e-6, Index, 1.0), 1e-12);
        Assert.Equal(1.0, IceOptics.Reflectance(0.0, Index, 1.0), 1e-12);
        Assert.InRange(IceOptics.Reflectance(critical + 1e-3, Index, 1.0), 0.5, 0.99);
        Assert.True(IceOptics.Reflectance(0.9, Index, 1.0) < 0.1);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.6)]
    [InlineData(0.2)]
    public void ReflectanceIsTheSameSeenFromBothSidesOfTheSurface(double cosine)
    {
        const double Index = 1.3110;
        var sineRefracted = Math.Sqrt(1.0 - cosine * cosine) / Index;
        var cosineRefracted = Math.Sqrt(1.0 - sineRefracted * sineRefracted);

        Assert.Equal(IceOptics.Reflectance(cosine, 1.0, Index), IceOptics.Reflectance(cosineRefracted, Index, 1.0), 1e-12);
    }

    [Fact]
    public void ReflectanceGrowsFromNormalToGrazingIncidence()
    {
        var values = Enumerable.Range(0, 100).Select(step => IceOptics.Reflectance(1.0 - step * 0.01, 1.0, 1.3110)).ToArray();

        Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.First <= pair.Second));
        Assert.All(values, value => Assert.InRange(value, 0.0, 1.0));
    }

    [Fact]
    public void ReflectanceIsHeldWithinTheUnitRangeForOutOfRangeCosines()
    {
        Assert.Equal(IceOptics.Reflectance(1.0, 1.0, 1.3110), IceOptics.Reflectance(1.5, 1.0, 1.3110), 1e-12);
        Assert.Equal(IceOptics.Reflectance(0.0, 1.0, 1.3110), IceOptics.Reflectance(-0.5, 1.0, 1.3110), 1e-12);
    }
}
