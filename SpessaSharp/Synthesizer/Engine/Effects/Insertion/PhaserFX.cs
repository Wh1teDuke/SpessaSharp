namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

/// <summary>
/// A phaser adds a phase-shifted sound to the original sound, producing a twisting modulation that creates spaciousness and depth. Type: Stereo
/// </summary>
/// <remarks>seems to use a triangle LFO for modulation</remarks>
public sealed class PhaserFX: Effect.InsertionProcessor
{
    public static class Param
    {
        public const int Level = 0x16;
    }
    
    public override int Type => 0x01_20;
    
    /// <summary>
    /// After lots of testing, this was the closest I've been able to get. SC-Va shows a very strange logarithmic curve after 1000 Hz (55 manual), which I wasn't able to replicate, no matter how hard I tried. Values below that are pretty much spot on though.
    /// </summary>
    private const int ALL_PASS_STAGES = 8;
    private const float DEPTH_DIV = 128f;
    private const int MANUAL_MULTIPLIER = 4;
    private const float MANUAL_OFFSET = 600f;
    private const float FEEDBACK = .9f;
    private const float PHASE_START = .35f;
    
    /// <summary>
    /// Adjusts the basic frequency from which the sound will be modulated.<br/>
    /// [100;8000]
    /// </summary>
    private int _manual = 620;
    
    /// <summary>
    /// Adjusts the frequency (period) of modulation.<br/>
    /// [0.05;10.0]
    /// </summary>
    private float _rate = .85f;

    /// <summary>
    /// Adjusts the depth of modulation.<br/>
    /// [0;1]
    /// </summary>
    private float _depth = 64f / DEPTH_DIV;
    
    /// <summary>
    /// Adjusts the amount of emphasis added to the frequency
    /// range surrounding the basic frequency determined by the
    /// Manual parameter setting.<br/>
    /// [0;1]
    /// </summary>
    private float _reso = 16f / 127f;

    /// <summary>
    /// Adjusts the proportion by which the phase-shifted sound is combined with the direct sound.<br/>
    /// [0;1]
    /// </summary>
    private float _mix = 1;

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
    
    // Allpass network
    // Per-channel state for the allpass filters
    private readonly float[] _prevInL;
    private readonly float[] _prevOutL;
    private readonly float[] _prevInR;
    private readonly float[] _prevOutR;
    
    /// <summary> Biquad shelving coefficients and states (per channel) </summary>
    private InsUtil.BiquadCoeffs _lowShelfCoef = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _highShelfCoef = InsUtil.ZeroCoeffs;

    private float _manualOffset = MANUAL_OFFSET;
    private InsUtil.BiquadState _lowShelfStateL = 
        new (x1: 0, x2: 0, y1: 0, y2: 0);
    private InsUtil.BiquadState _lowShelfStateR = 
        new (x1: 0, x2: 0, y1: 0, y2: 0);
    private InsUtil.BiquadState _highShelfStateL = 
        new (x1: 0, x2: 0, y1: 0, y2: 0);
    private InsUtil.BiquadState _highShelfStateR = 
        new (x1: 0, x2: 0, y1: 0, y2: 0);

    private float _prevL = 0;
    private float _prevR = 0;
    
    /// <summary>
    /// Adjusts the output level.<br/>
    /// [0;1]
    /// </summary>
    private float _level = 104f / 127f;
    private float _phase = PHASE_START;
    private readonly int _sampleRate;

    public PhaserFX(int sampleRate)
    {
        SendLevelToReverb = 40f / 127f;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
        
        _sampleRate = sampleRate;

        _prevInL = NewFloatArray();
        _prevOutL = NewFloatArray();
        _prevInR = NewFloatArray();
        _prevOutR = NewFloatArray();

        return;
        float[] NewFloatArray() => new float[ALL_PASS_STAGES];
    }
    
