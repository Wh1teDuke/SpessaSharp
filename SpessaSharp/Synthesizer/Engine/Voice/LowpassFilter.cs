namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>
/// Applies a low pass filter to a voice
/// note to self: a lot of tricks and come from fluidsynth.
/// They are the real smart guys.
/// Shoutout to them!
/// Give their repo a star over at:
/// https://github.com/FluidSynth/fluidsynth
/// </summary>
public struct LowpassFilter
{
    /// <summary>Latest test: 06-12-2025 for the 9600 cent cc74 change (XG accurate). Lowered from 0.1 to 0.03</summary>
    public const float FILTER_SMOOTHING_FACTOR = 0.03f;
    
    /// <summary>Resonance in centibels.</summary>
    public int ResonanceCb;
    /// <summary>Current cutoff frequency in absolute cents. </summary>
    public int CurrentInitialFc = 13_500;
    /// <summary>Filter coefficient 1.</summary>
    public float a0;
    /// <summary>Filter coefficient 2.</summary>
    public float a1;
    /// <summary>Filter coefficient 3.</summary>
    public float a2;
    /// <summary>Filter coefficient 4.</summary>
    public float a3;
    /// <summary>Filter coefficient 5.</summary>
    public float a4;
    /// <summary>Input history 1.</summary>
    public float x1;
    /// <summary>Input history 2.</summary>
    public float x2;
    /// <summary>Output history 1.</summary>
    public float y1;
    /// <summary>Output history 2.</summary>
    public float y2;
    
    /// <summary>
    /// For tracking the last cutoff frequency in the apply method, absolute cents. Set to infinity to force recalculation.
    /// </summary>
    public float LastTargetCutoff = float.PositiveInfinity;
    /// <summary>
    /// Used for tracking if the filter has been initialized.
    /// </summary>
    public bool Initialized = false;
    
    /// <summary> Filter's sample rate in Hz. </summary>
    private readonly int _sampleRate;

    /// <summary>
    /// Maximum cutoff frequency in Hz. This is used to prevent aliasing and ensure the filter operates within the valid frequency range.
    /// </summary>
    private readonly float _maxCutoff;

    /// <summary>Initializes a new instance of the filter.</summary>
    /// <param name="sampleRate">The sample rate of the audio engine in Hz.</param>
    public LowpassFilter(int sampleRate)
    {
        _sampleRate = sampleRate;
        _maxCutoff = _sampleRate * .45f;
    }

    public void Init()
    {
        LastTargetCutoff = float.PositiveInfinity;
        ResonanceCb = 0;
        CurrentInitialFc = 13_500;
        a0 = 0;
        a1 = 0;
        a2 = 0;
        a3 = 0;
        a4 = 0;
        x1 = 0;
        x2 = 0;
        y1 = 0;
        y2 = 0;
        Initialized = false;
    }

    /// <summary>Calculates the filter coefficients based on the current resonance and cutoff frequency and caches them.</summary>
    /// <param name="cutoffCents">The cutoff frequency in cents.</param>
    public void CalculateCoefficients(int cutoffCents)
    {
        var qCb = ResonanceCb;

        var cutoffHz = UnitConverter.AbsCentsToHz(cutoffCents);

        // Fix cutoff on low sample rates
        cutoffHz = float.Min(cutoffHz, _maxCutoff);

        // The coefficient calculation code was originally ported from meltysynth by sinshu.
        // Turn resonance to gain, -3.01 so it gives a non-resonant peak
        // -1 because it's attenuation, and we don't want attenuation
        var resonanceGain = UnitConverter.CbAttenuationToGain(
            (int)-(qCb - 3.01f));

        // The sf spec asks for a reduction in gain based on the Q value.
        // Note that we calculate it again,
        // Without the 3.01-peak offset as it only applies to the coefficients, not the gain.
        var qGain = 1f / float.Sqrt(UnitConverter.CbAttenuationToGain(-qCb));

        var w = (2 * MathF.PI * cutoffHz) / _sampleRate;
        var cosw = float.Cos(w);
        var alpha = float.Sin(w) / (2 * resonanceGain);

        var b1 = (1 - cosw) * qGain;
        var b0 = b1 / 2;
        var b2 = b0;
        var a0 = 1 + alpha;
        var a1 = -2 * cosw;
        var a2 = 1 - alpha;

        this.a0 = b0 / a0;
        this.a1 = b1 / a0;
        this.a2 = b2 / a0;
        this.a3 = a1 / a0;
        this.a4 = a2 / a0;
    }
}