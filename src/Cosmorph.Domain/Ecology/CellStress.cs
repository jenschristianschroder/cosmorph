using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Ecology;

/// <summary>Typed stress indicators of a cell, each in permille.</summary>
public readonly record struct CellStress(Permille Drought, Permille Disease, Permille Fire, Permille Flood)
{
    public static CellStress None { get; } = new(Permille.Zero, Permille.Zero, Permille.Zero, Permille.Zero);

    public StressKind Dominant
    {
        get
        {
            var best = StressKind.None;
            var bestValue = 100; // below this a cell is considered healthy
            if (Drought.Value > bestValue)
            {
                (best, bestValue) = (StressKind.Drought, Drought.Value);
            }

            if (Disease.Value > bestValue)
            {
                (best, bestValue) = (StressKind.Disease, Disease.Value);
            }

            if (Fire.Value > bestValue)
            {
                (best, bestValue) = (StressKind.Fire, Fire.Value);
            }

            if (Flood.Value > bestValue)
            {
                best = StressKind.Flood;
            }

            return best;
        }
    }

    public int Total => Drought.Value + Disease.Value + Fire.Value + Flood.Value;

    public CellStress Decay(int amount) => new(
        Drought.Add(-amount),
        Disease.Add(-amount),
        Fire.Add(-amount),
        Flood.Add(-amount));
}

public enum StressKind
{
    None = 0,
    Drought = 1,
    Disease = 2,
    Fire = 3,
    Flood = 4,
}
