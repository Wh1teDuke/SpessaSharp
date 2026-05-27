namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

/// <summary>
/// Stereo-EQ<br/>
/// This is a four-band stereo equalizer (low, mid x 2, high).
/// Type: Stereo
/// </summary>
public sealed class StereoEQFX: Effect.InsertionProcessor
{
    public override int Type => 0x01_00;

    private readonly int _sampleRate;
    private float _level = 1;
    /// <summary> Selects the frequency of the low range (200 Hz/400 Hz). </summary>
    private int _lowFreq = 400;
    
    /// <summary>
    /// Adjusts the gain of the low frequency.<br/>
    /// [-12;12]
    /// </summary>
    private int _lowGain = 5;
    
    /// <summary> Selects the frequency of the high range (4kHz/8kHz). </summary>
    private int _hiFreq = 8_000;

    /// <summary>
    /// Adjusts the gain of the high frequency.<br/>
    /// [-12;12]
    /// </summary>
    private int _hiGain = -12;
    
    /// <summary>
    /// Adjusts the frequency of Mid 1 (mid range1).<br/>
    /// [200;6300]
    /// </summary>
    private int _m1Freq = 1_600;

    /// <summary>
    /// This parameter adjusts the width of the area around the M1
    /// Freq parameter that will be affected by the Gain setting.
    /// Higher values of Q will result in a narrower area being
    /// affected. <br/>
    /// 0.5/1.0/2.0/4.0/9.0
    /// </summary>
    private float _m1Q = 0.5f;

    /// <summary>
    /// Adjusts the gain for the area specified by the M1 Freq
    /// parameter and M1 Q parameter settings.<br/>
    /// [-12;12]
    /// </summary>
    private int _m1Gain = 8;

    /// <summary>
    /// Adjusts the frequency of Mid 2 (mid range2).
    /// [200;6300]
    /// </summary>
    private int _m2Freq = 1_000;
    
    /// <summary>
    /// This parameter adjusts the width of the area around the M2
    /// Freq parameter that will be affected by the Gain setting.
    /// Higher values of Q will result in a narrower area being
    /// affected.
    /// 0.5/1.0/2.0/4.0/9.0
    /// </summary>
    private float _m2Q = 0.5f;

    /// <summary>
    /// Adjusts the gain for the area specified by the M2 Freq
    /// parameter and M2 Q parameter settings.
    /// [-12;12]
    /// </summary>
    private int _m2Gain = -8;

    private InsUtil.BiquadCoeffs _lowCoeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _m1Coeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _m2Coeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _hiCoeffs = InsUtil.ZeroCoeffs;

    private InsUtil.BiquadState _lowStateL = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _lowStateR = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _m1StateL = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _m1StateR = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _m2StateL = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _m2StateR = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _hiStateL = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _hiStateR = InsUtil.ZeroStateC;

    public StereoEQFX(int sampleRate)
    {
        SendLevelToReverb = 0;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
        
        _sampleRate = sampleRate;
        
        Reset();
        UpdateCoefficients();
    }
    
    public override void Reset()
    {
        _level = 1;
        _lowFreq = 400;
        _lowGain = 5;
        _hiGain = -12;
        _hiFreq = 8_000;
        _m1Freq = 1_600;
        _m1Q = 0.5f;
        _m1Gain = 8;
        _m2Freq = 1_000;
        _m2Q = 0.5f;
        _m2Gain = -8;
        
        // Reset states
        _lowStateL = InsUtil.ZeroStateC;
        _lowStateR = InsUtil.ZeroStateC;
        _m1StateL = InsUtil.ZeroStateC;
        _m1StateR = InsUtil.ZeroStateC;
        _m2StateL = InsUtil.ZeroStateC;
        _m2StateR = InsUtil.ZeroStateC;
        _hiStateL = InsUtil.ZeroStateC;
        _hiStateR = InsUtil.ZeroStateC;
        
        UpdateCoefficients();
    }

    public override void SetParameter(int parameter, int value)
    {
        switch (parameter) 
        {
            default: 
                break;

            case 0x03: 
                _lowFreq = value == 1 ? 400 : 200;
                break;

            case 0x04: 
                _lowGain = value - 64;
                break;

            case 0x05: 
                _hiFreq = value == 1 ? 8_000 : 4_000;
                break;

            case 0x06: 
                _hiGain = value - 64;
                break;

            case 0x07: 
                _m1Freq = InsertionValueConverter.EqFreq(value);
                break;

            case 0x08:
                var m1Q = (ReadOnlySpan<float>)[.5f, 1, 2, 4, 9];
                _m1Q = value >= 0 && value < m1Q.Length ? m1Q[value] : 1;
                break;

            case 0x09: 
                _m1Gain = value - 64;
                break;

            case 0x0a: 
                _m2Freq = InsertionValueConverter.EqFreq(value);
                break;

            case 0x0b:
                var m2Q = (ReadOnlySpan<float>)[.5f, 1, 2, 4, 9];
                _m2Q = value >= 0 && value < m2Q.Length ? m2Q[value] : 1;
                break;

            case 0x0c: 
                _m2Gain = value - 64;
                break;

            case 0x16: 
                _level = value / 127f;
                break;
        }

        UpdateCoefficients();
    }

