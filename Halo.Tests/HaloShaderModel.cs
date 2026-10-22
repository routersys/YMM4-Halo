using System.Numerics;

namespace Halo.Tests;

internal static class HaloShaderModel
{
    const float DegreesPerRadian = 57.29577951308232f;
    const float TwoPi = 6.283185307179586f;

    static float ToLinear(float encoded)
        => encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    static float ToEncoded(float linear)
        => linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

    static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

    static float Mix(float first, float second, float fraction) => first + (second - first) * fraction;

    public static (float Red, float Green, float Blue) Sample(Half[] texels, int width, int height, float u, float v)
    {
        var x = Math.Clamp(u * width - 0.5f, 0f, width - 1f);
        var y = Math.Clamp(v * height - 0.5f, 0f, height - 1f);
        var column = Math.Min((int)MathF.Floor(x), Math.Max(width - 2, 0));
        var row = Math.Min((int)MathF.Floor(y), Math.Max(height - 2, 0));
        var nextColumn = Math.Min(column + 1, width - 1);
        var nextRow = Math.Min(row + 1, height - 1);
        var fractionX = x - column;
        var fractionY = y - row;
        float Channel(int channel)
        {
            var low = Mix((float)texels[(row * width + column) * HaloSettings.TexelChannels + channel], (float)texels[(row * width + nextColumn) * HaloSettings.TexelChannels + channel], fractionX);
            var high = Mix((float)texels[(nextRow * width + column) * HaloSettings.TexelChannels + channel], (float)texels[(nextRow * width + nextColumn) * HaloSettings.TexelChannels + channel], fractionX);
            return Mix(low, high, fractionY);
        }

        return (Channel(0), Channel(1), Channel(2));
    }

    public static Bgra Shade(Bgra source, int x, int y, int width, int height, Vector3 sun, float halfFieldTangent, float pitchCosine, float pitchSine, float sunAzimuth, float sunElevation, Half[] ring, Half[] scatter, float amount)
    {
        if (amount <= 0f || source.Alpha == 0)
            return source;

        var focal = 0.5f * width / halfFieldTangent;
        var direction = Vector3.Normalize(new Vector3(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f, focal));
        var red = 0f;
        var green = 0f;
        var blue = 0f;

        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(direction, sun), -1f, 1f)) * DegreesPerRadian;
        var ringPosition = (angle - (float)HaloSettings.RingStartDegrees) / (float)(HaloSettings.RingEndDegrees - HaloSettings.RingStartDegrees);
        if (ringPosition >= 0f && ringPosition <= 1f)
        {
            var (ringRed, ringGreen, ringBlue) = Sample(ring, HaloSettings.RingTableWidth, 1, (ringPosition * (HaloSettings.RingTableWidth - 1f) + 0.5f) / HaloSettings.RingTableWidth, 0.5f);
            red += ringRed;
            green += ringGreen;
            blue += ringBlue;
        }

        var worldDown = direction.Y * pitchCosine - direction.Z * pitchSine;
        var worldForward = direction.Y * pitchSine + direction.Z * pitchCosine;
        var elevation = MathF.Asin(Math.Clamp(-worldDown, -1f, 1f)) * DegreesPerRadian;
        var turn = MathF.Atan2(direction.X, worldForward) - sunAzimuth;
        turn -= TwoPi * MathF.Round(turn / TwoPi, MidpointRounding.ToEven);
        var scatterU = MathF.Abs(turn) * DegreesPerRadian / ((float)HaloSettings.ScatterTexelDegrees * ScatterGrid.Width);
        var scatterV = (elevation - sunElevation + (float)HaloSettings.ScatterElevationHalfSpanDegrees) / ((float)HaloSettings.ScatterTexelDegrees * ScatterGrid.Height);
        if (scatterU <= 1f && scatterV >= 0f && scatterV <= 1f)
        {
            var (scatterRed, scatterGreen, scatterBlue) = Sample(scatter, ScatterGrid.Width, ScatterGrid.Height, scatterU, scatterV);
            red += scatterRed;
            green += scatterGreen;
            blue += scatterBlue;
        }

        var alpha = source.Alpha / 255f;
        byte Channel(byte premultiplied, float light)
        {
            var straight = Saturate(premultiplied / 255f / alpha);
            var linear = Saturate(ToLinear(straight) + (1f - MathF.Exp(-light)) * amount);
            return (byte)Math.Clamp((int)MathF.Round(ToEncoded(linear) * alpha * 255f), 0, 255);
        }

        return new Bgra(Channel(source.Blue, blue), Channel(source.Green, green), Channel(source.Red, red), source.Alpha);
    }
}
