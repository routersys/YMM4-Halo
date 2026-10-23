using System.Runtime.CompilerServices;

namespace Halo;

internal enum CrystalFamily
{
    Plate,
    Column,
}

internal static class CrystalScatter
{
    private const double SpinPeriodRadians = Math.PI / 3.0;
    private const double HalfTurnDegrees = 180.0;
    private const double FullTurnDegrees = 360.0;
    private const double MergeSpreadDegrees = 0.075;

    private readonly record struct Layer(double Tilt, double Weight);

    private readonly record struct Chunk(Layer Layer, int FirstStrip, int StripCount);

    private struct Sample
    {
        public double Azimuth;
        public double Elevation;
        public double Weight;
        public bool Valid;
    }

    private struct Corner
    {
        public bool Valid;
        public double Azimuth;
        public double Elevation;
        public double Spread;
        public Tristimulus Weight;
    }

    private sealed class SliceData(int nodeCount, SpectralBand[] bands)
    {
        public SpectralBand[] Bands { get; } = bands;
        public int WavelengthCount { get; } = bands.Length;
        public PrismPath[][] Paths { get; } = Enumerable.Range(0, nodeCount).Select(_ => new PrismPath[PrismGeometry.MaximumPathCount]).ToArray();
        public int[] PathCounts { get; } = new int[nodeCount];
        public Vec3[] Incoming { get; } = new Vec3[nodeCount];
        public Vec3[] FirstAxes { get; } = new Vec3[nodeCount];
        public Vec3[] SecondAxes { get; } = new Vec3[nodeCount];
        public Sample[] Samples { get; } = new Sample[PrismGeometry.LabelCount * nodeCount * bands.Length];
        public bool[] Used { get; } = new bool[PrismGeometry.LabelCount];
        private int NodeCount { get; } = nodeCount;

        public int Slot(int label, int node, int wavelength)
            => (label * NodeCount + node) * WavelengthCount + wavelength;

        public void Reset()
        {
            for (var label = 0; label < Used.Length; label++)
            {
                if (!Used[label])
                    continue;

                Array.Clear(Samples, label * NodeCount * WavelengthCount, NodeCount * WavelengthCount);
                Used[label] = false;
            }
        }

        public ReadOnlySpan<Sample> Wavelengths(int label, int node)
            => Samples.AsSpan((label * NodeCount + node) * WavelengthCount, WavelengthCount);
    }

    private sealed class Workspace(int nodeCount, SpectralBand[] bands)
    {
        public ScatterGrid Grid { get; } = new();
        public SliceData[] Slices { get; } = [new(nodeCount, bands), new(nodeCount, bands)];
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
        var layers = plate ? PlateLayers(tiltDegrees) : ColumnLayers(tiltDegrees);
        var chunks = BuildChunks(layers, azimuthSteps);
        var bands = SolarSpectrum.Bands(plate ? 1 : HaloSettings.ColumnWavelengthStride);
        var total = new ScatterGrid();
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
                    var axis = plate ? PlateAxis(chunk.Layer.Tilt, azimuth) : ColumnAxis(chunk.Layer.Tilt, azimuth);
                    FillSlice(current, axis, incoming, length, spinSteps, kinds, sunElevationDegrees);
                    if (strip > 0)
                        SplatStrip(workspace.Grid, workspace.Slices[(strip - 1) & 1], current, spinSteps, quadWeight);
                }

