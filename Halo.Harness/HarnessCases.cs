using System.Globalization;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;

namespace Halo.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, HaloEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("sun-elevation-animated-frames", Create(effect => effect.SunElevation.CopyFrom(Linear(10d, 50d))), [0, 100, 200, 299]);
        yield return ("sun-direction-animated-frames", Create(effect => effect.SunDirection.CopyFrom(Linear(-40d, 40d))), [0, 100, 200, 299]);
        yield return ("tilt-spread-animated-frames", Create(effect => effect.TiltSpread.CopyFrom(Linear(0.3d, 4d))), [0, 100, 200, 299]);
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("amount-30", Create(effect => effect.Amount.Values[0].Value = 30), [0]);
        yield return ("saturation-0", Create(effect => effect.Saturation.Values[0].Value = 0), [0]);
        yield return ("saturation-200", Create(effect => effect.Saturation.Values[0].Value = 200), [0]);
        yield return ("sun-elevation-8", Create(effect => { effect.SunElevation.Values[0].Value = 8; effect.CameraPitch.Values[0].Value = 8; }), [0]);
        yield return ("sun-elevation-50", Create(effect => { effect.SunElevation.Values[0].Value = 50; effect.CameraPitch.Values[0].Value = 50; }), [0]);
        yield return ("sun-direction-minus-30", Create(effect => effect.SunDirection.Values[0].Value = -30), [0]);
        yield return ("sun-direction-30", Create(effect => effect.SunDirection.Values[0].Value = 30), [0]);
        yield return ("sun-diameter-0", Create(effect => effect.SunDiameter.Values[0].Value = 0), [0]);
        yield return ("sun-diameter-2", Create(effect => effect.SunDiameter.Values[0].Value = 2), [0]);
        yield return ("field-of-view-50", Create(effect => effect.FieldOfView.Values[0].Value = 50), [0]);
        yield return ("field-of-view-140", Create(effect => effect.FieldOfView.Values[0].Value = 140), [0]);
        yield return ("camera-pitch-minus-10", Create(effect => effect.CameraPitch.Values[0].Value = -10), [0]);
        yield return ("camera-pitch-60", Create(effect => effect.CameraPitch.Values[0].Value = 60), [0]);
        yield return ("random-halos-0", Create(effect => effect.RandomHalos.Values[0].Value = 0), [0]);
        yield return ("plate-halos-0", Create(effect => effect.PlateHalos.Values[0].Value = 0), [0]);
        yield return ("column-arcs-0", Create(effect => effect.ColumnArcs.Values[0].Value = 0), [0]);
        yield return ("plate-halos-only", Create(effect => { effect.RandomHalos.Values[0].Value = 0; effect.ColumnArcs.Values[0].Value = 0; effect.PlateHalos.Values[0].Value = 100; }), [0]);
        yield return ("column-arcs-only", Create(effect => { effect.RandomHalos.Values[0].Value = 0; effect.PlateHalos.Values[0].Value = 0; effect.ColumnArcs.Values[0].Value = 100; }), [0]);
        yield return ("plate-thickness-0.05", Create(effect => effect.PlateThickness.Values[0].Value = 0.05), [0]);
        yield return ("plate-thickness-1", Create(effect => effect.PlateThickness.Values[0].Value = 1), [0]);
        yield return ("column-length-1", Create(effect => effect.ColumnLength.Values[0].Value = 1), [0]);
        yield return ("column-length-6", Create(effect => effect.ColumnLength.Values[0].Value = 6), [0]);
        yield return ("tilt-spread-0.1", Create(effect => effect.TiltSpread.Values[0].Value = 0.1), [0]);
        yield return ("tilt-spread-5", Create(effect => effect.TiltSpread.Values[0].Value = 5), [0]);
    }

    public static IEnumerable<(string Name, Func<HaloEffect> Create, Action<HaloEffect> Change, int Frame)> Transitions()
    {
        yield return ("amount-60-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
        yield return ("amount-0-to-60", () => Create(effect => effect.Amount.Values[0].Value = 0), effect => effect.Amount.Values[0].Value = 60, 0);
        yield return ("amount-60-to-30", () => Create(), effect => effect.Amount.Values[0].Value = 30, 0);
        yield return ("saturation-100-to-0", () => Create(), effect => effect.Saturation.Values[0].Value = 0, 0);
        yield return ("sun-elevation-30-to-45", () => Create(), effect => effect.SunElevation.Values[0].Value = 45, 0);
        yield return ("sun-direction-0-to-30", () => Create(), effect => effect.SunDirection.Values[0].Value = 30, 0);
        yield return ("sun-diameter-0.53-to-2", () => Create(), effect => effect.SunDiameter.Values[0].Value = 2, 0);
        yield return ("field-of-view-100-to-60", () => Create(), effect => effect.FieldOfView.Values[0].Value = 60, 0);
        yield return ("camera-pitch-30-to-10", () => Create(), effect => effect.CameraPitch.Values[0].Value = 10, 0);
        yield return ("random-halos-100-to-0", () => Create(), effect => effect.RandomHalos.Values[0].Value = 0, 0);
        yield return ("plate-halos-70-to-0", () => Create(), effect => effect.PlateHalos.Values[0].Value = 0, 0);
        yield return ("plate-halos-0-to-70", () => Create(effect => effect.PlateHalos.Values[0].Value = 0), effect => effect.PlateHalos.Values[0].Value = 70, 0);
        yield return ("column-arcs-40-to-0", () => Create(), effect => effect.ColumnArcs.Values[0].Value = 0, 0);
        yield return ("column-arcs-0-to-40", () => Create(effect => effect.ColumnArcs.Values[0].Value = 0), effect => effect.ColumnArcs.Values[0].Value = 40, 0);
        yield return ("plate-thickness-0.4-to-0.1", () => Create(), effect => effect.PlateThickness.Values[0].Value = 0.1, 0);
        yield return ("column-length-2-to-5", () => Create(), effect => effect.ColumnLength.Values[0].Value = 5, 0);
        yield return ("tilt-spread-1.5-to-4", () => Create(), effect => effect.TiltSpread.Values[0].Value = 4, 0);
    }

    public static IEnumerable<(string Name, HaloEffect Effect)> Benchmarks()
    {
        yield return ("default", Create());
        yield return ("sun-direction-animated", Create(effect => effect.SunDirection.CopyFrom(Linear(-40d, 40d))));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}""")) ?? throw new HarnessException("アニメーションを読み込めません。");

    public static HaloEffect Create(Action<HaloEffect>? configure = null)
    {
        var effect = new HaloEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
