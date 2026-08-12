using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>
/// Raw materials held by one cell. Stocks are plain integers, accrue a little each tick from the
/// cell's own biome and climate, and are spent by Wardens on constructions.
/// </summary>
/// <remarks>
/// Yield is derived from the cell rather than declared in the content pack, so a world stored before
/// materials existed starts at zero and refills on its next tick without a content-version change.
/// </remarks>
public readonly record struct CellResources(int Timber, int Stone, int Fibre)
{
    /// <summary>Ceiling per material. A cell cannot hoard beyond this, so stock is always bounded.</summary>
    public const int MaxStock = 1000;

    public static CellResources None { get; }

    public int Total => Timber + Stone + Fibre;

    /// <summary>How well stocked the cell is, in permille of its ceiling. Used by the spectator overlay.</summary>
    public Permille Richness => Permille.Clamp(Total * 1000 / (MaxStock * 3));

    public CellResources Add(CellResources amount) => new(
        Math.Clamp(Timber + amount.Timber, 0, MaxStock),
        Math.Clamp(Stone + amount.Stone, 0, MaxStock),
        Math.Clamp(Fibre + amount.Fibre, 0, MaxStock));

    public bool Covers(CellResources cost) =>
        Timber >= cost.Timber && Stone >= cost.Stone && Fibre >= cost.Fibre;

    /// <summary>Deducts a cost when the stock covers it in full. Partial spending never happens.</summary>
    public bool TrySpend(CellResources cost, out CellResources remaining)
    {
        if (!Covers(cost))
        {
            remaining = this;
            return false;
        }

        remaining = new CellResources(Timber - cost.Timber, Stone - cost.Stone, Fibre - cost.Fibre);
        return true;
    }

    /// <summary>
    /// What the cell produces in one tick. Timber and fibre are living material and track how full
    /// the cell's biomass is; stone is mineral and depends only on the terrain.
    /// </summary>
    public static CellResources YieldPerTick(PlanetCell cell)
    {
        if (!cell.IsLand || cell.CarryingCapacity <= 0)
        {
            return None;
        }

        var fill = Math.Clamp(cell.Biomass * 1000 / cell.CarryingCapacity, 0, 1000);

        var timber = cell.Biome switch
        {
            Biome.Rainforest => 8,
            Biome.TemperateForest => 7,
            Biome.BorealForest => 6,
            Biome.Wetland => 3,
            _ => 0,
        };

        var stone = cell.Biome switch
        {
            Biome.Mountain => 8,
            Biome.Desert => 5,
            Biome.Tundra => 3,
            _ => 1,
        };

        var fibre = cell.Biome switch
        {
            Biome.Grassland => 7,
            Biome.Wetland => 6,
            Biome.Rainforest => 4,
            Biome.TemperateForest => 3,
            Biome.Tundra => 2,
            _ => 1,
        };

        return new CellResources(
            timber * fill / 1000,
            stone + (cell.Elevation / 400),
            fibre * fill / 1000);
    }
}
