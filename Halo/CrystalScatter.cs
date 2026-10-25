using System.Runtime.CompilerServices;

namespace Halo;

internal enum CrystalFamily
{
    Plate,
    Column,
}

internal static class CrystalScatter
{
    private const double MergeSpreadDegrees = 0.075;

    private sealed class Workspace(int nodeCount, SpectralBand[] bands)
    {
        public ScatterGrid Grid { get; } = new();
        public StripSlice[] Slices { get; } = [new(nodeCount, bands), new(nodeCount, bands)];
    }

    public static float[] Integrate(CrystalFamily family, double sunElevationDegrees, double lengthRatio, double tiltDegrees)
    {
        var plate = family == CrystalFamily.Plate;
        var spinSteps = plate ? HaloSettings.PlateSpinSteps : HaloSettings.ColumnSpinSteps;
        var azimuthSteps = plate ? HaloSettings.PlateTiltAngleSteps : HaloSettings.ColumnAzimuthSteps;
        var kinds = plate ? PathKinds.SideToSide | PathKinds.Reflection : PathKinds.SideToSide;
        var length = 2.0 * lengthRatio;
        var elevation = sunElevationDegrees * HaloSettings.RadiansPerDegree;
        var incoming = new Vec3(-Math.Cos(elevation), 0.0, -Math.Sin(elevation));
        var layers = plate ? CrystalOrientations.PlateLayers(tiltDegrees) : CrystalOrientations.ColumnLayers(tiltDegrees);
        var chunks = CrystalOrientations.BuildChunks(layers, azimuthSteps);
        var bands = SolarSpectrum.Bands(plate ? 1 : HaloSettings.ColumnWavelengthStride);
        var grids = new System.Collections.Concurrent.ConcurrentBag<ScatterGrid>();
        Parallel.For(
            0,
            chunks.Length,
            () => new Workspace(spinSteps + 1, bands),
            (index, _, workspace) =>
            {
                var chunk = chunks[index];
                var quadWeight = chunk.Layer.Weight / (azimuthSteps * spinSteps);
                for (var strip = 0; strip <= chunk.StripCount; strip++)
                {
                    var current = workspace.Slices[strip & 1];
                    var azimuth = (chunk.FirstStrip + strip) * Math.PI / azimuthSteps;
                    var axis = plate ? CrystalOrientations.PlateAxis(chunk.Layer.Tilt, azimuth) : CrystalOrientations.ColumnAxis(chunk.Layer.Tilt, azimuth);
                    current.Fill(axis, incoming, length, kinds, sunElevationDegrees);
                    if (strip > 0)
                        SplatStrip(workspace.Grid, workspace.Slices[(strip - 1) & 1], current, quadWeight);
                }

                return workspace;
            },
            workspace => grids.Add(workspace.Grid));
        return ToRadiance(ScatterGrid.Sum([.. grids]), sunElevationDegrees);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplatStrip(ScatterGrid grid, StripSlice previous, StripSlice current, double quadWeight)
    {
        for (var label = 0; label < PrismGeometry.LabelCount; label++)
        {
            if (!previous.Used[label] && !current.Used[label])
                continue;

            for (var node = 0; node < current.SpinSteps; node++)
            {
                SplatQuad(
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
    private static void SplatQuad(ScatterGrid grid, SpectralBand[] bands, ReadOnlySpan<ExitSample> first, ReadOnlySpan<ExitSample> second, ReadOnlySpan<ExitSample> third, ReadOnlySpan<ExitSample> fourth, in Corner c00, in Corner c01, in Corner c10, in Corner c11, double quadWeight)
    {
        var anchor = c00.Valid ? c00 : c01.Valid ? c01 : c10.Valid ? c10 : c11;
        if (!anchor.Valid)
            return;

        if (c00.Spread <= MergeSpreadDegrees && c01.Spread <= MergeSpreadDegrees && c10.Spread <= MergeSpreadDegrees && c11.Spread <= MergeSpreadDegrees)
        {
            SplatPatch(grid, Complete(c00, anchor), Complete(c01, anchor), Complete(c10, anchor), Complete(c11, anchor), quadWeight);
            return;
        }

        for (var wavelength = 0; wavelength < bands.Length; wavelength++)
        {
            var spectrum = bands[wavelength].Weight;
            var single00 = Single(first[wavelength], spectrum);
            var single01 = Single(second[wavelength], spectrum);
            var single10 = Single(third[wavelength], spectrum);
            var single11 = Single(fourth[wavelength], spectrum);
            var single = single00.Valid ? single00 : single01.Valid ? single01 : single10.Valid ? single10 : single11;
            if (single.Valid)
                SplatPatch(grid, Complete(single00, single), Complete(single01, single), Complete(single10, single), Complete(single11, single), quadWeight);
        }
    }

    private static Corner Single(ExitSample sample, Tristimulus spectrum)
        => sample.Valid ? new Corner { Valid = true, Azimuth = sample.Azimuth, Elevation = sample.Elevation, Weight = sample.Weight * spectrum } : default;

    private static Corner Complete(Corner corner, Corner anchor)
    {
        if (!corner.Valid)
            return new Corner { Valid = true, Azimuth = anchor.Azimuth, Elevation = anchor.Elevation };

        corner.Azimuth = Corner.Unwrap(corner.Azimuth, anchor.Azimuth);
        return corner;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe void SplatPatch(ScatterGrid grid, in Corner c00, in Corner c01, in Corner c10, in Corner c11, double quadWeight)
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

    private static float[] ToRadiance(ScatterGrid grid, double sunElevationDegrees)
    {
        var texel = HaloSettings.ScatterTexelDegrees * HaloSettings.RadiansPerDegree;
        var radiance = new float[grid.Cells.Length];
        Parallel.For(0, ScatterGrid.Height, row =>
        {
            var elevation = Math.Min(Math.Abs(HaloSettings.GetScatterElevationDegrees(row, sunElevationDegrees)), 90.0) * HaloSettings.RadiansPerDegree;
            var solidAngle = Math.Max(Math.Cos(elevation), HaloSettings.ScatterMinimumCosine) * texel * texel;
            var scale = 1.0 / (2.0 * solidAngle * ScatterGrid.FixedScale);
            var start = row * ScatterGrid.Width * HaloSettings.ColorChannels;
            for (var index = start; index < start + ScatterGrid.Width * HaloSettings.ColorChannels; index++)
                radiance[index] = (float)(grid.Cells[index] * scale);
        });

        return radiance;
    }
}
