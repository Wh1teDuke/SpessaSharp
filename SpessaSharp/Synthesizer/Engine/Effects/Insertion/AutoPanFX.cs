namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

public sealed class AutoPanFX: Effect.InsertionProcessor
{
    public override int Type => 0x01_26;
    
    private const float DEFAULT_LEVEL = 127f;
    
    /// <summary>
    /// Selects the type of modulation.<br/>
    /// Tri:<br/>
    /// The sound will be modulated like a triangle wave.<br/>
    /// Sqr:<br/>
    /// The sound will be modulated like a square wave.<br/>
    /// Sin:<br/>
    /// The sound will be modulated like a sine wave.<br/>
    /// Saw1,2: The sound will be modulated like a sawtooth wave. The teeth in Saw1 and<br/>
    /// Saw2 point at opposite direction.<br/>
    /// [Tri/Sqr/Sin/Saw1/Saw2 -> 00/01/02/03/04]
    /// </summary>
    private InsUtil.ModulationType _modWave = InsUtil.ModulationType.Sqr;
    
    /// <summary>
    /// Adjusts the frequency of modulation.<br/>
    /// [Rate1 conversion]
    /// </summary>
    private float _modRate = 3.05f;

    /// <summary>
    /// Adjusts the depth of modulation.<br/>
    /// [0;127]
    /// </summary>
    private int _modDepth = 96;

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

    private float _currentPan = 0;
    private float _phase = 0;
    
    /// <summary> Biquad shelving coefficients and states (per channel) </summary>
    private InsUtil.BiquadCoeffs _lsCoeffs = InsUtil.ZeroCoeffs;
    private InsUtil.BiquadCoeffs _hsCoeffs = InsUtil.ZeroCoeffs;
    
    /// <summary> Low shelf </summary>
    private InsUtil.BiquadState _lsStateR = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _lsStateL = InsUtil.ZeroStateC;

    /// <summary> High shelf </summary>
    private InsUtil.BiquadState _hsStateR = InsUtil.ZeroStateC;
    private InsUtil.BiquadState _hsStateL = InsUtil.ZeroStateC;

    private readonly int _sampleRate;

    public AutoPanFX(int sampleRate)
    {
        SendLevelToReverb = 40f / 127f;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
        
        _sampleRate = sampleRate;
        
        Reset();
    }
    
    public override void Reset()
    {
        _modWave = InsUtil.ModulationType.Sqr;
        _modRate = 3.05f;
        _modDepth = 96;
        _lowGain = 0;
        _hiGain = 0;
        _level = DEFAULT_LEVEL / 127f;
        _currentPan = 0;
        _phase = 0;
        _hsStateR = InsUtil.ZeroStateC;
        _hsStateL = InsUtil.ZeroStateC;
        _lsStateR = InsUtil.ZeroStateC;
        _lsStateL = InsUtil.ZeroStateC;

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
        const float LEVEL_EXP = 2f;
        const float GAIN_LVL = .935f;
        const float PI_2 = MathF.PI * 2f;
        const float PAN_SMOOTHING = .01f;
        
        var sendLevelToReverb = SendLevelToReverb;
        var sendLevelToChorus = SendLevelToChorus;
        var sendLevelToDelay = SendLevelToDelay;
        var level = _level;
        var modWave = _modWave;
        
        var depth = float.Pow(_modDepth / 127f, LEVEL_EXP);
        var scale = (2 / (1 + depth)) * GAIN_LVL;
        var rateInc = _modRate / _sampleRate;
        var phase = _phase;
        var currentPan = _currentPan;

        for (var i = 0; i < sampleCount; i++)
        {
            // Apply EQ to input (EQ is applied regardless of mix)
            var sL = InsUtil.ApplyShelves(
                inputLeft[i],
                _lsCoeffs,
                _hsCoeffs,
                ref _lsStateL,
                ref _hsStateL);

            var sR = InsUtil.ApplyShelves(
                inputRight[i],
                _lsCoeffs,
                _hsCoeffs,
                ref _lsStateR,
                ref _hsStateR);
            
            // -1 left
            // 1 right
            var lfo = modWave switch
            {
                InsUtil.ModulationType.Tri =>
                    // 0 -> triangle
                    1 - 4 * float.Abs(phase - .5f),
                InsUtil.ModulationType.Sqr =>
                    // 1 - square
                    // This weird half-sine wave is what SC-VA produces so we have to keep it
                    phase > .5f ? -1 : -float.Cos((phase - .75f) * PI_2),
                InsUtil.ModulationType.Sin =>
                    // 2 - sine
                    float.Sin(PI_2 * phase),
                InsUtil.ModulationType.Saw1 =>
                    // Saw1
                    1 - 2 * phase,
                InsUtil.ModulationType.Saw2 =>
                    // Saw2
                    2 * phase - 1,
                _ => throw new ArgumentOutOfRangeException()
            };
            
            if ((phase += rateInc) >= 1) phase -= 1;
            currentPan += (lfo - currentPan) * PAN_SMOOTHING;
            var pan = currentPan * depth;
            var gainL = (1 - pan) * .5f * scale;
            var gainR = (1 + pan) * .5f * scale;

            var outL = sL * level * gainL;
            var outR = sR * level * gainR;

            // Mix
            var idx = startIndex + i;
            outputLeft[idx] += outL;
            outputRight[idx] += outR;
            var mono = (outL + outR) * .5f;
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }

        _currentPan = currentPan;
        _phase = phase;
    }
    
    public override void SetParameter(int parameter, int value)
    {
        switch (parameter) 
        {
            default: break;

            case 0x03: 
                _modWave = (InsUtil.ModulationType)value;
                break;

            case 0x04: 
                _modRate = InsertionValueConverter.Rate1(value);
                break;

            case 0x05: 
                _modDepth = value;
                break;

            case 0x13: 
                _lowGain = value - 64;
                break;

            case 0x14: 
                _hiGain = value - 64;
                break;

            case 0x16: 
                _level = value / 127f;
                break;
        }

        UpdateShelves();
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
}