                return workspace;
            },
            workspace =>
            {
                lock (total)
                    total.Add(workspace.Grid);
            });
        return ToRadiance(total, sunElevationDegrees);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FillSlice(SliceData data, Vec3 axis, Vec3 incoming, double length, int spinSteps, PathKinds kinds, double sunElevationDegrees)
    {
        var referenceIndex = IceOptics.RefractiveIndex(HaloSettings.ReferenceWavelengthNanometers);
        var helper = Math.Abs(axis.Z) < 0.9 ? new Vec3(0.0, 0.0, 1.0) : new Vec3(1.0, 0.0, 0.0);
        var firstBase = helper.Cross(axis).Normalized();
        var secondBase = axis.Cross(firstBase);
        for (var node = 0; node <= spinSteps; node++)
        {
            var (sine, cosine) = Math.SinCos(node * SpinPeriodRadians / spinSteps);
            var first = (cosine * firstBase) + (sine * secondBase);
            var second = (-sine * firstBase) + (cosine * secondBase);
            data.FirstAxes[node] = first;
            data.SecondAxes[node] = second;
            var local = new Vec3(incoming.Dot(first), incoming.Dot(second), incoming.Dot(axis));
            data.Incoming[node] = local;
            data.PathCounts[node] = PrismGeometry.Enumerate(local, length, referenceIndex, kinds, data.Paths[node]);
        }

        data.Reset();
        for (var wavelength = 0; wavelength < data.WavelengthCount; wavelength++)
        {
            var index = IceOptics.RefractiveIndex(data.Bands[wavelength].Wavelength);
            for (var node = 0; node <= spinSteps; node++)
            {
                var paths = data.Paths[node];
                for (var path = 0; path < data.PathCounts[node]; path++)
                {
                    var current = paths[path];
                    Vec3 exit;
                    double factor;
                    var found = current.Exit == PrismGeometry.ReflectionExit
                        ? PrismGeometry.TryReflect(data.Incoming[node], current, index, out exit, out factor)
                        : PrismGeometry.TryRefract(data.Incoming[node], current, index, out exit, out factor);
                    if (!found)
                        continue;

                    var sky = -((exit.X * data.FirstAxes[node]) + (exit.Y * data.SecondAxes[node]) + (exit.Z * axis));
                    data.Samples[data.Slot(current.Label, node, wavelength)] = new Sample
                    {
                        Azimuth = Math.Atan2(sky.Y, sky.X) / HaloSettings.RadiansPerDegree,
                        Elevation = Math.Asin(Math.Clamp(sky.Z, -1.0, 1.0)) / HaloSettings.RadiansPerDegree - sunElevationDegrees,
                        Weight = current.Geometry * factor,
                        Valid = true,
                    };
                    data.Used[current.Label] = true;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplatStrip(ScatterGrid grid, SliceData previous, SliceData current, int spinSteps, double quadWeight)
    {
        for (var label = 0; label < PrismGeometry.LabelCount; label++)
        {
            if (!previous.Used[label] && !current.Used[label])
                continue;

            for (var node = 0; node < spinSteps; node++)
            {
                SplatQuad(
                    grid,
                    previous.Bands,
                    previous.Wavelengths(label, node),
                    previous.Wavelengths(label, node + 1),
                    current.Wavelengths(label, node),
                    current.Wavelengths(label, node + 1),
                    quadWeight);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplatQuad(ScatterGrid grid, SpectralBand[] bands, ReadOnlySpan<Sample> first, ReadOnlySpan<Sample> second, ReadOnlySpan<Sample> third, ReadOnlySpan<Sample> fourth, double quadWeight)
    {
        var c00 = Combine(first, bands);
        var c01 = Combine(second, bands);
        var c10 = Combine(third, bands);
        var c11 = Combine(fourth, bands);
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Corner Combine(ReadOnlySpan<Sample> samples, SpectralBand[] bands)
    {
        var total = new Tristimulus();
        var luminance = 0.0;
        var azimuth = 0.0;
        var elevation = 0.0;
        var reference = double.NaN;
        for (var wavelength = 0; wavelength < samples.Length; wavelength++)
        {
            var sample = samples[wavelength];
            if (!sample.Valid)
                continue;

            if (double.IsNaN(reference))
                reference = sample.Azimuth;
            var weight = sample.Weight * bands[wavelength].Weight.Y;
            total += sample.Weight * bands[wavelength].Weight;
            luminance += weight;
            azimuth += weight * Unwrap(sample.Azimuth, reference);
            elevation += weight * sample.Elevation;
        }

        if (double.IsNaN(reference))
            return default;
        if (luminance <= 0.0)
            return new Corner { Valid = true, Azimuth = reference };

        var center = new Corner { Valid = true, Azimuth = azimuth / luminance, Elevation = elevation / luminance, Weight = total };
        var spread = 0.0;
        for (var wavelength = 0; wavelength < samples.Length; wavelength++)
        {
            if (!samples[wavelength].Valid)
                continue;

            var offset = Math.Max(Math.Abs(Unwrap(samples[wavelength].Azimuth, reference) - center.Azimuth), Math.Abs(samples[wavelength].Elevation - center.Elevation));
            spread = Math.Max(spread, offset);
        }

        center.Spread = spread;
        return center;
    }

    private static Corner Single(Sample sample, Tristimulus spectrum)
        => sample.Valid ? new Corner { Valid = true, Azimuth = sample.Azimuth, Elevation = sample.Elevation, Weight = sample.Weight * spectrum } : default;

    private static Corner Complete(Corner corner, Corner anchor)
    {
        if (!corner.Valid)
            return new Corner { Valid = true, Azimuth = anchor.Azimuth, Elevation = anchor.Elevation };

        corner.Azimuth = Unwrap(corner.Azimuth, anchor.Azimuth);
        return corner;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SplatPatch(ScatterGrid grid, Corner c00, Corner c01, Corner c10, Corner c11, double quadWeight)
    {
        var along = Math.Max(Distance(c00, c10), Distance(c01, c11));
        var across = Math.Max(Distance(c00, c01), Distance(c10, c11));
        var stepDegrees = HaloSettings.SubStepTexels * HaloSettings.ScatterTexelDegrees;
        var alongSteps = Math.Clamp((int)Math.Ceiling(along / stepDegrees), 1, HaloSettings.MaximumSubSteps);
        var acrossSteps = Math.Clamp((int)Math.Ceiling(across / stepDegrees), 1, HaloSettings.MaximumSubSteps);
        var unit = quadWeight / (alongSteps * acrossSteps);
        for (var first = 0; first < alongSteps; first++)
        {
            var s = (first + 0.5) / alongSteps;
            for (var second = 0; second < acrossSteps; second++)
            {
                var t = (second + 0.5) / acrossSteps;
                var w00 = (1.0 - s) * (1.0 - t);
                var w01 = (1.0 - s) * t;
                var w10 = s * (1.0 - t);
                var w11 = s * t;
                var weight = (w00 * c00.Weight) + (w01 * c01.Weight) + (w10 * c10.Weight) + (w11 * c11.Weight);
                grid.Splat(
                    w00 * c00.Azimuth + w01 * c01.Azimuth + w10 * c10.Azimuth + w11 * c11.Azimuth,
                    w00 * c00.Elevation + w01 * c01.Elevation + w10 * c10.Elevation + w11 * c11.Elevation,
                    unit * weight);
            }
        }
    }

    private static double Unwrap(double azimuth, double reference)
    {
        if (azimuth - reference > HalfTurnDegrees)
            return azimuth - FullTurnDegrees;
        return reference - azimuth > HalfTurnDegrees ? azimuth + FullTurnDegrees : azimuth;
    }

    private static double Distance(Corner first, Corner second)
        => Math.Max(Math.Abs(first.Azimuth - second.Azimuth), Math.Abs(first.Elevation - second.Elevation));

    private static Chunk[] BuildChunks(Layer[] layers, int azimuthSteps)
    {
        var chunks = new List<Chunk>();
        foreach (var layer in layers)
        {
            for (var strip = 0; strip < azimuthSteps; strip += HaloSettings.StripsPerTask)
                chunks.Add(new Chunk(layer, strip, Math.Min(HaloSettings.StripsPerTask, azimuthSteps - strip)));
        }

        return [.. chunks];
    }

    private static Layer[] PlateLayers(double tiltDegrees)
    {
        var nodes = HaloSettings.PlateTiltRadialNodes;
        var tilt = tiltDegrees * HaloSettings.RadiansPerDegree;
        var layers = new Layer[nodes];
        for (var node = 0; node < nodes; node++)
            layers[node] = new Layer(tilt * HaloSettings.GetPlateTiltQuantile(node), 1.0 / nodes);
        return layers;
    }

    private static Layer[] ColumnLayers(double tiltDegrees)
    {
        var quantiles = HaloSettings.ColumnTiltQuantiles;
        var tilt = tiltDegrees * HaloSettings.RadiansPerDegree;
        return [.. quantiles.Select(quantile => new Layer(tilt * quantile, 1.0 / quantiles.Length))];
    }

    private static Vec3 PlateAxis(double tilt, double azimuth)
        => new(Math.Sin(tilt) * Math.Cos(azimuth), Math.Sin(tilt) * Math.Sin(azimuth), Math.Cos(tilt));

    private static Vec3 ColumnAxis(double tilt, double azimuth)
        => new(Math.Cos(tilt) * Math.Cos(azimuth), Math.Cos(tilt) * Math.Sin(azimuth), Math.Sin(tilt));

    private static float[] ToRadiance(ScatterGrid grid, double sunElevationDegrees)
    {
        var texel = HaloSettings.ScatterTexelDegrees * HaloSettings.RadiansPerDegree;
        var radiance = new float[grid.Cells.Length];
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            var elevation = Math.Min(Math.Abs(HaloSettings.GetScatterElevationDegrees(row, sunElevationDegrees)), 90.0) * HaloSettings.RadiansPerDegree;
            var solidAngle = Math.Max(Math.Cos(elevation), HaloSettings.ScatterMinimumCosine) * texel * texel;
            var scale = 1.0 / (2.0 * solidAngle * ScatterGrid.FixedScale);
            var start = row * ScatterGrid.Width * HaloSettings.ColorChannels;
            for (var index = start; index < start + ScatterGrid.Width * HaloSettings.ColorChannels; index++)
                radiance[index] = (float)(grid.Cells[index] * scale);
        }

        return radiance;
    }
}
