using YukkuriMovieMaker.Commons;
using static Halo.HaloCustomEffect;

namespace Halo.Tests;

[Collection("Direct2D")]
public sealed class HaloCustomEffectTests
{
    static float Read(HaloCustomEffect effect, PropertyIndex index) => effect.GetFloatValue((int)index);

    static Rendering Render(IGraphicsDevicesAndContext devices, SourceImage source, Action<HaloCustomEffect> configure)
    {
        using var ring = SourceImage.Solid(devices, HaloSettings.RingTableWidth, 1, Bgra.Opaque(200, 40, 40));
        using var scatter = SourceImage.Solid(devices, ScatterGrid.Width, ScatterGrid.Height, Bgra.Opaque(10, 10, 10));
        using var effect = new HaloCustomEffect(devices);
        effect.SetInput(0, source.Bitmap, true);
        effect.SetInput(1, ring.Bitmap, true);
        effect.SetInput(2, scatter.Bitmap, true);
        configure(effect);
        using var output = effect.Output;

        return Rendering.Capture(devices, output);
    }

    [Fact]
    public void TheEffectIsEnabledOnceCreated()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        Assert.True(effect.IsEnabled);
    }

    [Fact]
    public void ThePropertiesStartFromTheirDefaults()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        Assert.Equal(0f, Read(effect, PropertyIndex.Amount));
        Assert.Equal(0f, Read(effect, PropertyIndex.SunX));
        Assert.Equal(0f, Read(effect, PropertyIndex.SunY));
        Assert.Equal(1f, Read(effect, PropertyIndex.SunZ));
        Assert.Equal(1f, Read(effect, PropertyIndex.HalfFieldTangent));
        Assert.Equal(1f, Read(effect, PropertyIndex.PitchCosine));
        Assert.Equal(0f, Read(effect, PropertyIndex.PitchSine));
        Assert.Equal(0f, Read(effect, PropertyIndex.SunAzimuth));
        Assert.Equal(0f, Read(effect, PropertyIndex.SunElevation));
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1f, 1f)]
    [InlineData(1.5f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    [InlineData(float.NaN, 0f)]
    public void TheAmountIsClampedToTheUnitRange(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.Amount = value;

        Assert.Equal(expected, Read(effect, PropertyIndex.Amount));
    }

    [Theory]
    [InlineData(-2f, -1f)]
    [InlineData(-0.5f, -0.5f)]
    [InlineData(0f, 0f)]
    [InlineData(0.75f, 0.75f)]
    [InlineData(3f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, -1f)]
    [InlineData(float.NaN, 0f)]
    public void TheSunDirectionAndThePitchStayInsideTheUnitCube(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.SunX = value;
        effect.SunY = value;
        effect.SunZ = value;
        effect.PitchCosine = value;
        effect.PitchSine = value;

        Assert.Equal(expected, Read(effect, PropertyIndex.SunX));
        Assert.Equal(expected, Read(effect, PropertyIndex.SunY));
        Assert.Equal(expected, Read(effect, PropertyIndex.SunZ));
        Assert.Equal(expected, Read(effect, PropertyIndex.PitchCosine));
        Assert.Equal(expected, Read(effect, PropertyIndex.PitchSine));
    }

    [Theory]
    [InlineData(-10f, -MathF.PI)]
    [InlineData(-1f, -1f)]
    [InlineData(2f, 2f)]
    [InlineData(10f, MathF.PI)]
    [InlineData(float.NaN, 0f)]
    public void TheSunAzimuthStaysWithinAHalfTurnEitherWay(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.SunAzimuth = value;

        Assert.Equal(expected, Read(effect, PropertyIndex.SunAzimuth));
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(33f, 33f)]
    [InlineData(120f, 90f)]
    [InlineData(float.NaN, 0f)]
    public void TheSunElevationStaysBetweenTheHorizonAndTheZenith(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.SunElevation = value;

        Assert.Equal(expected, Read(effect, PropertyIndex.SunElevation));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(3.7f)]
    public void TheHalfFieldTangentKeepsValuesInsideTheFieldOfViewLimits(float value)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.HalfFieldTangent = value;

        Assert.Equal(value, Read(effect, PropertyIndex.HalfFieldTangent));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(0f)]
    [InlineData(float.NegativeInfinity)]
    public void TheHalfFieldTangentNeverFallsBelowTheNarrowestFieldOfView(float value)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.HalfFieldTangent = value;

        Assert.Equal(HaloSettings.MinimumHalfFieldTangent, Read(effect, PropertyIndex.HalfFieldTangent));
    }

    [Theory]
    [InlineData(4f)]
    [InlineData(1e5f)]
    [InlineData(float.PositiveInfinity)]
    public void TheHalfFieldTangentNeverExceedsTheWidestFieldOfView(float value)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.HalfFieldTangent = value;

        Assert.Equal(HaloSettings.MaximumHalfFieldTangent, Read(effect, PropertyIndex.HalfFieldTangent));
    }

    [Fact]
    public void ANonNumberLeavesTheHalfFieldTangentAtItsDefault()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new HaloCustomEffect(context);

        effect.HalfFieldTangent = float.NaN;

        Assert.Equal(1f, Read(effect, PropertyIndex.HalfFieldTangent));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.6f)]
    [InlineData(1f)]
    public void TheOutputBoundsEqualTheInput(float amount)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, 40, 24, Bgra.Opaque(10, 20, 30));
        using var ring = SourceImage.Solid(context, HaloSettings.RingTableWidth, 1, Bgra.Opaque(10, 20, 30));
        using var scatter = SourceImage.Solid(context, ScatterGrid.Width, ScatterGrid.Height, Bgra.Opaque(10, 20, 30));
        using var effect = new HaloCustomEffect(context);
        effect.SetInput(0, source.Bitmap, true);
        effect.SetInput(1, ring.Bitmap, true);
        effect.SetInput(2, scatter.Bitmap, true);
        effect.Amount = amount;
        using var output = effect.Output;

        var bounds = context.DeviceContext.GetImageLocalBounds(output);

        Assert.Equal((0f, 0f, 40f, 24f), (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
    }

    [Fact]
    public void WithoutAnAmountTheInputPassesThroughUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, 48, 32, (x, y) => new Bgra((byte)(x * 5), (byte)(y * 7), (byte)(x + y), (byte)(100 + x)));

        var rendered = Render(context, source, effect =>
        {
            effect.Amount = 0f;
            effect.SunZ = 1f;
        });

        Assert.True(rendered.SamePixelsAs(Rendering.Capture(context, source.Bitmap)));
    }

    [Fact]
    public void PixelsOutsideBothTablesPassThroughUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, 48, 32, (x, y) => new Bgra((byte)(x * 5), (byte)(y * 7), (byte)(x + y), 255));

        var rendered = Render(context, source, effect =>
        {
            effect.Amount = 1f;
            effect.SunX = 0f;
            effect.SunY = 0f;
            effect.SunZ = -1f;
            effect.HalfFieldTangent = 0.1f;
            effect.SunElevation = 90f;
        });

        Assert.True(rendered.SamePixelsAs(Rendering.Capture(context, source.Bitmap)));
    }

    [Fact]
    public void ARingAroundTheSunAddsLightToTheOpaquePixels()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, 64, 64, Bgra.Opaque(40, 40, 40));

        var rendered = Render(context, source, effect =>
        {
            effect.Amount = 1f;
            effect.SunX = 0f;
            effect.SunY = 0f;
            effect.SunZ = 1f;
            effect.HalfFieldTangent = 1f;
        });

        var untouched = Rendering.Capture(context, source.Bitmap);
        var lit = rendered.Coordinates().Count(point => rendered[point.X, point.Y].Red > untouched[point.X, point.Y].Red);

        Assert.True(lit > 0);
    }

    [Fact]
    public void TransparentPixelsStayTransparent()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, 64, 64, (x, _) => x < 32 ? Bgra.Transparent : Bgra.Opaque(40, 40, 40));

        var rendered = Render(context, source, effect =>
        {
            effect.Amount = 1f;
            effect.HalfFieldTangent = 1f;
        });

        Assert.All(rendered.Coordinates().Where(point => point.X < 32), point => Assert.Equal(Bgra.Transparent, rendered[point.X, point.Y]));
    }
}
