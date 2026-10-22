using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace Halo.Tests;

[Collection("Direct2D")]
public sealed class HaloEffectProcessorTests
{
    const int Width = 128;
    const int Height = 96;
    const int Length = 30;
    const int Tolerance = 3;

    static readonly Bgra Sky = Bgra.Opaque(230, 150, 100);
    static readonly ConcurrentDictionary<(bool Plate, double Elevation, double Length, double Tilt), float[]> Raw = new();

    static Bgra SkyBlue(int x, int y) => Sky;

    static Bgra HalfTransparent(int x, int y)
        => x < Width / 2 ? Sky : new Bgra(115, 75, 50, 128);

    static Bgra Transparent(int x, int y) => y < Height / 3 ? Bgra.Transparent : Sky;

    static HaloEffect Wide(double amount = 100d)
    {
        var effect = new HaloEffect();
        effect.Amount.Values[0].Value = amount;
        effect.FieldOfView.Values[0].Value = 90d;
        return effect;
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static float[] Table(bool plate, double elevation, double length, double tilt)
        => Raw.GetOrAdd((plate, elevation, length, tilt), key => CrystalScatter.Integrate(key.Plate ? CrystalFamily.Plate : CrystalFamily.Column, key.Elevation, key.Length, key.Tilt));

    sealed class Model
    {
        readonly Vector3 sun;
        readonly float halfFieldTangent;
        readonly float pitchCosine;
        readonly float pitchSine;
        readonly float sunAzimuth;
        readonly float sunElevation;
        readonly Half[] ring;
        readonly Half[] scatter;
        readonly float amount;

        public Model(HaloEffect effect, int frame)
        {
            double Value(Animation animation) => animation.GetValue(frame, Length, EffectDescriptions.Fps);
            var elevation = Math.Clamp(Value(effect.SunElevation), HaloSettings.MinimumSunElevationDegrees, HaloSettings.MaximumSunElevationDegrees);
            var quantized = HaloSettings.QuantizeSunElevation(elevation);
            var pitch = Value(effect.CameraPitch);
            sun = HaloSettings.GetSunDirection(elevation, Value(effect.SunDirection), pitch);
            halfFieldTangent = HaloSettings.GetHalfFieldOfViewTangent(Value(effect.FieldOfView));
            pitchCosine = (float)Math.Cos(pitch * HaloSettings.RadiansPerDegree);
            pitchSine = (float)Math.Sin(pitch * HaloSettings.RadiansPerDegree);
            sunAzimuth = (float)(Value(effect.SunDirection) * HaloSettings.RadiansPerDegree);
            sunElevation = (float)elevation;
            amount = (float)(Value(effect.Amount) / 100.0);
            var diameter = Value(effect.SunDiameter);
            var saturation = Value(effect.Saturation) / 100.0;
            var plateShare = Value(effect.PlateHalos) / 100.0;
            var columnShare = Value(effect.ColumnArcs) / 100.0;
            var tilt = Value(effect.TiltSpread);
            ring = HaloTable.ComposeRing(diameter, Value(effect.RandomHalos) / 100.0, saturation);
            var empty = new float[ScatterGrid.Width * ScatterGrid.Height * HaloSettings.ColorChannels];
            scatter = HaloTable.ComposeScatter(
                plateShare > 0.0 ? Table(true, quantized, Value(effect.PlateThickness), tilt) : empty,
                columnShare > 0.0 ? Table(false, quantized, Value(effect.ColumnLength), tilt) : empty,
                plateShare,
                columnShare,
                quantized,
                diameter,
                saturation);
        }

        public Bgra Expected(Func<int, int, Bgra> source, int x, int y)
            => HaloShaderModel.Shade(source(x, y).Premultiplied(), x, y, Width, Height, sun, halfFieldTangent, pitchCosine, pitchSine, sunAzimuth, sunElevation, ring, scatter, amount);
    }

    static void AssertClose(Bgra expected, Bgra actual, string where)
    {
        Assert.True(Math.Abs(expected.Blue - actual.Blue) <= Tolerance, $"{where} blue {expected} {actual}");
        Assert.True(Math.Abs(expected.Green - actual.Green) <= Tolerance, $"{where} green {expected} {actual}");
        Assert.True(Math.Abs(expected.Red - actual.Red) <= Tolerance, $"{where} red {expected} {actual}");
        Assert.Equal(expected.Alpha, actual.Alpha);
    }

    static int ChangedPixels(Rendering rendering, SourceImage source)
        => rendering.Coordinates().Count(point => rendering[point.X, point.Y] != source[point.X, point.Y]);

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = Wide().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void WithoutAmountTheProcessorPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = Wide(0d).CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    [Fact]
    public void EveryPixelFollowsTheModelOfTheShader()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var model = new Model(effect, 0);
        Assert.All(rendering.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), rendering[point.X, point.Y], $"{point}"));
        Assert.True(ChangedPixels(rendering, source) > Width * Height / 10);
    }

    [Fact]
    public void TheInnerHaloAppearsAsACircleAroundTheSun()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.PlateHalos.Values[0].Value = 0d;
        effect.ColumnArcs.Values[0].Value = 0d;
        effect.RandomHalos.Values[0].Value = 100d;
        effect.SunDirection.Values[0].Value = 0d;
        effect.SunElevation.Values[0].Value = 30d;
        effect.CameraPitch.Values[0].Value = 30d;
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var column = Width / 2;
        var changed = Enumerable.Range(0, Height / 2).Where(y => rendering[column, y] != source[column, y]).ToArray();
        Assert.NotEmpty(changed);
        var focal = 0.5f * Width / HaloSettings.GetHalfFieldOfViewTangent(90d);
        var expectedTop = Height / 2.0 - focal * Math.Tan(21.7 * Math.PI / 180.0);
        Assert.InRange(changed.Max(), expectedTop - 4.0, expectedTop + 4.0);
        Assert.Equal(source[column, Height / 2], rendering[column, Height / 2]);
    }

    [Fact]
    public void WithNoCrystalsTheImageIsLeftAsItWas()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.RandomHalos.Values[0].Value = 0d;
        effect.PlateHalos.Values[0].Value = 0d;
        effect.ColumnArcs.Values[0].Value = 0d;
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.All(rendering.Coordinates(), point => AssertClose(source[point.X, point.Y], rendering[point.X, point.Y], $"{point}"));
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.SunElevation.CopyFrom(Linear(10d, 40d));
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 5);
        RenderFrame(context, processor, 20);
        var again = RenderFrame(context, processor, 5);

        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameHaloAsOneThatAlreadyDrewAnotherFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.SunElevation.CopyFrom(Linear(10d, 40d));
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var used = effect.CreateVideoEffect(context);
        using var fresh = effect.CreateVideoEffect(context);
        used.SetInput(source.Bitmap);
        fresh.SetInput(source.Bitmap);

        RenderFrame(context, used, 0);
        var reused = RenderFrame(context, used, 25);
        var created = RenderFrame(context, fresh, 25);

        Assert.True(reused.SamePixelsAs(created));
    }

    [Fact]
    public void AnimatedSunElevationIsReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.SunElevation.CopyFrom(Linear(8d, 45d));
        effect.CameraPitch.Values[0].Value = 20d;
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);

        Assert.False(start.SamePixelsAs(end));
        var model = new Model(effect, 0);
        Assert.All(start.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), start[point.X, point.Y], $"{point}"));
        model = new Model(effect, Length - 1);
        Assert.All(end.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), end[point.X, point.Y], $"{point}"));
    }

    public static readonly TheoryData<string, Action<HaloEffect>> Changes = new()
    {
        { nameof(HaloEffect.Amount), effect => effect.Amount.Values[0].Value = 40d },
        { nameof(HaloEffect.Saturation), effect => effect.Saturation.Values[0].Value = 0d },
        { nameof(HaloEffect.SunElevation), effect => effect.SunElevation.Values[0].Value = 38d },
        { nameof(HaloEffect.SunDirection), effect => effect.SunDirection.Values[0].Value = 25d },
        { nameof(HaloEffect.SunDiameter), effect => effect.SunDiameter.Values[0].Value = 1.8d },
        { nameof(HaloEffect.FieldOfView), effect => effect.FieldOfView.Values[0].Value = 120d },
        { nameof(HaloEffect.CameraPitch), effect => effect.CameraPitch.Values[0].Value = 12d },
        { nameof(HaloEffect.RandomHalos), effect => effect.RandomHalos.Values[0].Value = 0d },
        { nameof(HaloEffect.PlateHalos), effect => effect.PlateHalos.Values[0].Value = 0d },
        { nameof(HaloEffect.ColumnArcs), effect => effect.ColumnArcs.Values[0].Value = 0d },
        { nameof(HaloEffect.PlateThickness), effect => effect.PlateThickness.Values[0].Value = 0.1d },
        { nameof(HaloEffect.ColumnLength), effect => effect.ColumnLength.Values[0].Value = 5d },
        { nameof(HaloEffect.TiltSpread), effect => effect.TiltSpread.Values[0].Value = 3.5d },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheShader(string setting, Action<HaloEffect> change)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 3);
        change(effect);
        var after = RenderFrame(context, processor, 3);

        Assert.False(before.SamePixelsAs(after), setting);
        var model = new Model(effect, 3);
        Assert.All(after.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), after[point.X, point.Y], $"{setting} {point}"));
    }

    [Fact]
    public void ASwitchedOffCrystalFamilyComesBackWithTheCurrentSettings()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        RenderFrame(context, processor, 0);
        effect.ColumnArcs.Values[0].Value = 0d;
        RenderFrame(context, processor, 0);
        effect.ColumnLength.Values[0].Value = 5d;
        effect.SunElevation.Values[0].Value = 12d;
        effect.ColumnArcs.Values[0].Value = 60d;
        var returned = RenderFrame(context, processor, 0);

        var model = new Model(effect, 0);
        Assert.All(returned.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), returned[point.X, point.Y], $"{point}"));
    }

    [Fact]
    public void ReducingTheAmountToZeroRestoresTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var shown = RenderFrame(context, processor, 0);
        effect.Amount.Values[0].Value = 0d;
        var hidden = RenderFrame(context, processor, 0);

        Assert.False(shown.SamePixelsAs(hidden));
        Assert.All(hidden.Coordinates(), point => Assert.Equal(source[point.X, point.Y], hidden[point.X, point.Y]));
    }

    [Fact]
    public void TransparentPixelsStayTransparentAndTheOthersKeepTheirOpacity()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, Transparent);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var model = new Model(effect, 0);
        Assert.All(rendering.Coordinates(), point =>
        {
            Assert.Equal(source[point.X, point.Y].Alpha, rendering[point.X, point.Y].Alpha);
            AssertClose(model.Expected(Transparent, point.X, point.Y), rendering[point.X, point.Y], $"{point}");
        });
        Assert.Equal(Bgra.Transparent, rendering[Width / 2, 2]);
    }

    [Fact]
    public void ASemiTransparentSourceKeepsItsOpacityAndBlendsTheHaloByCoverage()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        using var source = new SourceImage(context, Width, Height, HalfTransparent);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var model = new Model(effect, 0);
        Assert.All(rendering.Coordinates(), point =>
        {
            AssertClose(model.Expected(HalfTransparent, point.X, point.Y), rendering[point.X, point.Y], $"{point}");
            Assert.InRange(rendering[point.X, point.Y].Blue, 0, rendering[point.X, point.Y].Alpha);
            Assert.InRange(rendering[point.X, point.Y].Green, 0, rendering[point.X, point.Y].Alpha);
            Assert.InRange(rendering[point.X, point.Y].Red, 0, rendering[point.X, point.Y].Alpha);
        });
    }

    [Fact]
    public void TheAmountScalesTheLightAddedToTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide(50d);
        using var source = new SourceImage(context, Width, Height, SkyBlue);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        var model = new Model(effect, 0);
        Assert.All(rendering.Coordinates(), point => AssertClose(model.Expected(SkyBlue, point.X, point.Y), rendering[point.X, point.Y], $"{point}"));
    }

    [Fact]
    public void TheImageSizeOnlyScalesTheHaloAndKeepsItsAngularSize()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Wide();
        effect.PlateHalos.Values[0].Value = 0d;
        effect.ColumnArcs.Values[0].Value = 0d;
        using var small = new SourceImage(context, Width, Height, SkyBlue);
        using var large = new SourceImage(context, Width * 2, Height * 2, SkyBlue);
        using var smallProcessor = effect.CreateVideoEffect(context);
        using var largeProcessor = effect.CreateVideoEffect(context);
        smallProcessor.SetInput(small.Bitmap);
        largeProcessor.SetInput(large.Bitmap);

        var first = RenderFrame(context, smallProcessor, 0);
        var second = RenderFrame(context, largeProcessor, 0);

        int Edge(Rendering rendering, int column, int rows)
            => Enumerable.Range(0, rows / 2).Where(y => rendering[column, y].Red != Sky.Premultiplied().Red).Max();

        Assert.InRange(Math.Abs(Edge(first, Width / 2, Height) - Edge(second, Width, Height * 2) / 2.0), 0.0, 2.0);
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = Wide().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }

    [Fact]
    public void AFailureWhileCreatingTheProcessorIsNotSwallowed()
    {
        Assert.ThrowsAny<Exception>(() => Wide().CreateVideoEffect(null!));
    }
}
