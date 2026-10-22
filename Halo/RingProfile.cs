using System.Runtime.CompilerServices;

namespace Halo;

internal static class RingProfile
{
    private static readonly Lazy<double[]> Profile = new(Build);

    public static double[] Radiance => Profile.Value;

    private static double[] Build()
    {
        var width = HaloSettings.RingTableWidth;
        var channels = HaloSettings.ColorChannels;
        var inclinations = HaloSettings.RandomInclinationSteps;
        var azimuths = HaloSettings.RandomAzimuthSteps;
        var length = 2.0 * HaloSettings.RandomCrystalLengthRatio;
        var inclinationStep = Math.PI / 2.0 / inclinations;
        var azimuthStep = Math.PI / 6.0 / azimuths;
        var step = HaloSettings.GetRingStepDegrees();
        var weights = SolarSpectrum.Weights;
        var referenceIndex = IceOptics.RefractiveIndex(HaloSettings.ReferenceWavelengthNanometers);
        var indices = Enumerable.Range(0, weights.Length).Select(index => IceOptics.RefractiveIndex(HaloSettings.GetWavelengthNanometers(index))).ToArray();
        var total = new double[width * channels];
        Parallel.For(
            0,
            inclinations,
            () => new double[width * channels],
            [MethodImpl(MethodImplOptions.AggressiveOptimization)] (row, _, histogram) =>
            {
                var inclination = (row + 0.5) * inclinationStep;
                Span<PrismPath> paths = stackalloc PrismPath[PrismGeometry.MaximumPathCount];
                for (var column = 0; column < azimuths; column++)
                {
                    var azimuth = (column + 0.5) * azimuthStep;
                    var orientation = Math.Cos(inclination) * inclinationStep * azimuthStep * 6.0 / Math.PI;
                    var incoming = new Vec3(-Math.Cos(inclination) * Math.Cos(azimuth), -Math.Cos(inclination) * Math.Sin(azimuth), -Math.Sin(inclination));
                    var count = PrismGeometry.Enumerate(incoming, length, referenceIndex, PathKinds.SideToSide | PathKinds.SideToEnd | PathKinds.EndToSide, paths);
                    for (var path = 0; path < count; path++)
                    {
                        for (var wavelength = 0; wavelength < weights.Length; wavelength++)
                        {
                            if (!PrismGeometry.TryRefract(incoming, paths[path], indices[wavelength], out var exit, out var transmittance))
                                continue;

                            var angle = Math.Acos(Math.Clamp(exit.Dot(incoming), -1.0, 1.0)) / HaloSettings.RadiansPerDegree;
                            var position = (angle - HaloSettings.RingStartDegrees) / step;
                            if (position < 0.0 || position >= width - 1)
                                continue;

                            var lower = (int)Math.Floor(position);
                            var fraction = position - lower;
                            var weight = orientation * paths[path].Geometry * transmittance;
                            AddWeight(histogram, lower, (1.0 - fraction) * weight, weights[wavelength]);
                            AddWeight(histogram, lower + 1, fraction * weight, weights[wavelength]);
                        }
                    }
                }

                return histogram;
            },
            histogram =>
            {
                lock (total)
                {
                    for (var index = 0; index < total.Length; index++)
                        total[index] += histogram[index];
                }
            });

        var stepRadians = step * HaloSettings.RadiansPerDegree;
        for (var index = 0; index < width; index++)
        {
            var angle = HaloSettings.GetRingAngleDegrees(index) * HaloSettings.RadiansPerDegree;
            var scale = 1.0 / (2.0 * Math.PI * Math.Sin(angle) * stepRadians);
            for (var channel = 0; channel < channels; channel++)
                total[index * channels + channel] *= scale;
        }

        return total;
    }

    private static void AddWeight(double[] histogram, int index, double weight, Tristimulus spectrum)
    {
        var offset = index * HaloSettings.ColorChannels;
        histogram[offset] += weight * spectrum.X;
        histogram[offset + 1] += weight * spectrum.Y;
        histogram[offset + 2] += weight * spectrum.Z;
    }
}