    public override void Process(
        ReadOnlySpan<float> inputLeft, 
        ReadOnlySpan<float> inputRight, 
        
        Span<float> outputLeft, 
        Span<float> outputRight, 
        
        Span<float> outputReverb,
        Span<float> outputChorus, 
        Span<float> outputDelay, 
        
        int startIndex, 
        int sampleCount)
    {
        var level = _level;
        var sendLevelToChorus = SendLevelToChorus;
        var sendLevelToDelay = SendLevelToDelay;
        var sendLevelToReverb = SendLevelToReverb;

        for (var i = 0; i < sampleCount; i++)
        {
            var sL = inputLeft[i];
            var sR = inputRight[i];

            // Low -> m1 -> m2 -> hi
            sL = InsUtil.ProcessBiquad(sL, _lowCoeffs, ref _lowStateL);
            sR = InsUtil.ProcessBiquad(sR, _lowCoeffs, ref _lowStateR);

            sL = InsUtil.ProcessBiquad(sL, _m1Coeffs, ref _m1StateL);
            sR = InsUtil.ProcessBiquad(sR, _m1Coeffs, ref _m1StateR);

            sL = InsUtil.ProcessBiquad(sL, _m2Coeffs, ref _m2StateL);
            sR = InsUtil.ProcessBiquad(sR, _m2Coeffs, ref _m2StateR);

            sL = InsUtil.ProcessBiquad(sL, _hiCoeffs, ref _hiStateL);
            sR = InsUtil.ProcessBiquad(sR, _hiCoeffs, ref _hiStateR);
            
            // Mix
            var idx = startIndex + i;
            outputLeft[idx] += sL * level;
            outputRight[idx] += sR * level;
            // Sends (index 0)
            var mono = 0.5f * (sL + sR);
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }
    }
    
    private void UpdateCoefficients() 
    {
        // Dividing low and hi gain by 2 seems to improve accuracy to SCVA
        ComputeLowShelfCoeffs(
            ref _lowCoeffs,
            _lowFreq,
            _lowGain / 2,
            _sampleRate);

        ComputePeakingEQCoeffs(
            ref _m1Coeffs,
            _m1Freq,
            _m1Gain,
            _m1Q,
            _sampleRate);

        ComputePeakingEQCoeffs(
            ref _m2Coeffs,
            _m2Freq,
            _m2Gain,
            _m2Q,
            _sampleRate);

        ComputeHighShelfCoeffs(
            ref _hiCoeffs,
            _hiFreq,
            _hiGain / 2,
            _sampleRate);
    }
    
    /// <summary>
    /// Robert Bristow-Johnson cookbook formulas<br/>
    /// (https://webaudio.github.io/Audio-EQ-Cookbook/audio-eq-cookbook.html)<br/>
    /// S - a "shelf slope" parameter (for shelving EQ only).<br/>
    /// When S = 1, the shelf slope is as steep as it can be and remain monotonically increasing or decreasing gain with frequency.<br/>
    /// The shelf slope, in dB/octave,<br/>
    /// remains proportional to S for all other values for a fixed  f0/Fs and dB gain.
    /// </summary>
    private const float SHELF_SLOPE = 1;

    private static void ComputePeakingEQCoeffs(
        ref InsUtil.BiquadCoeffs coeffs,
        int freq,
        int gainDB,
        float Q,
        int sampleRate)
    {
        var A = float.Pow(10, gainDB / 40f);
        var w0 = (2 * MathF.PI * freq) / sampleRate;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var alpha = sinw0 / (2 * Q);

        var b0 = 1 + alpha * A;
        var b1 = -2 * cosw0;
        var b2 = 1 - alpha * A;
        var a0 = 1 + alpha / A;
        var a1 = -2 * cosw0;
        var a2 = 1 - alpha / A;

        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
    }

    private static void ComputeLowShelfCoeffs(  
        ref InsUtil.BiquadCoeffs coeffs,
        int freq,
        int gainDB,
        int sampleRate)
    {
        var A = float.Pow(10, gainDB / 40f);
        var w0 = (2 * MathF.PI * freq) / sampleRate;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var alpha =
            (sinw0 / 2) * float.Sqrt((A + 1 / A) * (1 / SHELF_SLOPE - 1) + 2);

        var b0 = A * (A + 1 - (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha);
        var b1 = 2 * A * (A - 1 - (A + 1) * cosw0);
        var b2 = A * (A + 1 - (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha);
        var a0 = A + 1 + (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha;
        var a1 = -2 * (A - 1 + (A + 1) * cosw0);
        var a2 = A + 1 + (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha;

        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
    }

    private static void ComputeHighShelfCoeffs(
        ref InsUtil.BiquadCoeffs coeffs,
        int freq,
        int gainDB,
        int sampleRate)
    {
        var A = float.Pow(10, gainDB / 40f);
        var w0 = (2 * MathF.PI * freq) / sampleRate;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var alpha =
            (sinw0 / 2) * float.Sqrt((A + 1f / A) * (1 / SHELF_SLOPE - 1) + 2);

        var b0 = A * (A + 1 + (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha);
        var b1 = -2 * A * (A - 1 + (A + 1) * cosw0);
        var b2 = A * (A + 1 + (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha);
        var a0 = A + 1 - (A - 1) * cosw0 + 2 * float.Sqrt(A) * alpha;
        var a1 = 2 * (A - 1 - (A + 1) * cosw0);
        var a2 = A + 1 - (A - 1) * cosw0 - 2 * float.Sqrt(A) * alpha;

        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
    }
}