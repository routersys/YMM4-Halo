namespace Halo.Tests;

internal static class ReferenceTracer
{
    public const double BinDegrees = 1.0;
    public const int AzimuthBins = 180;
    public const int ElevationBins = 80;

    const double Degrees = 180.0 / Math.PI;
    const double Apothem = 0.8660254037844386;
    const int Chunks = 16;

    static readonly Vec3[] Normals = Enumerable.Range(0, 6)
        .Select(face => new Vec3(Math.Cos((30.0 + 60.0 * face) / Degrees), Math.Sin((30.0 + 60.0 * face) / Degrees), 0.0))
        .Concat([new Vec3(0.0, 0.0, 1.0), new Vec3(0.0, 0.0, -1.0)])
        .ToArray();

    public static double[] Sky(CrystalFamily family, double sunElevationDegrees, double lengthRatio, double tiltDegrees, int samples, int seed)
    {
        var plate = family == CrystalFamily.Plate;
        var weights = SolarSpectrum.Weights;
        var indices = Enumerable.Range(0, weights.Length).Select(index => IceOptics.RefractiveIndex(HaloSettings.GetWavelengthNanometers(index))).ToArray();
        var cumulative = new double[weights.Length];
        var total = 0.0;
        for (var index = 0; index < weights.Length; index++)
            cumulative[index] = total += weights[index].Y;

        var chunks = new double[Chunks][];
        Parallel.For(0, Chunks, chunk =>
        {
            var random = new Random(seed + chunk);
            var histogram = new double[AzimuthBins * ElevationBins];
            var events = new List<(Vec3 Direction, double Weight)>();
            var elevation = sunElevationDegrees / Degrees;
            var sun = new Vec3(Math.Cos(elevation), 0.0, Math.Sin(elevation));
            for (var sample = 0; sample < samples / Chunks; sample++)
            {
                var axis = plate ? PlateAxis(tiltDegrees / Degrees, random) : ColumnAxis(tiltDegrees / Degrees, random);
                var first = (Math.Abs(axis.Z) < 0.9 ? new Vec3(0.0, 0.0, 1.0) : new Vec3(1.0, 0.0, 0.0)).Cross(axis).Normalized();
                var second = axis.Cross(first);
                var spin = 2.0 * Math.PI * random.NextDouble();
                var a = Math.Cos(spin) * first + Math.Sin(spin) * second;
                var b = -Math.Sin(spin) * first + Math.Cos(spin) * second;
                var incoming = new Vec3(-sun.Dot(a), -sun.Dot(b), -sun.Dot(axis));
                var band = Pick(cumulative, random.NextDouble() * total);
                events.Clear();
                Trace(incoming, 2.0 * lengthRatio, indices[band], false, false, plate, random, events);
                foreach (var (direction, weight) in events)
                {
                    var sky = -(direction.X * a + direction.Y * b + direction.Z * axis);
                    var azimuth = Math.Abs(Math.Atan2(sky.Y, sky.X)) * Degrees;
                    var offset = Math.Asin(Math.Clamp(sky.Z, -1.0, 1.0)) * Degrees - sunElevationDegrees;
                    var column = (int)Math.Floor(azimuth / BinDegrees);
                    var row = (int)Math.Floor(offset / BinDegrees + ElevationBins / 2.0);
                    if (column >= 0 && column < AzimuthBins && row >= 0 && row < ElevationBins)
                        histogram[row * AzimuthBins + column] += weight;
                }
            }

            chunks[chunk] = histogram;
        });

        var sum = new double[AzimuthBins * ElevationBins];
        foreach (var histogram in chunks)
        {
            for (var index = 0; index < sum.Length; index++)
                sum[index] += histogram[index] / samples;
        }

        return sum;
    }

    public static double[] Ring(int samples, int seed)
    {
        var weights = SolarSpectrum.Weights;
        var indices = Enumerable.Range(0, weights.Length).Select(index => IceOptics.RefractiveIndex(HaloSettings.GetWavelengthNanometers(index))).ToArray();
        var cumulative = new double[weights.Length];
        var total = 0.0;
        for (var index = 0; index < weights.Length; index++)
            cumulative[index] = total += weights[index].Y;

        var chunks = new double[Chunks][];
        Parallel.For(0, Chunks, chunk =>
        {
            var random = new Random(seed + chunk);
            var histogram = new double[180];
            var events = new List<(Vec3 Direction, double Weight)>();
            for (var sample = 0; sample < samples / Chunks; sample++)
            {
                var z = 2.0 * random.NextDouble() - 1.0;
                var azimuth = 2.0 * Math.PI * random.NextDouble();
                var radius = Math.Sqrt(1.0 - z * z);
                var incoming = new Vec3(-radius * Math.Cos(azimuth), -radius * Math.Sin(azimuth), -z);
                var band = Pick(cumulative, random.NextDouble() * total);
                events.Clear();
                Trace(incoming, 2.0 * HaloSettings.RandomCrystalLengthRatio, indices[band], true, true, false, random, events);
                foreach (var (direction, weight) in events)
                {
                    var angle = Math.Acos(Math.Clamp(direction.Dot(incoming), -1.0, 1.0)) * Degrees;
                    var bin = (int)Math.Floor(angle);
                    if (bin >= 0 && bin < histogram.Length)
                        histogram[bin] += weight;
                }
            }

            chunks[chunk] = histogram;
        });

        var sum = new double[180];
        foreach (var histogram in chunks)
        {
            for (var index = 0; index < sum.Length; index++)
                sum[index] += histogram[index] / samples;
        }

        return sum;
    }