    public override void Reset()
    {
        _phase = PHASE_START;
        SetManual(620);
        _rate = .85f;
        _depth = 64f / DEPTH_DIV;
        _reso = 16f / 127f;
        _mix = 1;
        _lowGain = 0;
        _hiGain = 0;
        _level = 104f / 127f;

        _highShelfStateL = new InsUtil.BiquadState();
        _highShelfStateR = new InsUtil.BiquadState();
        _lowShelfStateL = new InsUtil.BiquadState();
        _lowShelfStateR = new InsUtil.BiquadState();

        UpdateShelves();
        ClearAllPass();
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
        var sendLevelToReverb = SendLevelToReverb;
        var sendLevelToChorus = SendLevelToChorus;
        var sendLevelToDelay = SendLevelToDelay;
        var level = _level;
        var manual = _manual;
        var manualOffset = _manualOffset;
        var mix =  _mix;
        var highShelfCoef = _highShelfCoef;
        var prevInL =  _prevInL.AsSpan();
        var prevInR =  _prevInR.AsSpan();
        var prevOutL =  _prevOutL.AsSpan();
        var prevOutR =  _prevOutR.AsSpan();
        var depth = _depth;
        
        var prevL = _prevL;
        var prevR = _prevR;
        var phase = _phase;
        
        var rateInc = _rate / _sampleRate;
        var fb = _reso * FEEDBACK;

        for (var i = 0; i < sampleCount; i++)
        {
            // Apply EQ to input (EQ is applied regardless of mix)
            var sL = InsUtil.ApplyShelves(
                inputLeft[i],
                _lowShelfCoef,
                highShelfCoef,
                ref _lowShelfStateL,
                ref _highShelfStateL);

            var sR = InsUtil.ApplyShelves(
                inputRight[i],
                _lowShelfCoef,
                highShelfCoef,
                ref _lowShelfStateR,
                ref _highShelfStateR);
            
            // Triangle LFO
            var lfo = 2 * float.Abs(phase - .5f);
            if ((phase += rateInc) >= 1) phase -= 1;
            var lfoMul = 1 - depth * lfo;
            
            // Instantaneous modulated frequency (Hz), depth is fraction
            var fc = manualOffset + manual * lfoMul;

            // Convert to all-pass coefficient 'a' for first-order AP
            var tanTerm = float.Tan((MathF.PI * fc) / _sampleRate);
            var a = Math.Clamp((1 - tanTerm) / (1 + tanTerm), -.9999f, .9999f);
            
            // Process all pass
            var apL = sL + fb * prevL;
            var apR = sR + fb * prevR;
            for (var stage = 0; stage < ALL_PASS_STAGES; stage++) 
            {
                var outL1 = -a * apL + prevInL[stage] + a * prevOutL[stage];
                prevInL[stage] = apL;
                prevOutL[stage] = outL1;
                apL = outL1; // Feed to next stage
                var outR1 = -a * apR + prevInR[stage] + a * prevOutR[stage];
                prevInR[stage] = apR;
                prevOutR[stage] = outR1;
                apR = outR1;
            }
            prevL = apL;
            prevR = apR;

            var outL = (sL + apL * mix) * level;
            var outR = (sR + apR * mix) * level;
            
            // Mix
            var idx = startIndex + i;
            outputLeft[idx] += outL;
            outputRight[idx] += outR;
            var mono = (outL + outR) * .5f;
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }

        _phase = phase;
        _prevL = prevL;
        _prevR = prevR;
    }
    
    
    public override void SetParameter(int parameter, int value)
    {
        switch (parameter) 
        {
            default: break;

            case 0x03:
                SetManual(InsertionValueConverter.Manual(value));
                break;

            case 0x04: 
                _rate = InsertionValueConverter.Rate1(value);
                break;

            case 0x05: 
                _depth = value / DEPTH_DIV;
                break;

            case 0x06: 
                _reso = value / 127f;
                break;

            case 0x07: 
                _mix = value / 127f;
                break;

            case 0x13: 
                _lowGain = value - 64;
                break;

            case 0x14: 
                _hiGain = value - 64;
                break;

            case Param.Level: 
                _level = value / 127f;
                break;
        }

        UpdateShelves();
    }

    private void SetManual(int manualIn)
    {
        if (manualIn > 1_000) 
        {
            _manualOffset = MANUAL_OFFSET * 1.5f * MANUAL_MULTIPLIER;
            _manual = manualIn;
        } 
        else 
        {
            _manualOffset = MANUAL_OFFSET;
            _manual = manualIn * MANUAL_MULTIPLIER;
        }
    }

    private void ClearAllPass()
    {
        _prevR = 0;
        _prevL = 0;
        _prevInL.AsSpan().Clear();
        _prevOutL.AsSpan().Clear();
        _prevInR.AsSpan().Clear();
        _prevOutR.AsSpan().Clear();
    }

    private void UpdateShelves()
    {
        InsUtil.ComputeShelfCoeffs(
            ref _lowShelfCoef,
            _lowGain,
            200,
            _sampleRate,
            true);

        InsUtil.ComputeShelfCoeffs(
            ref _highShelfCoef,
            _hiGain,
            4_000,
            _sampleRate,
            false);
    }
}