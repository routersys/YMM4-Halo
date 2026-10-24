namespace Halo.Tests;

public sealed class ScatterGridTests
{
    const int Channels = HaloSettings.ColorChannels;

    static readonly Tristimulus Unit = new(1.0, 1.0, 1.0);

    static double RowSum(ScatterGrid grid, int row)
        => Enumerable.Range(0, ScatterGrid.Width).Sum(column => grid.Cells[(row * ScatterGrid.Width + column) * Channels] / ScatterGrid.FixedScale);

    static double ColumnSum(ScatterGrid grid, int column)
        => Enumerable.Range(0, ScatterGrid.Height).Sum(row => grid.Cells[(row * ScatterGrid.Width + column) * Channels] / ScatterGrid.FixedScale);

    static double Total(ScatterGrid grid)
        => Enumerable.Range(0, ScatterGrid.Height).Sum(row => RowSum(grid, row));

    [Fact]
    public void ALightKeepsItsWholePowerInsideTheTable()
    {
        var grid = new ScatterGrid();

        grid.Splat(33.3, 5.1, Unit);

        Assert.Equal(1.0, Total(grid), 1e-6);
    }

    [Fact]
    public void TheSignOfTheAzimuthDoesNotMatter()
    {
        var positive = new ScatterGrid();
        var negative = new ScatterGrid();

        positive.Splat(33.3, 5.1, Unit);
        negative.Splat(-33.3, 5.1, Unit);

        Assert.True(Total(positive) > 0.0);
        Assert.True(positive.Cells.AsSpan().SequenceEqual(negative.Cells));
    }

    [Fact]
    public void ALightBetweenTheBottomEdgeAndTheCentreOfTheFirstRowFallsIntoTheFirstRow()
    {
        var grid = new ScatterGrid();

        grid.Splat(33.3, -HaloSettings.ScatterElevationHalfSpanDegrees, Unit);

        Assert.Equal(0.5, RowSum(grid, 0), 1e-6);
        Assert.Equal(0.5, Total(grid), 1e-6);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void ALightBeyondTheElevationRangeIsDropped(double margin)
    {
        var grid = new ScatterGrid();

        grid.Splat(33.3, Math.CopySign(HaloSettings.ScatterElevationHalfSpanDegrees + 1.0, margin), Unit);

        Assert.Equal(0.0, Total(grid));
    }

    [Fact]
    public void TheSumOfGridsAddsEveryCellOfEveryGrid()
    {
        var first = new ScatterGrid();
        var second = new ScatterGrid();
        for (var row = 0; row < ScatterGrid.Height; row++)
        {
            for (var column = 0; column < ScatterGrid.Width; column++)
            {
                var azimuth = (column + 0.5) * HaloSettings.ScatterTexelDegrees;
                var elevation = -HaloSettings.ScatterElevationHalfSpanDegrees + (row + 0.5) * HaloSettings.ScatterTexelDegrees;
                first.Splat(azimuth, elevation, Unit);
                second.Splat(azimuth + 0.1, elevation + 0.1, new Tristimulus(2.0, 0.5, 3.0));
            }
        }

        var total = ScatterGrid.Sum([first, second, new ScatterGrid()]);

        var expected = first.Cells.Zip(second.Cells, (left, right) => left + right).ToArray();
        Assert.Contains(expected, cell => cell != 0);
        Assert.True(total.Cells.AsSpan().SequenceEqual(expected));
    }

    [Fact]
    public void AnAzimuthPastHalfATurnFoldsBackIntoTheTable()
    {
        var grid = new ScatterGrid();

        grid.Splat(181.0, 0.0, Unit);

        Assert.Equal(1.0, ColumnSum(grid, ScatterGrid.Width - 4) + ColumnSum(grid, ScatterGrid.Width - 3), 1e-6);
    }
}
