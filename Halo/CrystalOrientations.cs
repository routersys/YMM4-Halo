namespace Halo;

internal readonly record struct TiltLayer(double Tilt, double Weight);

internal readonly record struct StripChunk(TiltLayer Layer, int FirstStrip, int StripCount);

internal static class CrystalOrientations
{
    public static StripChunk[] BuildChunks(TiltLayer[] layers, int azimuthSteps)
    {
        var chunks = new List<StripChunk>();
        foreach (var layer in layers)
        {
            for (var strip = 0; strip < azimuthSteps; strip += HaloSettings.StripsPerTask)
                chunks.Add(new StripChunk(layer, strip, Math.Min(HaloSettings.StripsPerTask, azimuthSteps - strip)));
        }

        return [.. chunks];
    }

    public static TiltLayer[] PlateLayers(double tiltDegrees)
    {
        var nodes = HaloSettings.PlateTiltRadialNodes;
        var tilt = tiltDegrees * HaloSettings.RadiansPerDegree;
        var layers = new TiltLayer[nodes];
        for (var node = 0; node < nodes; node++)
            layers[node] = new TiltLayer(tilt * HaloSettings.GetPlateTiltQuantile(node), 1.0 / nodes);
        return layers;
    }

    public static TiltLayer[] ColumnLayers(double tiltDegrees)
    {
        var quantiles = HaloSettings.ColumnTiltQuantiles;
        var tilt = tiltDegrees * HaloSettings.RadiansPerDegree;
        return [.. quantiles.Select(quantile => new TiltLayer(tilt * quantile, 1.0 / quantiles.Length))];
    }

    public static Vec3 PlateAxis(double tilt, double azimuth)
        => new(Math.Sin(tilt) * Math.Cos(azimuth), Math.Sin(tilt) * Math.Sin(azimuth), Math.Cos(tilt));

    public static Vec3 ColumnAxis(double tilt, double azimuth)
        => new(Math.Cos(tilt) * Math.Cos(azimuth), Math.Cos(tilt) * Math.Sin(azimuth), Math.Sin(tilt));
}
