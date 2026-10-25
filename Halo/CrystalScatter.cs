namespace Halo;

internal enum CrystalFamily
{
    Plate,
    Column,
}

internal static class CrystalScatter
{
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
                        ScatterSplat.SplatStrip(workspace.Grid, workspace.Slices[(strip - 1) & 1], current, quadWeight);
                }

                return workspace;
            },
            workspace => grids.Add(workspace.Grid));
        return ToRadiance(ScatterGrid.Sum([.. grids]), sunElevationDegrees);
    }

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