    static bool Inside(double x, double y)
    {
        for (var face = 0; face < 6; face++)
        {
            if (Normals[face].X * x + Normals[face].Y * y > Apothem)
                return false;
        }

        return true;
    }

    static int Pick(double[] cumulative, double value)
    {
        for (var index = 0; index < cumulative.Length; index++)
        {
            if (value <= cumulative[index])
                return index;
        }

        return cumulative.Length - 1;
    }

    static Vec3 PlateAxis(double tilt, Random random)
    {
        var magnitude = tilt * Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble()));
        var azimuth = 2.0 * Math.PI * random.NextDouble();
        return new Vec3(Math.Sin(magnitude) * Math.Cos(azimuth), Math.Sin(magnitude) * Math.Sin(azimuth), Math.Cos(magnitude));
    }

    static Vec3 ColumnAxis(double tilt, Random random)
    {
        var elevation = tilt * Gaussian(random);
        var azimuth = 2.0 * Math.PI * random.NextDouble();
        return new Vec3(Math.Cos(elevation) * Math.Cos(azimuth), Math.Cos(elevation) * Math.Sin(azimuth), Math.Sin(elevation));
    }

    static double Gaussian(Random random)
        => Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());

    static void Trace(Vec3 incoming, double length, double index, bool endsEnter, bool endsLeave, bool reflect, Random random, List<(Vec3 Direction, double Weight)> events)
    {
        var half = length / 2.0;
        var areas = new double[8];
        var total = 0.0;
        for (var face = 0; face < 8; face++)
        {
            if (face >= 6 && !endsEnter)
                continue;

            var cosine = -incoming.Dot(Normals[face]);
            if (cosine <= 0.0)
                continue;

            areas[face] = (face < 6 ? length : 1.5 * Math.Sqrt(3.0)) * cosine;
            total += areas[face];
        }

        if (total <= 0.0)
            return;

        var pick = random.NextDouble() * total;
        var entry = 0;
        while (entry < 7 && pick > areas[entry])
            pick -= areas[entry++];

        var normal = Normals[entry];
        Vec3 point;
        if (entry < 6)
        {
            var tangent = new Vec3(-normal.Y, normal.X, 0.0);
            point = Apothem * normal + (random.NextDouble() - 0.5) * tangent + new Vec3(0.0, 0.0, (random.NextDouble() - 0.5) * length);
        }
        else
        {
            double x;
            double y;
            do
            {
                x = 2.0 * random.NextDouble() - 1.0;
                y = 2.0 * random.NextDouble() - 1.0;
            }
            while (!Inside(x, y));
            point = new Vec3(x, y, normal.Z * half);
        }

        var cosineIn = -incoming.Dot(normal);
        if (reflect && entry < 6)
            events.Add((incoming + 2.0 * cosineIn * normal, total * IceOptics.Reflectance(cosineIn, 1.0, index)));

        var sineT = Math.Sqrt(1.0 - cosineIn * cosineIn) / index;
        var cosineT = Math.Sqrt(1.0 - sineT * sineT);
        var inner = (1.0 / index) * incoming + (cosineIn / index - cosineT) * normal;
        var best = double.PositiveInfinity;
        var exit = -1;
        for (var face = 0; face < 8; face++)
        {
            var speed = inner.Dot(Normals[face]);
            if (speed <= 1e-12)
                continue;

            var offset = face < 6 ? Apothem : half;
            var distance = (offset - point.Dot(Normals[face])) / speed;
            if (distance > 1e-12 && distance < best)
            {
                best = distance;
                exit = face;
            }
        }

        if (exit < 0 || (exit < 6 && entry < 6 && (entry + 3) % 6 == exit) || (exit >= 6 && !endsLeave))
            return;

        if (exit >= 6 && entry >= 6)
            return;

        var cosineInner = inner.Dot(Normals[exit]);
        var sineOut = index * Math.Sqrt(Math.Max(1.0 - cosineInner * cosineInner, 0.0));
        if (sineOut >= 1.0)
            return;

        var cosineOut = Math.Sqrt(1.0 - sineOut * sineOut);
        var direction = index * inner - (index * cosineInner - cosineOut) * Normals[exit];
        var transmittance = (1.0 - IceOptics.Reflectance(cosineIn, 1.0, index)) * (1.0 - IceOptics.Reflectance(cosineInner, index, 1.0));
        events.Add((direction, total * transmittance));
    }
}
