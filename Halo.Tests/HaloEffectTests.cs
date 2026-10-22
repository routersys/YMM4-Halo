using System.ComponentModel.DataAnnotations;
using System.Reflection;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace Halo.Tests;

public sealed class HaloEffectTests
{
    static PropertyInfo Property(string name) => typeof(HaloEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(HaloEffect effect)
        => [effect.Amount, effect.Saturation, effect.SunElevation, effect.SunDirection, effect.SunDiameter, effect.FieldOfView, effect.CameraPitch, effect.RandomHalos, effect.PlateHalos, effect.ColumnArcs, effect.PlateThickness, effect.ColumnLength, effect.TiltSpread];

    [Theory]
    [InlineData(nameof(HaloEffect.Amount), 60d, 0d, 100d)]
    [InlineData(nameof(HaloEffect.Saturation), 100d, 0d, 200d)]
    [InlineData(nameof(HaloEffect.SunElevation), 30d, 0d, 90d)]
    [InlineData(nameof(HaloEffect.SunDirection), 0d, -180d, 180d)]
    [InlineData(nameof(HaloEffect.SunDiameter), 0.53d, 0d, 2d)]
    [InlineData(nameof(HaloEffect.FieldOfView), 100d, 10d, 150d)]
    [InlineData(nameof(HaloEffect.CameraPitch), 30d, -60d, 90d)]
    [InlineData(nameof(HaloEffect.RandomHalos), 100d, 0d, 100d)]
    [InlineData(nameof(HaloEffect.PlateHalos), 70d, 0d, 100d)]
    [InlineData(nameof(HaloEffect.ColumnArcs), 40d, 0d, 100d)]
    [InlineData(nameof(HaloEffect.PlateThickness), 0.4d, 0.05d, 1d)]
    [InlineData(nameof(HaloEffect.ColumnLength), 2d, 1d, 6d)]
    [InlineData(nameof(HaloEffect.TiltSpread), 1.5d, 0.1d, 5d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new HaloEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void TheRangesMatchTheLimitsTheModelCovers()
    {
        var effect = new HaloEffect();

        Assert.Equal(HaloSettings.MinimumSunElevationDegrees, effect.SunElevation.MinValue);
        Assert.Equal(HaloSettings.MaximumSunElevationDegrees, effect.SunElevation.MaxValue);
        Assert.Equal(HaloSettings.MaximumSunDiameterDegrees, effect.SunDiameter.MaxValue);
        Assert.Equal(HaloSettings.MinimumFieldOfViewDegrees, effect.FieldOfView.MinValue);
        Assert.Equal(HaloSettings.MaximumFieldOfViewDegrees, effect.FieldOfView.MaxValue);
        Assert.Equal(HaloSettings.MinimumCameraPitchDegrees, effect.CameraPitch.MinValue);
        Assert.Equal(HaloSettings.MaximumCameraPitchDegrees, effect.CameraPitch.MaxValue);
        Assert.Equal(HaloSettings.MinimumPlateThickness, effect.PlateThickness.MinValue);
        Assert.Equal(HaloSettings.MaximumPlateThickness, effect.PlateThickness.MaxValue);
        Assert.Equal(HaloSettings.MinimumColumnLength, effect.ColumnLength.MinValue);
        Assert.Equal(HaloSettings.MaximumColumnLength, effect.ColumnLength.MaxValue);
        Assert.Equal(HaloSettings.MinimumTiltDegrees, effect.TiltSpread.MinValue);
        Assert.Equal(HaloSettings.MaximumTiltDegrees, effect.TiltSpread.MaxValue);
    }

    [Fact]
    public void TheAnimatedParametersAreListedOnceAndReused()
    {
        var effect = new HaloEffect();
        var method = typeof(HaloEffect).GetMethod("GetAnimatables", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;
        var second = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;

        Assert.Same(first, second);
        Assert.Equal(Animations(effect), first);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        Assert.Equal(Texts.Halo, new HaloEffect().Label);
    }

    [Fact]
    public void TheThirteenNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new HaloEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.Equal(13, Animations(effect).Length);
        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new HaloEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheAuthorIsDeclared()
    {
        var details = typeof(HaloEffect).GetCustomAttribute<PluginDetailsAttribute>()!;

        Assert.Equal("routersys", details.AuthorName);
    }

    [Fact]
    public void TheEffectIsRegisteredForFilteringAndDecorationWithoutAviUtlSupport()
    {
        var attribute = typeof(HaloEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.Halo), attribute.Name);
        Assert.Equal([VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], attribute.Categories);
        Assert.Equal([nameof(Texts.TagHalo), nameof(Texts.TagSky), nameof(Texts.TagLight)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.Halo, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(HaloEffect.Amount), nameof(Texts.BasicGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(HaloEffect.Saturation), nameof(Texts.BasicGroup), nameof(Texts.Saturation), nameof(Texts.SaturationDescription), 1)]
    [InlineData(nameof(HaloEffect.SunElevation), nameof(Texts.SunGroup), nameof(Texts.SunElevation), nameof(Texts.SunElevationDescription), 10)]
    [InlineData(nameof(HaloEffect.SunDirection), nameof(Texts.SunGroup), nameof(Texts.SunDirection), nameof(Texts.SunDirectionDescription), 11)]
    [InlineData(nameof(HaloEffect.SunDiameter), nameof(Texts.SunGroup), nameof(Texts.SunDiameter), nameof(Texts.SunDiameterDescription), 12)]
    [InlineData(nameof(HaloEffect.FieldOfView), nameof(Texts.CameraGroup), nameof(Texts.FieldOfView), nameof(Texts.FieldOfViewDescription), 20)]
    [InlineData(nameof(HaloEffect.CameraPitch), nameof(Texts.CameraGroup), nameof(Texts.CameraPitch), nameof(Texts.CameraPitchDescription), 21)]
    [InlineData(nameof(HaloEffect.RandomHalos), nameof(Texts.CrystalGroup), nameof(Texts.RandomHalos), nameof(Texts.RandomHalosDescription), 30)]
    [InlineData(nameof(HaloEffect.PlateHalos), nameof(Texts.CrystalGroup), nameof(Texts.PlateHalos), nameof(Texts.PlateHalosDescription), 31)]
    [InlineData(nameof(HaloEffect.ColumnArcs), nameof(Texts.CrystalGroup), nameof(Texts.ColumnArcs), nameof(Texts.ColumnArcsDescription), 32)]
    [InlineData(nameof(HaloEffect.PlateThickness), nameof(Texts.CrystalGroup), nameof(Texts.PlateThickness), nameof(Texts.PlateThicknessDescription), 33)]
    [InlineData(nameof(HaloEffect.ColumnLength), nameof(Texts.CrystalGroup), nameof(Texts.ColumnLength), nameof(Texts.ColumnLengthDescription), 34)]
    [InlineData(nameof(HaloEffect.TiltSpread), nameof(Texts.CrystalGroup), nameof(Texts.TiltSpread), nameof(Texts.TiltSpreadDescription), 35)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(HaloEffect.Amount), "F1", "%", 0d, 100d)]
    [InlineData(nameof(HaloEffect.Saturation), "F1", "%", 0d, 200d)]
    [InlineData(nameof(HaloEffect.SunElevation), "F1", "°", 0d, 90d)]
    [InlineData(nameof(HaloEffect.SunDirection), "F1", "°", -180d, 180d)]
    [InlineData(nameof(HaloEffect.SunDiameter), "F2", "°", 0d, 2d)]
    [InlineData(nameof(HaloEffect.FieldOfView), "F1", "°", 10d, 150d)]
    [InlineData(nameof(HaloEffect.CameraPitch), "F1", "°", -60d, 90d)]
    [InlineData(nameof(HaloEffect.RandomHalos), "F1", "%", 0d, 100d)]
    [InlineData(nameof(HaloEffect.PlateHalos), "F1", "%", 0d, 100d)]
    [InlineData(nameof(HaloEffect.ColumnArcs), "F1", "%", 0d, 100d)]
    [InlineData(nameof(HaloEffect.PlateThickness), "F2", "", 0.05d, 1d)]
    [InlineData(nameof(HaloEffect.ColumnLength), "F2", "", 1d, 6d)]
    [InlineData(nameof(HaloEffect.TiltSpread), "F2", "°", 0.1d, 5d)]
    public void AnimatedParametersAreEditedWithAnimationSliders(string property, string format, string unit, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal(format, slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new HaloEffect();
        var values = new[] { 55d, 140d, 33d, -70d, 1.25d, 80d, -12d, 44d, 77d, 21d, 0.31d, 3.5d, 2.25d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
