using System.Collections.Concurrent;

namespace Cosmorph.Domain.Grid;

/// <summary>Caches immutable grid topologies so the tick pipeline never rebuilds neighbour tables.</summary>
public static class GridCache
{
    private static readonly ConcurrentDictionary<(int Width, int Height), EquirectangularGrid> Grids = new();

    public static ICellTopology Get(int width, int height) =>
        Grids.GetOrAdd((width, height), key => new EquirectangularGrid(key.Width, key.Height));
}
