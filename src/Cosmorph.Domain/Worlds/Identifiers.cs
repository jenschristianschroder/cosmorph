using System.Text.RegularExpressions;

namespace Cosmorph.Domain.Worlds;

/// <summary>Opaque, validated world identifier. Never derived from user text without validation.</summary>
public readonly partial record struct WorldId
{
    public const int MaxLength = 40;

    private WorldId(string value) => Value = value;

    public string Value { get; }

    public static WorldId Parse(string? value) =>
        TryParse(value, out var id) ? id : throw new ArgumentException("Invalid world identifier.", nameof(value));

    public static bool TryParse(string? value, out WorldId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        if (!IdentifierPattern().IsMatch(value))
        {
            return false;
        }

        id = new WorldId(value);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,39}$")]
    private static partial Regex IdentifierPattern();
}

/// <summary>Identifier of a Warden within a single world.</summary>
public readonly partial record struct WardenId
{
    private WardenId(string value) => Value = value;

    public string Value { get; }

    public static WardenId Parse(string? value) =>
        TryParse(value, out var id) ? id : throw new ArgumentException("Invalid warden identifier.", nameof(value));

    public static bool TryParse(string? value, out WardenId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value) || value.Length > 32 || !IdentifierPattern().IsMatch(value))
        {
            return false;
        }

        id = new WardenId(value);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,31}$")]
    private static partial Regex IdentifierPattern();
}

/// <summary>Identifier of a species defined by a versioned content pack.</summary>
public readonly record struct SpeciesId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Deterministic world seed. All randomness is derived from it.</summary>
public readonly record struct WorldSeed(ulong Value);

/// <summary>Monotonic logical tick counter.</summary>
public readonly record struct TickNumber(long Value)
{
    public TickNumber Next() => new(checked(Value + 1));

    public override string ToString() => Value.ToString();
}

/// <summary>Season identifier of the content and rules release.</summary>
public readonly record struct SeasonId(int Value);

/// <summary>Chapter identifier: a versioned narrative and snapshot boundary within a season.</summary>
public readonly record struct ChapterId(int Value)
{
    public ChapterId Next() => new(checked(Value + 1));
}

/// <summary>Logical world time derived from the tick number and the world's cadence.</summary>
public readonly record struct WorldInstant(long Day, int TickOfDay)
{
    public static WorldInstant FromTick(TickNumber tick, int ticksPerDay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerDay, 1);
        return new WorldInstant(Math.DivRem(tick.Value, ticksPerDay, out var rest), (int)rest);
    }

    public int DayOfYear(int daysPerYear) => daysPerYear <= 0 ? 0 : (int)(Day % daysPerYear);
}
