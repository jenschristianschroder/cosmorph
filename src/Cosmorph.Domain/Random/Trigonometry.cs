namespace Cosmorph.Domain.Random;

/// <summary>
/// Integer trigonometry used by the authoritative simulation. Floating point is deliberately avoided
/// so results are bit-identical on every platform.
/// </summary>
public static class Trigonometry
{
    /// <summary>Quarter-period sine table scaled to 1000, sampled every degree.</summary>
    private static readonly int[] SineTable = BuildTable();

    /// <summary>Returns sin(degrees) scaled to permille, i.e. -1000..1000.</summary>
    public static int SinMilli(int degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        return d switch
        {
            <= 90 => SineTable[d],
            <= 180 => SineTable[180 - d],
            <= 270 => -SineTable[d - 180],
            _ => -SineTable[360 - d],
        };
    }

    /// <summary>Returns cos(degrees) scaled to permille, i.e. -1000..1000.</summary>
    public static int CosMilli(int degrees) => SinMilli(degrees + 90);

    private static int[] BuildTable()
    {
        var table = new int[91];
        for (var i = 0; i <= 90; i++)
        {
            table[i] = (int)Math.Round(Math.Sin(i * Math.PI / 180d) * 1000d);
        }

        return table;
    }
}
