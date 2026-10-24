using System.Runtime.CompilerServices;

namespace Halo;

internal sealed class ScatterGrid
{
    public const int Width = HaloSettings.ScatterWidth;
    public const int Height = HaloSettings.ScatterHeight;
    public const double FixedScale = 1073741824.0;

    private const double TexelsPerDegree = 1.0 / HaloSettings.ScatterTexelDegrees;
    private const int Channels = HaloSettings.ColorChannels;
    private const int SumBlock = 8192;

    public ScatterGrid()
    {
        Cells = new long[Width * Height * Channels];
    }

    public long[] Cells { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void Splat(double azimuthDegrees, double elevationOffsetDegrees, Tristimulus weight)
    {
        var x = Math.Abs(azimuthDegrees) * TexelsPerDegree - 0.5;
        var y = (elevationOffsetDegrees + HaloSettings.ScatterElevationHalfSpanDegrees) * TexelsPerDegree - 0.5;
        var column = (int)Math.Floor(x);
        var row = (int)Math.Floor(y);
        if (row < -1 || row >= Height)
            return;

        var fractionX = x - column;
        var fractionY = y - row;
        var left = Reflect(column) * Channels;
        var right = Reflect(column + 1) * Channels;
        var wx = weight.X * FixedScale;
        var wy = weight.Y * FixedScale;
        var wz = weight.Z * FixedScale;
        fixed (long* cells = Cells)
        {
            if (row >= 0)
                Add(cells + row * Width * Channels, left, right, (1.0 - fractionY) * (1.0 - fractionX), (1.0 - fractionY) * fractionX, wx, wy, wz);
            if (row + 1 < Height)
                Add(cells + (row + 1) * Width * Channels, left, right, fractionY * (1.0 - fractionX), fractionY * fractionX, wx, wy, wz);
        }
    }

    public static ScatterGrid Sum(IReadOnlyList<ScatterGrid> grids)
    {
        var total = new ScatterGrid();
        var cells = total.Cells;
        Parallel.For(0, (cells.Length + SumBlock - 1) / SumBlock, block =>
        {
            var end = Math.Min((block + 1) * SumBlock, cells.Length);
            foreach (var grid in grids)
            {
                var other = grid.Cells;
                for (var index = block * SumBlock; index < end; index++)
                    cells[index] += other[index];
            }
        });
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Add(long* line, int left, int right, double leftScale, double rightScale, double wx, double wy, double wz)
    {
        line[left] += (long)(leftScale * wx + 0.5);
        line[left + 1] += (long)(leftScale * wy + 0.5);
        line[left + 2] += (long)(leftScale * wz + 0.5);
        line[right] += (long)(rightScale * wx + 0.5);
        line[right + 1] += (long)(rightScale * wy + 0.5);
        line[right + 2] += (long)(rightScale * wz + 0.5);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Reflect(int column)
    {
        if (column < 0)
            return -column - 1;
        return column >= Width ? 2 * Width - 1 - column : column;
    }
}
