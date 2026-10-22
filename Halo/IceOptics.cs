using System.Runtime.CompilerServices;

namespace Halo;

internal static class IceOptics
{
    private const double TableStartNanometers = 400.0;
    private const double TableStepNanometers = 10.0;
    public const double AirRefractiveIndex = 1.0;

    private static readonly double[] RefractiveIndices =
    [
        1.3194, 1.3185, 1.3177, 1.3170, 1.3163, 1.3157, 1.3151, 1.3145,
        1.3140, 1.3135, 1.3130, 1.3126, 1.3121, 1.3117, 1.3114, 1.3110,
        1.3106, 1.3103, 1.3100, 1.3097, 1.3094, 1.3091, 1.3088, 1.3085,
        1.3083, 1.3080, 1.3078, 1.3076, 1.3073, 1.3071, 1.3069, 1.3067,
        1.3065, 1.3062, 1.3060, 1.3059, 1.3057, 1.3055, 1.3053, 1.3051,
        1.3049,
    ];

    public static double RefractiveIndex(double wavelengthNanometers)
    {
        var position = Math.Clamp((wavelengthNanometers - TableStartNanometers) / TableStepNanometers, 0.0, RefractiveIndices.Length - 1.0);
        var lower = Math.Min((int)Math.Floor(position), RefractiveIndices.Length - 2);
        var fraction = position - lower;
        return RefractiveIndices[lower] + (RefractiveIndices[lower + 1] - RefractiveIndices[lower]) * fraction;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Reflectance(double cosineIncidence, double incidentIndex, double transmittedIndex)
    {
        var cosine = Math.Clamp(cosineIncidence, 0.0, 1.0);
        var sineTransmitted = incidentIndex / transmittedIndex * Math.Sqrt(1.0 - cosine * cosine);
        if (sineTransmitted >= 1.0)
            return 1.0;

        var cosineTransmitted = Math.Sqrt(1.0 - sineTransmitted * sineTransmitted);
        var perpendicular = Square((incidentIndex * cosine - transmittedIndex * cosineTransmitted) / (incidentIndex * cosine + transmittedIndex * cosineTransmitted));
        var parallel = Square((incidentIndex * cosineTransmitted - transmittedIndex * cosine) / (incidentIndex * cosineTransmitted + transmittedIndex * cosine));
        return 0.5 * (perpendicular + parallel);
    }

    private static double Square(double value)
        => value * value;
}
