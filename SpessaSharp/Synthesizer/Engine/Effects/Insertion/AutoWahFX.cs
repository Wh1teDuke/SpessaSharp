namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

/// <summary>
/// The Auto Wah cyclically controls a filter to create cyclic change in timbre.<br/>
/// Type: Mono
/// </summary>
public sealed class AutoWahFX: Effect.InsertionProcessor
{
    public static class Param
    {
        public const int Level = 0x16;
    }
    
    private const float DEFAULT_LEVEL = 96;
    private const float DEPTH_MUL = 5;

    public enum FilterType
    {
        /// <summary> The wah effect will be applied over a wide frequency range.</summary>
        LPF,
        /// <summary>The wah effect will be applied over a narrow frequency range.</summary>
        BPF,
    }

    public enum Polarity { Down, Up }

    public override int Type => 0x01_21;

    /// <summary> Selects the type of filter. </summary>
    private FilterType _filType = FilterType.BPF;
    
    /// <summary>
    /// Adjusts the sensitivity with which the filter is controlled. If this value is increased, the filter frequency will change more readily in response to the input level.<br/>
    /// [0;127]
    /// </summary>
    private int _sens = 0;
    
    /// <summary>
    /// Adjusts the center frequency from which the effect is applied.
    /// Note: Doesn't use "Manual" conversion??<br/>
    /// [0;127] (assuming manual though, seems to use a part of the curve)
    /// </summary>
    private int _manual = 68;
    
    /// <summary>
    /// Adjusts the amount of the wah effect that will occur in the
    /// area of the center frequency. Lower settings will cause the
    /// effect to be applied in a broad area around the center
    /// frequency. Higher settings will cause the effect to be
    /// applied in a more narrow range. In the case of LPF,
    /// decreasing the value will cause the wah effect to change less.<br/>
    /// [0;127]
    /// </summary>
    private int _peak = 62;
    
    /// <summary>
    /// Adjusts the speed of the modulation.<br/>
    /// [Rate1 conversion]
    /// </summary>
    private float _rate = 2.05f;

    /// <summary>
    /// Adjusts the depth of the modulation.
    /// [0;127]
    /// </summary>
    private int _depth = 72;

    /// <summary>
    /// Sets the direction in which the frequency will change when
    /// the filter is modulated. With a setting of Up, the filter will
    ///  change toward a higher frequency. With a setting of Down
    /// it will change toward a lower frequency.
    /// </summary>
    private Polarity _polarity = Polarity.Up;
    
    /// <summary>
    /// Adjusts the stereo location of the output sound. L63 is far
    /// left, 0 is center, and R63 is far right.<br/>
    /// [-64;63]
    /// </summary>
    private int _pan = 0;
    
    /// <summary>
    /// Adjusts the gain of the low frequency range. (200Hz)<br/>
    /// [-12;12]
    /// </summary>
    private int _lowGain = 0;

    /// <summary>
    /// Adjusts the gain of the high frequency range. (4kHz)<br/>
    /// [-12;12]
    /// </summary>
    private int _hiGain = 0;
    
    /// <summary>
    /// Adjusts the output level.<br/>
    /// [0;1]
    /// </summary>
    private float _level = DEFAULT_LEVEL / 127f;

    private InsUtil.BiquadCoeffs _coeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadState _state = InsUtil.ZeroStateC;
    private InsUtil.BiquadCoeffs _hpCoeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadState _hpState = InsUtil.ZeroStateC;
    private float _phase = 0;
    
    /// <summary> Biquad shelving coefficients and states (per channel) </summary>
    private InsUtil.BiquadCoeffs _lsCoeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _hsCoeffs = InsUtil.ZeroCoeffs;
    
    /// <summary> Low shelf </summary>
    private InsUtil.BiquadState _lsState = InsUtil.ZeroStateC;
    /// <summary> High shelf </summary>
    private InsUtil.BiquadState _hsState = InsUtil.ZeroStateC;

    private readonly int _sampleRate;
    private float _lastFc;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;
    private float _envelope = 0;
    
    public AutoWahFX(int sampleRate)
    {
        const float attackTime = .1f;
        const float releaseTime = .1f;
        
        SendLevelToReverb = 40f / 127f;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
        _lastFc = _manual;

        _sampleRate = sampleRate;
        
        _attackCoeff = float.Exp(-1 / (attackTime * sampleRate));
        _releaseCoeff = float.Exp(-1 / (releaseTime * sampleRate));

        Reset();
    }
    
