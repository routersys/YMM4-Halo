namespace Halo;

internal static class HaloTable
{
    private const double RedLuminance = 0.2126;
    private const double GreenLuminance = 0.7152;
    private const double BlueLuminance = 0.0722;
    private const int MaximumBlurTexels = 70;
    private const double OpaqueAlpha = 1.0;

    private static readonly Lazy<double> Scale = new(BuildScale);

    public static Half[] ComposeRing(double sunDiameterDegrees, double share, double saturation)
    {
        var colors = RingColors(sunDiameterDegrees);
        var width = HaloSettings.RingTableWidth;
        var texels = new Half[width * HaloSettings.TexelChannels];
        var strength = Math.Clamp(share, 0.0, 1.0) * Scale.Value;
        for (var index = 0; index < width; index++)
        {
            var angle = HaloSettings.GetRingAngleDegrees(index);
            var taper = 1.0 - SmoothStep(HaloSettings.RingEndDegrees - HaloSettings.RingOuterTaperDegrees, HaloSettings.RingEndDegrees, angle);
            Write(texels, index, colors[index * HaloSettings.ColorChannels] * strength * taper, colors[index * HaloSettings.ColorChannels + 1] * strength * taper, colors[index * HaloSettings.ColorChannels + 2] * strength * taper, saturation);
        }

        return texels;
    }

    public static Half[] ComposeScatter(float[] plate, float[] column, double plateShare, double columnShare, double sunElevationDegrees, double sunDiameterDegrees, double saturation)
    {
        var width = ScatterGrid.Width;
        var height = ScatterGrid.Height;
        var combined = new double[plate.Length];
        var plateWeight = Math.Clamp(plateShare, 0.0, 1.0);
        var columnWeight = Math.Clamp(columnShare, 0.0, 1.0);
        for (var index = 0; index < combined.Length; index++)
            combined[index] = plateWeight * plate[index] + columnWeight * column[index];

        var blurred = BlurScatter(combined, sunElevationDegrees, HaloSettings.GetSunRadiusDegrees(sunDiameterDegrees));
        var (whiteRed, whiteGreen, whiteBlue) = SolarSpectrum.ToLinearSrgb(SolarSpectrum.White);
        var texels = new Half[width * height * HaloSettings.TexelChannels];
        var scale = Scale.Value;
        Parallel.For(0, height, row =>
        {
            var offset = HaloSettings.GetScatterElevationDegrees(row, sunElevationDegrees) - sunElevationDegrees;
            var taper = SmoothStep(-HaloSettings.ScatterElevationHalfSpanDegrees, -HaloSettings.ScatterElevationHalfSpanDegrees + HaloSettings.ScatterElevationTaperDegrees, offset)
                * (1.0 - SmoothStep(HaloSettings.ScatterElevationHalfSpanDegrees - HaloSettings.ScatterElevationTaperDegrees, HaloSettings.ScatterElevationHalfSpanDegrees, offset));
            for (var column0 = 0; column0 < width; column0++)
            {
                var source = (row * width + column0) * HaloSettings.ColorChannels;
                var (red, green, blue) = SolarSpectrum.ToLinearSrgb(new Tristimulus(blurred[source], blurred[source + 1], blurred[source + 2]));
                Write(texels, row * width + column0, red / whiteRed * scale * taper, green / whiteGreen * scale * taper, blue / whiteBlue * scale * taper, saturation);
            }
        });

        return texels;
    }

    private static double[] RingColors(double sunDiameterDegrees)
        => RingColors(sunDiameterDegrees, RingProfile.Radiance);

    public static double[] RingColors(double sunDiameterDegrees, double[] profile)
    {
        var width = HaloSettings.RingTableWidth;
        var channels = HaloSettings.ColorChannels;
        var kernel = SunKernel(sunDiameterDegrees);
        var half = kernel.Length / 2;
        var (whiteRed, whiteGreen, whiteBlue) = SolarSpectrum.ToLinearSrgb(SolarSpectrum.White);
        var colors = new double[width * channels];
        for (var index = 0; index < width; index++)
        {
            var x = 0.0;
            var y = 0.0;
            var z = 0.0;
            for (var tap = -half; tap <= half; tap++)
            {
                var source = Math.Clamp(index + tap, 0, width - 1) * channels;
                x += kernel[tap + half] * profile[source];
                y += kernel[tap + half] * profile[source + 1];
                z += kernel[tap + half] * profile[source + 2];
            }

            var (red, green, blue) = SolarSpectrum.ToLinearSrgb(new Tristimulus(x, y, z));
            colors[index * channels] = red / whiteRed;
            colors[index * channels + 1] = green / whiteGreen;
            colors[index * channels + 2] = blue / whiteBlue;
        }

        return colors;
    }

