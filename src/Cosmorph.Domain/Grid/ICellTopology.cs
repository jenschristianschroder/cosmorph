namespace Cosmorph.Domain.Grid;

/// <summary>
/// Topology of the versioned cell grid. Keeping the topology behind an interface allows a later
/// equal-area or icosphere grid without rewriting the simulation.
/// </summary>
public interface ICellTopology
{
    /// <summary>Version of the topology encoding, recorded with every world.</summary>
    int Version { get; }

    int CellCount { get; }

    int Width { get; }

    int Height { get; }

    int IndexOf(int x, int y);

    (int X, int Y) CoordinatesOf(int index);

    /// <summary>Latitude of a cell centre in degrees, -90 (south) to 90 (north).</summary>
    int LatitudeDegrees(int index);

    /// <summary>Longitude of a cell centre in degrees, -180 to 180.</summary>
    int LongitudeDegrees(int index);

    /// <summary>Neighbour indices in a stable, deterministic order.</summary>
    IReadOnlyList<int> NeighboursOf(int index);
}
