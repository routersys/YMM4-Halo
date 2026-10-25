using System.Runtime.CompilerServices;

namespace Halo;

internal struct ExitSample
{
    public double Azimuth;
    public double Elevation;
    public double Weight;
    public bool Valid;
}

internal struct Corner
{
    public bool Valid;
    public double Azimuth;
    public double Elevation;
    public double Spread;
    public Tristimulus Weight;

    public static double Unwrap(double azimuth, double reference)
    {
        if (azimuth - reference > HaloSettings.HalfTurnDegrees)
            return azimuth - HaloSettings.FullTurnDegrees;
        return reference - azimuth > HaloSettings.HalfTurnDegrees ? azimuth + HaloSettings.FullTurnDegrees : azimuth;
    }
}

internal sealed class StripSlice(int nodeCount, SpectralBand[] bands)
{
    private PrismPath[][] Paths { get; } = Enumerable.Range(0, nodeCount).Select(_ => new PrismPath[PrismGeometry.MaximumPathCount]).ToArray();
    private int[] PathCounts { get; } = new int[nodeCount];
    private Vec3[] Incoming { get; } = new Vec3[nodeCount];
    private Vec3[] FirstAxes { get; } = new Vec3[nodeCount];
    private Vec3[] SecondAxes { get; } = new Vec3[nodeCount];
    private ExitSample[] Samples { get; } = new ExitSample[PrismGeometry.LabelCount * nodeCount * bands.Length];
    private Corner[] Corners { get; } = new Corner[PrismGeometry.LabelCount * nodeCount];
    private int NodeCount { get; } = nodeCount;
    private int WavelengthCount { get; } = bands.Length;

    public SpectralBand[] Bands { get; } = bands;
    public bool[] Used { get; } = new bool[PrismGeometry.LabelCount];
    public int SpinSteps => NodeCount - 1;

    public ref readonly Corner CornerAt(int label, int node)
        => ref Corners[label * NodeCount + node];

    public ReadOnlySpan<ExitSample> Wavelengths(int label, int node)
        => Samples.AsSpan((label * NodeCount + node) * WavelengthCount, WavelengthCount);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Fill(Vec3 axis, Vec3 incoming, double length, PathKinds kinds, double sunElevationDegrees)
    {
        var referenceIndex = IceOptics.RefractiveIndex(HaloSettings.ReferenceWavelengthNanometers);
        var helper = Math.Abs(axis.Z) < 0.9 ? new Vec3(0.0, 0.0, 1.0) : new Vec3(1.0, 0.0, 0.0);
        var firstBase = helper.Cross(axis).Normalized();
        var secondBase = axis.Cross(firstBase);
        for (var node = 0; node < NodeCount; node++)
        {
            var (sine, cosine) = Math.SinCos(node * PrismGeometry.SpinPeriodRadians / SpinSteps);
            var first = (cosine * firstBase) + (sine * secondBase);
            var second = (-sine * firstBase) + (cosine * secondBase);
            FirstAxes[node] = first;
            SecondAxes[node] = second;
            var local = new Vec3(incoming.Dot(first), incoming.Dot(second), incoming.Dot(axis));
            Incoming[node] = local;
            PathCounts[node] = PrismGeometry.Enumerate(local, length, referenceIndex, kinds, Paths[node]);
        }

        Reset();
        for (var wavelength = 0; wavelength < WavelengthCount; wavelength++)
        {
            var index = IceOptics.RefractiveIndex(Bands[wavelength].Wavelength);
            for (var node = 0; node < NodeCount; node++)
            {
                var paths = Paths[node];
                for (var path = 0; path < PathCounts[node]; path++)
                {
                    var current = paths[path];
                    Vec3 exit;
                    double factor;
                    var found = current.Exit == PrismGeometry.ReflectionExit
                        ? PrismGeometry.TryReflect(Incoming[node], current, index, out exit, out factor)
                        : PrismGeometry.TryRefract(Incoming[node], current, index, out exit, out factor);
                    if (!found)
                        continue;

                    var sky = -((exit.X * FirstAxes[node]) + (exit.Y * SecondAxes[node]) + (exit.Z * axis));
                    Samples[Slot(current.Label, node, wavelength)] = new ExitSample
                    {
                        Azimuth = Math.Atan2(sky.Y, sky.X) / HaloSettings.RadiansPerDegree,
                        Elevation = Math.Asin(Math.Clamp(sky.Z, -1.0, 1.0)) / HaloSettings.RadiansPerDegree - sunElevationDegrees,
                        Weight = current.Geometry * factor,
                        Valid = true,
                    };
                    Used[current.Label] = true;
                }
            }
        }

        MergeCorners();
    }

    private int Slot(int label, int node, int wavelength)
        => (label * NodeCount + node) * WavelengthCount + wavelength;

    private void Reset()
    {
        for (var label = 0; label < Used.Length; label++)
        {
            if (!Used[label])
                continue;

            Array.Clear(Samples, label * NodeCount * WavelengthCount, NodeCount * WavelengthCount);
            Array.Clear(Corners, label * NodeCount, NodeCount);
            Used[label] = false;
        }
    }

    private void MergeCorners()
    {
        for (var label = 0; label < Used.Length; label++)
        {
            if (!Used[label])
                continue;

            for (var node = 0; node < NodeCount; node++)
                Corners[label * NodeCount + node] = MergeBands(Wavelengths(label, node), Bands);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static Corner MergeBands(ReadOnlySpan<ExitSample> samples, SpectralBand[] bands)
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
            azimuth += weight * Corner.Unwrap(sample.Azimuth, reference);
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

            var offset = Math.Max(Math.Abs(Corner.Unwrap(samples[wavelength].Azimuth, reference) - center.Azimuth), Math.Abs(samples[wavelength].Elevation - center.Elevation));
            spread = Math.Max(spread, offset);
        }

        center.Spread = spread;
        return center;
    }
}
