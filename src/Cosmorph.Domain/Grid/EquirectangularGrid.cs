namespace Cosmorph.Domain.Grid;

/// <summary>
/// Versioned equirectangular grid. Longitude wraps; latitude does not.
/// </summary>
public sealed class EquirectangularGrid : ICellTopology
{
    public const int TopologyVersion = 1;

    private readonly int[][] _neighbours;

    public EquirectangularGrid(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 512);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 512);

        Width = width;
        Height = height;
        _neighbours = new int[width * height][];
        for (var index = 0; index < _neighbours.Length; index++)
        {
            _neighbours[index] = BuildNeighbours(index);
        }
    }

    public static EquirectangularGrid Default { get; } = new(64, 32);

    public int Version => TopologyVersion;

    public int Width { get; }

    public int Height { get; }

    public int CellCount => Width * Height;

    public int IndexOf(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        var wrapped = ((x % Width) + Width) % Width;
        return (y * Width) + wrapped;
    }

    public (int X, int Y) CoordinatesOf(int index)
    {
        ValidateIndex(index);
        return (index % Width, index / Width);
    }

    public int LatitudeDegrees(int index)
    {
        var (_, y) = CoordinatesOf(index);
        return 90 - (((y * 2) + 1) * 180 / (Height * 2));
    }

    public int LongitudeDegrees(int index)
    {
        var (x, _) = CoordinatesOf(index);
        return (((x * 2) + 1) * 360 / (Width * 2)) - 180;
    }

    public IReadOnlyList<int> NeighboursOf(int index)
    {
        ValidateIndex(index);
        return _neighbours[index];
    }

    private int[] BuildNeighbours(int index)
    {
        var (x, y) = (index % Width, index / Width);
        var list = new List<int>(4)
        {
            IndexOf(x - 1, y),
            IndexOf(x + 1, y),
        };

        if (y > 0)
        {
            list.Add(IndexOf(x, y - 1));
        }

        if (y < Height - 1)
        {
            list.Add(IndexOf(x, y + 1));
        }

        list.Sort();
        return [.. list];
    }

    private void ValidateIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, CellCount);
    }
}
