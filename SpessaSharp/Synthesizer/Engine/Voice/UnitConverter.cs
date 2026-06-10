using System.Runtime.CompilerServices;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>
/// Converts soundfont units into more usable values with the use of lookup tables to improve performance
/// </summary>
public static class UnitConverter
{
    // Timecent lookup table
    private const int MIN_TIMECENT = -15_000;
    private const int MAX_TIMECENT = +15_000;
    
    private static readonly float[] TimecentLookupTable =
        new float[MAX_TIMECENT - MIN_TIMECENT + 1];
    
    /// <summary>Converts timecents to seconds.</summary>
    /// <param name="timecents">The timecents value.</param>
    /// <returns>The time in seconds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float TimecentsToSeconds(int timecents) => 
        timecents <= -32_767
            ? 0
            : TimecentLookupTable[timecents - MIN_TIMECENT];
    
    /// <summary>Converts seconds to timecents.</summary>
    /// <param name="seconds">The seconds value.</param>
    /// <returns>The time in timecents.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SecondsToTimecents(double seconds)
    {
        if (seconds is <= 0 or double.NaN) return -12_000;

        var timecents = 1_200.0 * Math.Log2(seconds);
        return (short)Math.Clamp(
            (long)Math.Round(timecents), -12_000, short.MaxValue);
    }
    
    // Abs cent lookup table
    private const int MIN_ABS_CENT = -20_000; // FreqVibLfo
    private const int MAX_ABS_CENT = 16_500; // FilterFc
    private static readonly float[] AbsoluteCentLookupTable = 
        new float[MAX_ABS_CENT - MIN_ABS_CENT + 1];

    /// <summary>Converts absolute cents to frequency in Hz.</summary>
    /// <param name="cents">The absolute cents value.</param>
    /// <returns>The frequency in Hz.</returns>
    public static float AbsCentsToHz(int cents) =>
        cents is < MIN_ABS_CENT or > MAX_ABS_CENT
            ? 440 * float.Pow(2, (cents - 6_900) / 1_200f)
            : AbsoluteCentLookupTable[cents - MIN_ABS_CENT];
    
    // Centibel lookup table (1 cB precision)
    // 1 dB = 10 cB
    public const int MIN_CENTIBELS = -16_600; // -1660 dB
    private const int MAX_CENTIBELS = 16_000; //  1600 dB
    
    internal static readonly float[] CentibelLookupTable = new float[
        MAX_CENTIBELS - MIN_CENTIBELS + 1];

    /// <summary>Converts centibel attenuation to gain</summary>
    /// <param name="centibels">The centibel value.</param>
    /// <returns>The gain value.</returns>
    public static float CbAttenuationToGain(int centibels) =>
        CentibelLookupTable[centibels - MIN_CENTIBELS];

    static UnitConverter()
    {
        for (var i = 0; i < TimecentLookupTable.Length; i++) 
        {
            var timecents = MIN_TIMECENT + i;
            TimecentLookupTable[i] = float.Pow(2, timecents / 1_200f);
        }

        for (var i = 0; i < AbsoluteCentLookupTable.Length; i++) 
        {
            var absoluteCents = MIN_ABS_CENT + i;
            AbsoluteCentLookupTable[i] =
                440 * float.Pow(2, (absoluteCents - 6_900) / 1_200f);
        }

        for (var i = 0; i < CentibelLookupTable.Length; i++) 
        {
            var centibels = MIN_CENTIBELS + i;
            CentibelLookupTable[i] = float.Pow(10, -centibels / 200f);
        }
    }
}