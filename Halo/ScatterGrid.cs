using System.Runtime.CompilerServices;

namespace Halo;

internal sealed class ScatterGrid
{
    public const int Width = HaloSettings.ScatterWidth;
    public const int Height = HaloSettings.ScatterHeight;

    private const double TexelsPerDegree = 1.0 / HaloSettings.ScatterTexelDegrees;
    private const int Channels = HaloSettings.ColorChannels;

    public ScatterGrid()
    {
        Cells = new float[Width * Height * Channels];
    }

    public float[] Cells { get; }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void Splat(double azimuthDegrees, double elevationOffsetDegrees, Tristimulus weight)
    {
        var x = Math.Abs(azimuthDegrees) * TexelsPerDegree - 0.5;
        var y = (elevationOffsetDegrees + HaloSettings.ScatterElevationHalfSpanDegrees) * TexelsPerDegree - 0.5;
        var column = (int)Math.Floor(x);
        var row = (int)Math.Floor(y);
        if (row < -1 || row >= Height)
            return;

        var fractionX = (float)(x - column);
        var fractionY = (float)(y - row);
        var left = Reflect(column) * Channels;
        var right = Reflect(column + 1) * Channels;
        var wx = (float)weight.X;
        var wy = (float)weight.Y;
        var wz = (float)weight.Z;
        fixed (float* cells = Cells)
        {
            if (row >= 0)
                Add(cells + row * Width * Channels, left, right, (1f - fractionY) * (1f - fractionX), (1f - fractionY) * fractionX, wx, wy, wz);
            if (row + 1 < Height)
                Add(cells + (row + 1) * Width * Channels, left, right, fractionY * (1f - fractionX), fractionY * fractionX, wx, wy, wz);
        }
    }

    public void Add(ScatterGrid other)
    {
        for (var index = 0; index < Cells.Length; index++)
            Cells[index] += other.Cells[index];
    }

    private static unsafe void Add(float* line, int left, int right, float leftScale, float rightScale, float wx, float wy, float wz)
    {
        line[left] += leftScale * wx;
        line[left + 1] += leftScale * wy;
        line[left + 2] += leftScale * wz;
        line[right] += rightScale * wx;
        line[right + 1] += rightScale * wy;
        line[right + 2] += rightScale * wz;
    }

    private static int Reflect(int column)
    {
        if (column < 0)
            return -column - 1;
        return column >= Width ? 2 * Width - 1 - column : column;
    }
}
