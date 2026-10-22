using System.Runtime.CompilerServices;

namespace Halo;

[Flags]
internal enum PathKinds
{
    None = 0,
    SideToSide = 1,
    SideToEnd = 2,
    EndToSide = 4,
    Reflection = 8,
}

internal readonly record struct PrismPath(int Entry, int Exit, double Geometry)
{
    public int Label => Entry * PrismGeometry.ExitKindCount + Exit;
}

internal static class PrismGeometry
{
    public const int SideFaceCount = 6;
    public const int UpperEnd = 6;
    public const int LowerEnd = 7;
    public const int ReflectionExit = 8;
    public const int EntryKindCount = 8;
    public const int ExitKindCount = 9;
    public const int LabelCount = EntryKindCount * ExitKindCount;
    public const int MaximumPathCount = LabelCount;

    private const double MinimumCosine = 1e-9;
    private const double MinimumProjection = 1e-12;
    private const double FirstSideAngleDegrees = 30.0;
    private const double SideAngleStepDegrees = 60.0;

    public static readonly double Apothem = Math.Sqrt(3.0) / 2.0;
    public static readonly double EndFaceArea = 1.5 * Math.Sqrt(3.0);

    private static readonly Vec3[] Normals = BuildNormals();
    private static readonly (double X, double Y)[] EndSamples = BuildEndSamples(HaloSettings.EndFaceSubdivisions);

