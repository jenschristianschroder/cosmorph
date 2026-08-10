using Cosmorph.Domain.Grid;

namespace Cosmorph.Domain.Tests;

public sealed class GridTests
{
    [Fact]
    public void LongitudeWrapsAroundTheSphere()
    {
        var grid = GridCache.Get(32, 16);
        var westEdge = grid.IndexOf(0, 8);
        var eastEdge = grid.IndexOf(31, 8);

        Assert.Contains(eastEdge, grid.NeighboursOf(westEdge));
        Assert.Contains(westEdge, grid.NeighboursOf(eastEdge));
    }

    [Fact]
    public void PolesDoNotWrapVertically()
    {
        var grid = GridCache.Get(16, 8);
        var north = grid.IndexOf(3, 0);
        var south = grid.IndexOf(3, 7);

        Assert.DoesNotContain(south, grid.NeighboursOf(north));
        Assert.All(grid.NeighboursOf(north), n => Assert.InRange(n, 0, grid.CellCount - 1));
    }

    [Fact]
    public void CoordinatesRoundTrip()
    {
        var grid = GridCache.Get(24, 12);
        for (var index = 0; index < grid.CellCount; index++)
        {
            var (x, y) = grid.CoordinatesOf(index);
            Assert.Equal(index, grid.IndexOf(x, y));
            Assert.InRange(grid.LatitudeDegrees(index), -90, 90);
            Assert.InRange(grid.LongitudeDegrees(index), -180, 180);
        }
    }

    [Fact]
    public void NeighbourOrderIsDeterministic()
    {
        var first = GridCache.Get(20, 10).NeighboursOf(57);
        var second = GridCache.Get(20, 10).NeighboursOf(57);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(16, 1)]
    [InlineData(4096, 8)]
    public void UnsupportedGridSizesAreRejected(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GridCache.Get(width, height));
}
