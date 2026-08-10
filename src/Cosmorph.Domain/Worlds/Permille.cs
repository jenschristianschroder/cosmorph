namespace Cosmorph.Domain.Worlds;

/// <summary>Authoritative fixed-point ratio expressed in permille (0..1000).</summary>
public readonly record struct Permille
{
    public const int Max = 1000;

    public Permille(int value)
    {
        if (value is < 0 or > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Permille must be within 0..1000.");
        }

        Value = value;
    }

    public int Value { get; }

    public static Permille Zero => new(0);

    public static Permille Full => new(Max);

    public static Permille Clamp(int value) => new(Math.Clamp(value, 0, Max));

    public Permille Add(int delta) => Clamp(Value + delta);

    /// <summary>Scales an integer quantity by this ratio using truncating integer arithmetic.</summary>
    public int Scale(int quantity) => (int)((long)quantity * Value / Max);

    public override string ToString() => Value.ToString();
}
