using System.Runtime.CompilerServices;

namespace Halo;

internal static class ScatterSplat
{
    private const double MergeSpreadDegrees = 0.075;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Strip(ScatterGrid grid, StripSlice previous, StripSlice current, double quadWeight)
    {
        for (var label = 0; label < PrismGeometry.LabelCount; label++)
        {
            if (!previous.Used[label] && !current.Used[label])
                continue;

            for (var node = 0; node < current.SpinSteps; node++)
            {
                Quad(
                    grid,
                    previous.Bands,
                    previous.Wavelengths(label, node),
                    previous.Wavelengths(label, node + 1),
                    current.Wavelengths(label, node),
                    current.Wavelengths(label, node + 1),
                    previous.CornerAt(label, node),
                    previous.CornerAt(label, node + 1),
                    current.CornerAt(label, node),
                    current.CornerAt(label, node + 1),
                    quadWeight);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Quad(
        ScatterGrid grid,
        SpectralBand[] bands,
        ReadOnlySpan<ExitSample> samples00,
        ReadOnlySpan<ExitSample> samples01,
        ReadOnlySpan<ExitSample> samples10,
        ReadOnlySpan<ExitSample> samples11,
        in Corner c00,
        in Corner c01,
        in Corner c10,
        in Corner c11,
        double quadWeight)
    {
        var reference = FirstValid(c00, c01, c10, c11);
        if (!reference.Valid)
            return;

        if (c00.Spread <= MergeSpreadDegrees && c01.Spread <= MergeSpreadDegrees && c10.Spread <= MergeSpreadDegrees && c11.Spread <= MergeSpreadDegrees)
        {
            Patch(grid, AlignTo(c00, reference), AlignTo(c01, reference), AlignTo(c10, reference), AlignTo(c11, reference), quadWeight);
            return;
        }

        for (var wavelength = 0; wavelength < bands.Length; wavelength++)
        {
            var bandWeight = bands[wavelength].Weight;
            var band00 = BandCorner(samples00[wavelength], bandWeight);
            var band01 = BandCorner(samples01[wavelength], bandWeight);
            var band10 = BandCorner(samples10[wavelength], bandWeight);
            var band11 = BandCorner(samples11[wavelength], bandWeight);
            var bandReference = FirstValid(band00, band01, band10, band11);
            if (bandReference.Valid)
                Patch(grid, AlignTo(band00, bandReference), AlignTo(band01, bandReference), AlignTo(band10, bandReference), AlignTo(band11, bandReference), quadWeight);
        }
    }

    private static Corner FirstValid(in Corner first, in Corner second, in Corner third, in Corner fourth)
        => first.Valid ? first : second.Valid ? second : third.Valid ? third : fourth;

    private static Corner BandCorner(ExitSample sample, Tristimulus bandWeight)
        => sample.Valid ? new Corner { Valid = true, Azimuth = sample.Azimuth, Elevation = sample.Elevation, Weight = sample.Weight * bandWeight } : default;

    private static Corner AlignTo(Corner corner, Corner reference)
    {
        if (!corner.Valid)
            return new Corner { Valid = true, Azimuth = reference.Azimuth, Elevation = reference.Elevation };

        corner.Azimuth = Corner.Unwrap(corner.Azimuth, reference.Azimuth);
        return corner;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void Patch(ScatterGrid grid, in Corner c00, in Corner c01, in Corner c10, in Corner c11, double quadWeight)
    {
        var along = Math.Max(Distance(c00, c10), Distance(c01, c11));
        var across = Math.Max(Distance(c00, c01), Distance(c10, c11));
        var stepDegrees = HaloSettings.SubStepTexels * HaloSettings.ScatterTexelDegrees;
        var alongSteps = Math.Clamp((int)Math.Ceiling(along / stepDegrees), 1, HaloSettings.MaximumSubSteps);
        var acrossSteps = Math.Clamp((int)Math.Ceiling(across / stepDegrees), 1, HaloSettings.MaximumSubSteps);
        var unit = quadWeight / (alongSteps * acrossSteps);
        var ts = stackalloc double[HaloSettings.MaximumSubSteps];
        var complements = stackalloc double[HaloSettings.MaximumSubSteps];
        for (var second = 0; second < acrossSteps; second++)
        {
            var t = (second + 0.5) / acrossSteps;
            ts[second] = t;
            complements[second] = 1.0 - t;
        }

        for (var first = 0; first < alongSteps; first++)
        {
            var s = (first + 0.5) / alongSteps;
            var complement = 1.0 - s;
            for (var second = 0; second < acrossSteps; second++)
            {
                var t = ts[second];
                var w00 = complement * complements[second];
                var w01 = complement * t;
                var w10 = s * complements[second];
                var w11 = s * t;
                var weight = (w00 * c00.Weight) + (w01 * c01.Weight) + (w10 * c10.Weight) + (w11 * c11.Weight);
                grid.Splat(
                    w00 * c00.Azimuth + w01 * c01.Azimuth + w10 * c10.Azimuth + w11 * c11.Azimuth,
                    w00 * c00.Elevation + w01 * c01.Elevation + w10 * c10.Elevation + w11 * c11.Elevation,
                    unit * weight);
            }
        }
    }

    private static double Distance(in Corner first, in Corner second)
        => Math.Max(Math.Abs(first.Azimuth - second.Azimuth), Math.Abs(first.Elevation - second.Elevation));
}