    public override void Reset()
    {
        _filType = FilterType.BPF;
        _sens = 0;
        SetManual(68);
        _peak = 62;
        _rate = 2.05f;
        _depth = 72;
        _polarity = Polarity.Up;
        _lowGain = 0;
        _hiGain = 0;
        _pan = 0;
        _level = DEFAULT_LEVEL / 127f;
        _phase = .2f;
        _lastFc = _manual;

        _hsState = InsUtil.ZeroStateC;
        _lsState = InsUtil.ZeroStateC;
        _state = InsUtil.ZeroStateC;
        _hpState = InsUtil.ZeroStateC;

        UpdateShelves();
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
        const float PEAK_DB = 128f;
        const float HPF_Q = -28f;
        const float LFO_SMOOTH_FRAC = DEPTH_MUL * .5f;
        const float SENS_COEFF = 27f;
        const float FC_SMOOTH = .005f;
        const float HPF_FC = 400f;
        
        var sendLevelToReverb = SendLevelToReverb;
        var sendLevelToChorus = SendLevelToChorus;
        var sendLevelToDelay = SendLevelToDelay;
        var level = _level;
        var lsCoeffs = _lsCoeffs;
        var hsCoeffs = _hsCoeffs;
        var filType = _filType;
        var manual = _manual;
        var pan =  _pan;

        var phase = _phase;
        var lastFc = _lastFc;
        var envelope = _envelope;
        
        var rateInc = _rate / _sampleRate;
        var peak = float.Pow(10, ((_peak / 127f) * PEAK_DB) / 20f);
        var hpfPeak = float.Pow(10, ((_peak / 127f) * HPF_Q) / 20f);
        // FIXME: Polarity in 11Gtr_EFX shows that this is very wrong...
        var pol = _polarity == Polarity.Down ? -1 : DEPTH_MUL;
        var depth = (_depth / 127f) * pol;
        var sens = _sens / 127f;
        
        var index = (pan + 64);
        var gainL = InsUtil.PanTableLeft[index];
        var gainR = InsUtil.PanTableRight[index];

        for (var i = 0; i < sampleCount; i++)
        {
            // Mono!
            var s = InsUtil.ApplyShelves(
                (inputLeft[i] + inputRight[i]) * 0.5f,
                lsCoeffs,
                hsCoeffs,
                ref _lsState,
                ref _hsState);

            var rectified  = float.Abs(s);
            
            envelope =
                rectified > envelope
                    ? _attackCoeff * envelope + (1 - _attackCoeff) * rectified
                    : _releaseCoeff * envelope + (1 - _releaseCoeff) * rectified;

            // Triangle LFO
            var lfo = 2 * float.Abs(phase - .5f) * depth;
            if ((phase += rateInc) >= 1) phase -= 1;
            var lfoMul =
                lfo >= LFO_SMOOTH_FRAC || pol < 0
                ? 1
                : float.Sin((lfo * MathF.PI) / (2 * LFO_SMOOTH_FRAC));
            var @base = manual * (1 + sens * envelope * SENS_COEFF);
            var fc = float.Max(20, @base * (1 + lfoMul * lfo));
            var target = float.Max(10, fc);
            lastFc += (target - lastFc) * FC_SMOOTH;
            ComputeLowPassCoeffs(ref _coeffs, lastFc, peak, _sampleRate);
            
            var processedSample = s;

            if (filType == FilterType.BPF) 
            {
                ComputeHighPassCoeffs(ref _hpCoeffs, HPF_FC, hpfPeak, _sampleRate);
                processedSample = InsUtil.ProcessBiquad(
                    processedSample,
                    _hpCoeffs,
                    ref _hpState);
            }

            var mono = InsUtil.ProcessBiquad(
                processedSample, _coeffs, ref _state) * level;
            
            // Pan
            var outL = mono * gainL;
            var outR = mono * gainR;

            // Mix
            var idx = startIndex + i;
            outputLeft[idx] += outL;
            outputRight[idx] += outR;
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }

        _phase = phase;
        _lastFc = lastFc;
        _envelope = envelope;
    }
    
    public override void SetParameter(int parameter, int value)
    {
        switch (parameter) 
        {
            default: break;

            case 0x03: 
                _filType = (FilterType)value;
                break;

            case 0x04: 
                _sens = value;
                break;

            case 0x05: 
                SetManual(value);
                break;

            case 0x06: 
                _peak = value;
                break;

            case 0x07: 
                _rate = InsertionValueConverter.Rate1(value);
                break;

            case 0x08: 
                _depth = value;
                break;

            case 0x09: 
                _polarity = (Polarity)value;
                break;

            case 0x13: 
                _lowGain = value - 64;
                break;

            case 0x14: 
                _hiGain = value - 64;
                break;

            case 0x15: 
                _pan = value - 64;
                break;

            case Param.Level: 
                _level = value / 127f;
                break;
        }

        UpdateShelves();
    }
    
    private void SetManual(int value)
    {
        const float manualScale = .62f;
        var target = value * manualScale;
        var floor = InsertionValueConverter.Manual((int)float.Floor(target));
        var ceil = InsertionValueConverter.Manual((int)float.Ceiling(target));
        var frac = target - float.Floor(target);

        _manual = (int)(floor + (ceil - floor) * frac);
    }

    private void UpdateShelves()
    {
        InsUtil.ComputeShelfCoeffs(
            ref _lsCoeffs,
            _lowGain,
            200,
            _sampleRate,
            true);

        InsUtil.ComputeShelfCoeffs(
            ref _hsCoeffs,
            _hiGain,
            4_000,
            _sampleRate,
            false);
    }

    private static void ComputeLowPassCoeffs(
        ref InsUtil.BiquadCoeffs coeffs,
        float freq,
        float Q,
        int sampleRate)
    {
        var w0 = (2 * MathF.PI * freq) / sampleRate;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var alpha = sinw0 / (2 * Q);

        var b1 = 1 - cosw0;
        var b0 = b1 / 2;
        var b2 = b0;
        var a0 = 1 + alpha;
        var a1 = -2 * cosw0;
        var a2 = 1 - alpha;

        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
    }

    private static void ComputeHighPassCoeffs(
        ref InsUtil.BiquadCoeffs coeffs,
        float freq,
        float Q,
        int sampleRate)
    {
        var w0 = (2 * MathF.PI * freq) / sampleRate;
        var cosw0 = float.Cos(w0);
        var sinw0 = float.Sin(w0);
        var alpha = sinw0 / (2 * Q);

        var b0 = (1 + cosw0) / 2;
        var b1 = -(1 + cosw0);
        var b2 = b0;
        var a0 = 1 + alpha;
        var a1 = -2 * cosw0;
        var a2 = 1 - alpha;

        coeffs.a0 = 1;
        coeffs.a1 = a1 / a0;
        coeffs.a2 = a2 / a0;
        coeffs.b0 = b0 / a0;
        coeffs.b1 = b1 / a0;
        coeffs.b2 = b2 / a0;
    }
}