    public static Vec3 Normal(int face)
        => Normals[face];

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static Vec3 Refract(Vec3 incoming, Vec3 outwardNormal, double index)
    {
        var cosineIncidence = -incoming.Dot(outwardNormal);
        var eta = 1.0 / index;
        var cosineRefraction = Math.Sqrt(1.0 - eta * eta * (1.0 - cosineIncidence * cosineIncidence));
        return (eta * incoming) + ((eta * cosineIncidence - cosineRefraction) * outwardNormal);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Enumerate(Vec3 incoming, double length, double index, PathKinds kinds, Span<PrismPath> output)
    {
        var count = 0;
        for (var face = 0; face < SideFaceCount; face++)
        {
            var cosine = -incoming.Dot(Normals[face]);
            if (cosine <= MinimumCosine)
                continue;

            if ((kinds & (PathKinds.SideToSide | PathKinds.SideToEnd)) != 0)
                count = AddSideEntry(incoming, face, cosine, length, index, kinds, output, count);
            if ((kinds & PathKinds.Reflection) != 0)
                output[count++] = new PrismPath(face, ReflectionExit, length * cosine);
        }

        if ((kinds & PathKinds.EndToSide) != 0)
        {
            count = AddEndEntry(incoming, UpperEnd, length, index, output, count);
            count = AddEndEntry(incoming, LowerEnd, length, index, output, count);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryRefract(Vec3 incoming, PrismPath path, double index, out Vec3 exitDirection, out double transmittance)
    {
        exitDirection = default;
        transmittance = 0.0;
        var entryNormal = Normals[path.Entry];
        var cosineEntry = -incoming.Dot(entryNormal);
        var inner = Refract(incoming, entryNormal, index);
        var exitNormal = Normals[path.Exit];
        var cosineInner = inner.Dot(exitNormal);
        if (cosineInner <= MinimumCosine)
            return false;

        var sineOuter = index * Math.Sqrt(Math.Max(1.0 - cosineInner * cosineInner, 0.0));
        if (sineOuter >= 1.0)
            return false;

        var cosineOuter = Math.Sqrt(1.0 - sineOuter * sineOuter);
        exitDirection = (index * inner) - ((index * cosineInner - cosineOuter) * exitNormal);
        transmittance = (1.0 - IceOptics.Reflectance(cosineEntry, IceOptics.AirRefractiveIndex, index))
            * (1.0 - IceOptics.Reflectance(cosineInner, index, IceOptics.AirRefractiveIndex));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryReflect(Vec3 incoming, PrismPath path, double index, out Vec3 exitDirection, out double reflectance)
    {
        var normal = Normals[path.Entry];
        var cosine = -incoming.Dot(normal);
        exitDirection = incoming + ((2.0 * cosine) * normal);
        reflectance = IceOptics.Reflectance(cosine, IceOptics.AirRefractiveIndex, index);
        return cosine > MinimumCosine;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int AddSideEntry(Vec3 incoming, int face, double cosine, double length, double index, PathKinds kinds, Span<PrismPath> output, int count)
    {
        var entryNormal = Normals[face];
        var inner = Refract(incoming, entryNormal, index);
        var tangentX = -entryNormal.Y;
        var tangentY = entryNormal.X;
        var verticalSlope = Math.Abs(inner.Z);
        Span<double> offsets = stackalloc double[SideFaceCount];
        Span<double> slopes = stackalloc double[SideFaceCount];
        Span<bool> active = stackalloc bool[SideFaceCount];
        for (var other = 0; other < SideFaceCount; other++)
        {
            active[other] = false;
            if (other == face)
                continue;

            var projection = inner.Dot(Normals[other]);
            if (projection <= MinimumProjection)
                continue;

            var otherNormal = Normals[other];
            active[other] = true;
            offsets[other] = Apothem * (1.0 - (entryNormal.X * otherNormal.X + entryNormal.Y * otherNormal.Y)) / projection;
            slopes[other] = -(tangentX * otherNormal.X + tangentY * otherNormal.Y) / projection;
        }

        Span<double> breaks = stackalloc double[SideFaceCount * SideFaceCount + 2];
        var breakCount = 0;
        breaks[breakCount++] = -0.5;
        for (var first = 0; first < SideFaceCount; first++)
        {
            if (!active[first])
                continue;

            for (var second = first + 1; second < SideFaceCount; second++)
            {
                if (!active[second] || Math.Abs(slopes[first] - slopes[second]) <= MinimumProjection)
                    continue;

                var position = (offsets[second] - offsets[first]) / (slopes[first] - slopes[second]);
                if (position > -0.5 && position < 0.5)
                    breaks[breakCount++] = position;
            }
        }

        breaks[breakCount++] = 0.5;
        breaks[..breakCount].Sort();

        Span<double> sideWeights = stackalloc double[SideFaceCount];
        sideWeights.Clear();
        var endWeight = 0.0;
        for (var interval = 0; interval + 1 < breakCount; interval++)
        {
            var start = breaks[interval];
            var end = breaks[interval + 1];
            if (end - start <= 0.0)
                continue;

            var middle = 0.5 * (start + end);
            var exit = -1;
            var nearest = double.PositiveInfinity;
            for (var other = 0; other < SideFaceCount; other++)
            {
                if (!active[other])
                    continue;

                var distance = offsets[other] + slopes[other] * middle;
                if (distance < nearest)
                {
                    nearest = distance;
                    exit = other;
                }
            }

            if (exit < 0)
                continue;

            var surviving = IntegratePositive(1.0 - verticalSlope * offsets[exit] / length, -verticalSlope * slopes[exit] / length, start, end);
            sideWeights[exit] += cosine * length * surviving;
            endWeight += cosine * length * ((end - start) - surviving);
        }

        if ((kinds & PathKinds.SideToSide) != 0)
        {
            for (var other = 0; other < SideFaceCount; other++)
            {
                if (sideWeights[other] > 0.0 && other != OppositeSide(face))
                    output[count++] = new PrismPath(face, other, sideWeights[other]);
            }
        }

        if ((kinds & PathKinds.SideToEnd) != 0 && endWeight > 0.0)
            output[count++] = new PrismPath(face, inner.Z > 0.0 ? UpperEnd : LowerEnd, endWeight);

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int AddEndEntry(Vec3 incoming, int end, double length, double index, Span<PrismPath> output, int count)
    {
        var entryNormal = Normals[end];
        var cosine = -incoming.Dot(entryNormal);
        if (cosine <= MinimumCosine)
            return count;

        var inner = Refract(incoming, entryNormal, index);
        var travel = length / Math.Abs(inner.Z);
        Span<double> sideWeights = stackalloc double[SideFaceCount];
        sideWeights.Clear();
        var sampleWeight = cosine * EndFaceArea / EndSamples.Length;
        foreach (var (x, y) in EndSamples)
        {
            var exit = -1;
            var nearest = travel;
            for (var other = 0; other < SideFaceCount; other++)
            {
                var otherNormal = Normals[other];
                var projection = inner.Dot(otherNormal);
                if (projection <= MinimumProjection)
                    continue;

                var distance = (Apothem - (x * otherNormal.X + y * otherNormal.Y)) / projection;
                if (distance < nearest)
                {
                    nearest = distance;
                    exit = other;
                }
            }

            if (exit >= 0)
                sideWeights[exit] += sampleWeight;
        }

        for (var other = 0; other < SideFaceCount; other++)
        {
            if (sideWeights[other] > 0.0)
                output[count++] = new PrismPath(end, other, sideWeights[other]);
        }

        return count;
    }

    private static int OppositeSide(int face)
        => (face + SideFaceCount / 2) % SideFaceCount;

    private static double IntegratePositive(double intercept, double slope, double start, double end)
    {
        var first = intercept + slope * start;
        var second = intercept + slope * end;
        if (first >= 0.0 && second >= 0.0)
            return 0.5 * (first + second) * (end - start);
        if (first <= 0.0 && second <= 0.0)
            return 0.0;

        var positive = Math.Max(first, second);
        var fraction = positive / Math.Abs(first - second);
        return 0.5 * positive * (end - start) * fraction;
    }

    private static Vec3[] BuildNormals()
    {
        var normals = new Vec3[EntryKindCount];
        for (var face = 0; face < SideFaceCount; face++)
        {
            var angle = (FirstSideAngleDegrees + SideAngleStepDegrees * face) * HaloSettings.RadiansPerDegree;
            normals[face] = new Vec3(Math.Cos(angle), Math.Sin(angle), 0.0);
        }

        normals[UpperEnd] = new Vec3(0.0, 0.0, 1.0);
        normals[LowerEnd] = new Vec3(0.0, 0.0, -1.0);
        return normals;
    }

    private static (double X, double Y)[] BuildEndSamples(int subdivisions)
    {
        var samples = new List<(double X, double Y)>(SideFaceCount * subdivisions * subdivisions);
        for (var corner = 0; corner < SideFaceCount; corner++)
        {
            var first = corner * SideAngleStepDegrees * HaloSettings.RadiansPerDegree;
            var second = (corner + 1) * SideAngleStepDegrees * HaloSettings.RadiansPerDegree;
            var (bx, by) = (Math.Cos(first), Math.Sin(first));
            var (cx, cy) = (Math.Cos(second), Math.Sin(second));
            for (var row = 0; row < subdivisions; row++)
            {
                for (var column = 0; column < subdivisions - row; column++)
                {
                    samples.Add(Triangle(bx, by, cx, cy, (row + 1.0 / 3.0) / subdivisions, (column + 1.0 / 3.0) / subdivisions));
                    if (column < subdivisions - row - 1)
                        samples.Add(Triangle(bx, by, cx, cy, (row + 2.0 / 3.0) / subdivisions, (column + 2.0 / 3.0) / subdivisions));
                }
            }
        }

        return [.. samples];
    }

    private static (double X, double Y) Triangle(double bx, double by, double cx, double cy, double u, double v)
        => (u * bx + v * cx, u * by + v * cy);
}
