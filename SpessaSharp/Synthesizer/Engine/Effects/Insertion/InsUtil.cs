namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

internal static class InsUtil
{
    public enum ModulationType { Tri, Sqr, Sin, Saw1, Saw2 }
    
    public record struct BiquadCoeffs(
        float b0, float b1, float b2, float a0, float a1, float a2);

    public record struct BiquadState(
        float x1, float x2, float y1, float y2);
    
    private const float HALF_PI = MathF.PI / 2;
    private const int MIN_PAN = -64;
    private const int MAX_PAN = 63;
    private const int PAN_RESOLUTION = MAX_PAN - MIN_PAN;

    public static readonly BiquadCoeffs ZeroCoeffs = new(
        b0: 1, b1: 0, b2: 0, a0: 1, a1: 0, a2: 0);

    public static readonly BiquadState ZeroStateC = new(
        0, 0, 0, 0);
    
    // Initialize pan lookup tables
    public static readonly float[] PanTableLeft = 
        new float[PAN_RESOLUTION + 1];
    public static readonly float[] PanTableRight = 
        new float[PAN_RESOLUTION + 1];

    static InsUtil()
    {
        for (var pan = MIN_PAN; pan <= MAX_PAN; pan++)
        {
            // Clamp to 0-1
            var realPan = (pan - MIN_PAN) / PAN_RESOLUTION;
            var tableIndex = pan - MIN_PAN;
            PanTableLeft[tableIndex] = float.Cos(HALF_PI * realPan);
            PanTableRight[tableIndex] = float.Sin(HALF_PI * realPan);
        }
    }

    public static float ApplyShelves(
        float x, 
        BiquadCoeffs lowC, BiquadCoeffs highC,
        ref BiquadState lowS, ref BiquadState highS)
    {
        // Direct form I (inlined for performance)
        // Low shelf
        var l =
            lowC.b0 * x +
            lowC.b1 * lowS.x1 +
            lowC.b2 * lowS.x2 -
            lowC.a1 * lowS.y1 -
            lowC.a2 * lowS.y2;
        lowS.x2 = lowS.x1;
        lowS.x1 = x;
        lowS.y2 = lowS.y1;
        lowS.y1 = l;

        // High shelf
        var h =
            highC.b0 * l +
            highC.b1 * highS.x1 +
            highC.b2 * highS.x2 -
            highC.a1 * highS.y1 -
            highC.a2 * highS.y2;
        highS.x2 = highS.x1;
        highS.x1 = l;
        highS.y2 = highS.y1;
        highS.y1 = h;
        return h;
    }

    public static float ProcessBiquad(
        float x, BiquadCoeffs coeffs, ref BiquadState state)
    {
        // Direct form I
        var y =
            coeffs.b0 * x +
            coeffs.b1 * state.x1 +
            coeffs.b2 * state.x2 -
            coeffs.a1 * state.y1 -
            coeffs.a2 * state.y2;

        state.x2 = state.x1;
        state.x1 = x;
        state.y2 = state.y1;
        state.y1 = y;

        return y;
    }

    /// <summary>
    /// Robert Bristow-Johnson cookbook formulas<br/>
    /// (https://webaudio.github.io/Audio-EQ-Cookbook/audio-eq-cookbook.html)<br/>
    /// S - a "shelf slope" parameter (for shelving EQ only).<br/>
    /// When S = 1, the shelf slope is as steep as it can be and remain monotonically increasing or decreasing gain with frequency.<br/>
    /// The shelf slope, in dB/octave,<br/>
    /// remains proportional to S for all other values for a fixed  f0/Fs and dB gain.
    /// </summary>
    /// <param name="coeffs"></param>
    /// <param name="dbGain"></param>
    /// <param name="f0"></param>
    /// <param name="fs"></param>
    /// <param name="isLow"></param>
    public static void ComputeShelfCoeffs(
        ref BiquadCoeffs coeffs,
        int dbGain,
        int f0,
        int fs,
        bool isLow)
    {
        var A = float.Pow(10, dbGain / 40f);
        var w0 = (2 * MathF.PI * f0) / fs;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var S = 1;
        var alpha =
            (sinw0 / 2f) * float.Sqrt((A + 1f / A) * (1f / S - 1f) + 2f);

        float b0; float b1; float b2; float a0; float a1; float a2;
        
        if (isLow) 
        {
            // Low shelf
            b0 = A * (A + 1 - (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha);
            b1 = 2 * A * (A - 1 - (A + 1) * cosw0);
            b2 = A * (A + 1 - (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha);
            a0 = A + 1 + (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha;
            a1 = -2 * (A - 1 + (A + 1) * cosw0);
            a2 = A + 1 + (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha;
        } 
        else 
        {
            // High shelf
            b0 = A * (A + 1 + (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha);
            b1 = -2 * A * (A - 1 + (A + 1) * cosw0);
            b2 = A * (A + 1 + (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha);
            a0 = A + 1 - (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha;
            a1 = 2 * (A - 1 - (A + 1) * cosw0);
            a2 = A + 1 - (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha;
        }
        
        // Normalize
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
    }
}