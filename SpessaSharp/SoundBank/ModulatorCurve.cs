namespace SpessaSharp.SoundBank;

/// <summary>
/// Precomputes modulator concave and convex curves and calculates a curve value for a given polarity, direction and type
/// </summary>
public static class ModulatorCurve
{
    public enum Type { Linear, Concave, Convex, Switch, }
    
    /// <summary> The length of the precomputed curve tables </summary>
    internal const int MODULATOR_RESOLUTION = 16_384;
    
    internal static readonly int MOD_CURVE_TYPES_AMOUNT =
        Enum.GetValues<Type>().Length;
    
    /*
     * Unipolar positive
     * unipolar negative
     * bipolar positive
     * bipolar negative
     * that's 4
     */
    internal const int MOD_SOURCE_TRANSFORM_POSSIBILITIES = 4;

    // Precalculate lookup tables for concave and convex curves
    private static readonly float[] Concave =
        new float[MODULATOR_RESOLUTION + 1];
    private static readonly float[] Convex =
        new float[MODULATOR_RESOLUTION + 1];

    static ModulatorCurve()
    {
        // The equation is taken from FluidSynth as it's the standard for soundFonts
        // More precisely, the gen_conv.c file
        var ln10 = float.Log(10);
        
        Concave[0] = 0;
        Concave[^1] = 1;

        Convex[0] = 0;
        Convex[^1] = 1;

        for (var i = 1; i < MODULATOR_RESOLUTION - 1; i++) 
        {
            var x =
                (((-200f * 2) / 960) * 
                float.Log((float)i / (Concave.Length - 1))) 
                / ln10;

            Convex[i] = 1 - x;
            Concave[Concave.Length - 1 - i] = x;
        }
    }
    
    /// <summary> Transforms a value with a given curve type </summary>
    /// <param name="transformType">The bipolar and negative flags as a 2-bit number: 0bPD (polarity MSB, direction LSB)</param>
    /// <param name="curveType">Enumeration of curve types</param>
    /// <param name="value">The linear value, 0 to 1</param>
    /// <returns>The transformed value, 0 to 1, or -1 to 1</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    internal static float GetValue(
        int transformType,
        Type curveType,
        float value)
    {
        var isBipolar = (transformType & 0b10) != 0;
        var isNegative = (transformType & 1) != 0;

        // Inverse the value if needed
        if (isNegative) value = 1 - value;

        switch (curveType) 
        {
            case Type.Linear:
                if (isBipolar)
                    // Bipolar curve
                    return value * 2 - 1;
                return value;

            case Type.Switch:
                // Switch
                value = value > 0.5f ? 1 : 0;
                if (isBipolar)
                    // Multiply
                    return value * 2 - 1;
                return value;

            case Type.Concave:
                // Look up the value
                if (isBipolar) 
                {
                    value = value * 2 - 1;
                    if (value < 0)
                        return -Concave[(int)(value * -MODULATOR_RESOLUTION)];
                    return Concave[(int)(value * MODULATOR_RESOLUTION)];
                }
                return Concave[(int)(value * MODULATOR_RESOLUTION)];

            case Type.Convex:
                // Look up the value
                if (isBipolar) 
                {
                    value = value * 2 - 1;
                    if (value < 0)
                        return -Convex[(int)(value * -MODULATOR_RESOLUTION)];
                    return Convex[(int)(value * MODULATOR_RESOLUTION)];
                }
                return Convex[(int)(value * MODULATOR_RESOLUTION)];

            default:
                throw new ArgumentOutOfRangeException(nameof(curveType), curveType, null);
        }
    }
}