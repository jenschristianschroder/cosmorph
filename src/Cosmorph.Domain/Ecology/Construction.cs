using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>
/// What a Warden can raise on a cell. Names are stable; the numeric values are part of the wire format.
/// </summary>
public enum ConstructionKind
{
    None = 0,

    /// <summary>Softens the strain the species living on the cell feel.</summary>
    Shelter = 1,

    /// <summary>Raises the cell's biomass carrying capacity.</summary>
    Terrace = 2,

    /// <summary>Damps drought and flood accrual on the cell.</summary>
    Windbreak = 3,
}

/// <summary>
/// A standing structure on one cell. There is at most one per cell. Condition falls every tick, so a
/// construction that is never rebuilt eventually disappears and its effect goes with it.
/// </summary>
public readonly record struct Construction(int CellIndex, ConstructionKind Kind, int Level, Permille Condition)
{
    public const int MaxLevel = 3;

    /// <summary>Condition lost per tick. At this rate a fresh construction stands for 200 ticks.</summary>
    public const int DecayPerTick = 5;

    public bool IsStanding => Kind != ConstructionKind.None && Condition.Value > 0;

    /// <summary>Materials needed to raise a construction to the given level.</summary>
    public static CellResources CostFor(ConstructionKind kind, int level)
    {
        var scale = Math.Clamp(level, 1, MaxLevel);
        return kind switch
        {
            ConstructionKind.Shelter => new CellResources(40 * scale, 10 * scale, 30 * scale),
            ConstructionKind.Terrace => new CellResources(10 * scale, 60 * scale, 20 * scale),
            ConstructionKind.Windbreak => new CellResources(30 * scale, 20 * scale, 40 * scale),
            _ => CellResources.None,
        };
    }

    public static Construction Raise(int cellIndex, ConstructionKind kind) =>
        new(cellIndex, kind, 1, Permille.Full);

    /// <summary>Adds a level and restores condition. A construction never goes past <see cref="MaxLevel"/>.</summary>
    public Construction Upgrade() => this with
    {
        Level = Math.Min(MaxLevel, Level + 1),
        Condition = Permille.Full,
    };

    public Construction Weather() => this with { Condition = Condition.Add(-DecayPerTick) };

    /// <summary>Extra carrying capacity in permille. Zero unless this is a terrace.</summary>
    public int CapacityBonusPermille => Kind == ConstructionKind.Terrace
        ? Condition.Scale(40 * Level)
        : 0;

    /// <summary>How much of the strain on a population the structure absorbs. Zero unless this is a shelter.</summary>
    public int StrainReliefPermille => Kind == ConstructionKind.Shelter
        ? Condition.Scale(20 * Level)
        : 0;

    /// <summary>How much weather stress the structure keeps off the cell, in permille of the accrual.</summary>
    public int WeatherReliefPermille => Kind == ConstructionKind.Windbreak
        ? Condition.Scale(100 * Level)
        : 0;
}
