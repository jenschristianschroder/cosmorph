namespace Cosmorph.Domain.Random;

/// <summary>
/// Stateless, seeded random source. Values depend only on the supplied coordinates, so evaluation
/// order never changes a result and a tick can be replayed exactly.
/// </summary>
public static class DeterministicRandom
{
    /// <summary>Mixes a seed and up to four coordinates into a uniformly distributed 64-bit value.</summary>
    public static ulong Hash(ulong seed, long a, long b = 0, long c = 0, long d = 0)
    {
        unchecked
        {
            var h = seed + 0x9E3779B97F4A7C15UL;
            h = Mix(h ^ (ulong)a);
            h = Mix(h ^ (ulong)b);
            h = Mix(h ^ (ulong)c);
            h = Mix(h ^ (ulong)d);
            return h;
        }
    }

    /// <summary>Returns a value in [0, bound).</summary>
    public static int Next(ulong seed, int bound, long a, long b = 0, long c = 0, long d = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
        return (int)(Hash(seed, a, b, c, d) % (ulong)bound);
    }

    /// <summary>Returns a value in [0, 1000].</summary>
    public static int NextPermille(ulong seed, long a, long b = 0, long c = 0, long d = 0) =>
        Next(seed, 1001, a, b, c, d);

    /// <summary>Returns a value in [-amplitude, amplitude].</summary>
    public static int NextSigned(ulong seed, int amplitude, long a, long b = 0, long c = 0, long d = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amplitude);
        return amplitude == 0 ? 0 : Next(seed, (2 * amplitude) + 1, a, b, c, d) - amplitude;
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }
    }
}