    private static double BuildScale()
    {
        var colors = RingColors(HaloSettings.ReferenceSunDiameterDegrees);
        var peak = 0.0;
        foreach (var value in colors)
            peak = Math.Max(peak, value);
        return peak > 0.0 ? HaloSettings.RingPeakBrightness / peak : 0.0;
    }

    public static double[] SunKernel(double sunDiameterDegrees)
    {
        var radius = HaloSettings.GetSunRadiusDegrees(sunDiameterDegrees);
        var step = HaloSettings.GetRingStepDegrees();
        var half = (int)Math.Floor(radius / step);
        if (half == 0)
            return [1.0];

        var kernel = new double[2 * half + 1];
        var total = 0.0;
        for (var offset = -half; offset <= half; offset++)
        {
            var distance = offset * step / radius;
            var weight = Math.Sqrt(Math.Max(1.0 - distance * distance, 0.0));
            kernel[offset + half] = weight;
            total += weight;
        }

        for (var index = 0; index < kernel.Length; index++)
            kernel[index] /= total;
        return kernel;
    }

    private static double[] BlurScatter(double[] values, double sunElevationDegrees, double radiusDegrees)
    {
        var width = ScatterGrid.Width;
        var height = ScatterGrid.Height;
        var channels = HaloSettings.ColorChannels;
        var texel = HaloSettings.ScatterTexelDegrees;
        var extent = width + 2 * MaximumBlurTexels;
        var prefix = new double[height * channels * (extent + 1)];
        Parallel.For(0, height, row =>
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var start = (row * channels + channel) * (extent + 1);
                for (var index = 0; index < extent; index++)
                {
                    var column = ReflectColumn(index - MaximumBlurTexels, width);
                    prefix[start + index + 1] = prefix[start + index] + values[(row * width + column) * channels + channel];
                }
            }
        });

        var verticalRadius = (int)Math.Floor(radiusDegrees / texel);
        var result = new double[values.Length];
        Parallel.For(0, height, row =>
        {
            var elevation = Math.Min(Math.Abs(HaloSettings.GetScatterElevationDegrees(row, sunElevationDegrees)), 90.0) * HaloSettings.RadiansPerDegree;
            var horizontalScale = 1.0 / (texel * Math.Max(Math.Cos(elevation), HaloSettings.ScatterMinimumCosine));
            var halfWidths = new double[2 * verticalRadius + 1];
            var area = 0.0;
            for (var offset = -verticalRadius; offset <= verticalRadius; offset++)
            {
                var vertical = offset * texel;
                var halfWidth = Math.Min(Math.Sqrt(Math.Max(radiusDegrees * radiusDegrees - vertical * vertical, 0.0)) * horizontalScale, MaximumBlurTexels - 1.0);
                halfWidths[offset + verticalRadius] = halfWidth;
                area += 2.0 * halfWidth;
            }

            if (area <= 0.0)
            {
                Array.Copy(values, row * width * channels, result, row * width * channels, width * channels);
                return;
            }

            for (var column = 0; column < width; column++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var sum = 0.0;
                    for (var offset = -verticalRadius; offset <= verticalRadius; offset++)
                    {
                        var source = Math.Clamp(row + offset, 0, height - 1);
                        var halfWidth = halfWidths[offset + verticalRadius];
                        var start = (source * channels + channel) * (extent + 1);
                        sum += Accumulate(prefix, start, MaximumBlurTexels + column + 0.5 + halfWidth) - Accumulate(prefix, start, MaximumBlurTexels + column + 0.5 - halfWidth);
                    }

                    result[(row * width + column) * channels + channel] = sum / area;
                }
            }
        });

        return result;
    }

    private static double Accumulate(double[] prefix, int start, double position)
    {
        var index = (int)Math.Floor(position);
        return prefix[start + index] + (position - index) * (prefix[start + index + 1] - prefix[start + index]);
    }

    private static int ReflectColumn(int column, int width)
    {
        if (column < 0)
            return Math.Min(-column - 1, width - 1);
        return column >= width ? Math.Max(2 * width - 1 - column, 0) : column;
    }

    private static void Write(Half[] texels, int index, double red, double green, double blue, double saturation)
    {
        var chroma = Math.Clamp(saturation, 0.0, HaloSettings.MaximumSaturation);
        var luminance = RedLuminance * red + GreenLuminance * green + BlueLuminance * blue;
        var target = index * HaloSettings.TexelChannels;
        texels[target] = (Half)Math.Max(luminance + chroma * (red - luminance), 0.0);
        texels[target + 1] = (Half)Math.Max(luminance + chroma * (green - luminance), 0.0);
        texels[target + 2] = (Half)Math.Max(luminance + chroma * (blue - luminance), 0.0);
        texels[target + 3] = (Half)OpaqueAlpha;
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
