using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;

namespace Halo;

[PluginDetails(AuthorName = "routersys")]
[VideoEffect(nameof(Texts.Halo), [VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], [nameof(Texts.TagHalo), nameof(Texts.TagSky), nameof(Texts.TagLight)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class HaloEffect : VideoEffectBase
{
    public override string Label => Texts.Halo;

    public HaloEffect()
    {
        HaloTelemetry.EnsureStartedOnce();
        HaloUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Saturation), Description = nameof(Texts.SaturationDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100 * HaloSettings.MaximumSaturation)]
    public Animation Saturation { get; } = new Animation(100, 0, 100 * HaloSettings.MaximumSaturation);

    [Display(GroupName = nameof(Texts.SunGroup), Name = nameof(Texts.SunElevation), Description = nameof(Texts.SunElevationDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", HaloSettings.MinimumSunElevationDegrees, HaloSettings.MaximumSunElevationDegrees)]
    public Animation SunElevation { get; } = new Animation(30, HaloSettings.MinimumSunElevationDegrees, HaloSettings.MaximumSunElevationDegrees);

    [Display(GroupName = nameof(Texts.SunGroup), Name = nameof(Texts.SunDirection), Description = nameof(Texts.SunDirectionDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation SunDirection { get; } = new Animation(0, -180, 180);

    [Display(GroupName = nameof(Texts.SunGroup), Name = nameof(Texts.SunDiameter), Description = nameof(Texts.SunDiameterDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "°", 0, HaloSettings.MaximumSunDiameterDegrees)]
    public Animation SunDiameter { get; } = new Animation(HaloSettings.ReferenceSunDiameterDegrees, 0, HaloSettings.MaximumSunDiameterDegrees);

    [Display(GroupName = nameof(Texts.CameraGroup), Name = nameof(Texts.FieldOfView), Description = nameof(Texts.FieldOfViewDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", HaloSettings.MinimumFieldOfViewDegrees, HaloSettings.MaximumFieldOfViewDegrees)]
    public Animation FieldOfView { get; } = new Animation(100, HaloSettings.MinimumFieldOfViewDegrees, HaloSettings.MaximumFieldOfViewDegrees);

    [Display(GroupName = nameof(Texts.CameraGroup), Name = nameof(Texts.CameraPitch), Description = nameof(Texts.CameraPitchDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", HaloSettings.MinimumCameraPitchDegrees, HaloSettings.MaximumCameraPitchDegrees)]
    public Animation CameraPitch { get; } = new Animation(30, HaloSettings.MinimumCameraPitchDegrees, HaloSettings.MaximumCameraPitchDegrees);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.RandomHalos), Description = nameof(Texts.RandomHalosDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation RandomHalos { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.PlateHalos), Description = nameof(Texts.PlateHalosDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation PlateHalos { get; } = new Animation(70, 0, 100);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.ColumnArcs), Description = nameof(Texts.ColumnArcsDescription), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation ColumnArcs { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.PlateThickness), Description = nameof(Texts.PlateThicknessDescription), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "", HaloSettings.MinimumPlateThickness, HaloSettings.MaximumPlateThickness)]
    public Animation PlateThickness { get; } = new Animation(0.4, HaloSettings.MinimumPlateThickness, HaloSettings.MaximumPlateThickness);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.ColumnLength), Description = nameof(Texts.ColumnLengthDescription), Order = 34, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "", HaloSettings.MinimumColumnLength, HaloSettings.MaximumColumnLength)]
    public Animation ColumnLength { get; } = new Animation(2, HaloSettings.MinimumColumnLength, HaloSettings.MaximumColumnLength);

    [Display(GroupName = nameof(Texts.CrystalGroup), Name = nameof(Texts.TiltSpread), Description = nameof(Texts.TiltSpreadDescription), Order = 35, ResourceType = typeof(Texts))]
    [AnimationSlider("F2", "°", HaloSettings.MinimumTiltDegrees, HaloSettings.MaximumTiltDegrees)]
    public Animation TiltSpread { get; } = new Animation(1.5, HaloSettings.MinimumTiltDegrees, HaloSettings.MaximumTiltDegrees);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        try
        {
            return new HaloEffectProcessor(devices, this);
        }
        catch (Exception exception)
        {
            HaloTelemetry.Report(exception);
            throw;
        }
    }

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Saturation, SunElevation, SunDirection, SunDiameter, FieldOfView, CameraPitch, RandomHalos, PlateHalos, ColumnArcs, PlateThickness, ColumnLength, TiltSpread];
}
