namespace SpessaSharp.Synthesizer.Engine.Effects;

public sealed class SSChorus: Effect.ChorusProcessor
{
    /// <summary> Cutoff frequency </summary>
    private float _preLPFfc = 8_000;
    /// <summary> Alpha </summary>
    private float _preLPFa = 0;
    /// <summary> Previous value </summary>
    private float _preLPFz = 0;

    private readonly float[] _leftDelayBuffer;
    private readonly float[] _rightDelayBuffer;

    private readonly int _sampleRate;
    
    private float _phase = 0;
    private int _write = 0;
    private float _gain = .5f;
    private float _reverbGain = 0;
    private float _delayGain = 0;
    private float _depthSamples = 0;
    private float _delaySamples = 1;
    private float _rateInc = 0;
    private float _feedbackGain = 0;

    private int _sendLevelToReverb = 0;
    private int _sendLevelToDelay = 0;
    private int _preLowPass = 0;
    private int _depth = 0;
    private int _delay = 0;
    private int _feedback = 0;
    private int _rate = 0;
    private int _level = 64;
    
    public SSChorus(int sampleRate, int maxBufferSize) 
    {
        _sampleRate         = sampleRate;
        _leftDelayBuffer    = new float[sampleRate];
        _rightDelayBuffer   = new float[sampleRate];

        // Update alpha
        _preLowPass = 0;
        // For consistency across effects
        // discard maxBufferSize
    }

    public override int SendLevelToReverb
    {
        get => _sendLevelToReverb;
        set
        {
            _sendLevelToReverb = value;
            _reverbGain = value / 127f;
        }
    }

    public override int SendLevelToDelay
    {
        get => _sendLevelToDelay;
        set
        {
            _sendLevelToDelay = value;
            _delayGain = value / 127f;
        }
    }

    public override int PreLowPass
    {
        get => _preLowPass;
        set
        {
            _preLowPass = value;
            // GS sure loves weird mappings, huh?
            // Maps to around 8000-300 Hz
            _preLPFfc = 8_000 * float.Pow(.63f, _preLowPass);
            var decay = float.Exp((-2 * MathF.PI * _preLPFfc) / _sampleRate);
            _preLPFa = 1 - decay;
        }
    }

    public override int Depth
    {
        get => _depth;
        set
        {
            _depth = value;
            _depthSamples = (value / 127f) * .025f * _sampleRate;
        }
    }
    
    public override int Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            _delaySamples = float.Max(1, (value / 127f) * .025f * _sampleRate);
        }
    }
    
    public override int Feedback
    {
        get => _feedback;
        set
        {
            _feedback = value;
            _feedbackGain = value * 0.007_63f;
        }
    }

    public override int Rate
    {
        get => _rate;
        set
        {
            _rate = value;
            // GS Advanced Editor actually specifies the rate!
            // 127 - 15.50Hz, 1 - 0.12 Hz
            // And GM2 section 4.5.2 actually specifies the equation!
            var rate = value * 0.122f;
            _rateInc = rate / _sampleRate;
        }
    }

    public override int Level
    {
        get => _level;
        set
        {
            const float chorusGain = 1.3f;
            _gain = (value / 127f) * chorusGain;
            _level = value;
        }
    }
    
    public override void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        Span<float> outputReverb,
        Span<float> outputDelay,
        int startIndex,
        int sampleCount)
    {
        var bufferL = _leftDelayBuffer.AsSpan();
        var bufferR = _rightDelayBuffer.AsSpan();
        var rateInc = _rateInc;
        var bufferLen = bufferL.Length;
        var depth = _depthSamples;
        var delay = _delaySamples;
        var gain = _gain;
        var reverbGain = _reverbGain;
        var delayGain = _delayGain;
        var feedback = _feedbackGain;

        var preLPF = _preLowPass > 0;
        var phase = _phase;
        var write = _write;
        var z = _preLPFz;
        var a = _preLPFa;

        for (var i = 0; i < sampleCount; i++)
        {
            var inputSample = input[i];
            // Pre lowpass filter
            if (preLPF)
            {
                z += a * (inputSample - z);
                inputSample = z;
            }

            // Triangle LFO (GS uses triangle)
            var lfo = 2 * float.Abs(phase - .5f);

            // Read position
            var dL = float.Clamp(delay + lfo * depth, 1, bufferLen);
            var readPosL = write - dL;
            if (readPosL < 0) readPosL = (int)float.Floor(readPosL) + bufferLen;

            // Linear interpolation
            var x0 = (int)readPosL;
            var x1 = x0 + 1;
            if (x1 >= bufferLen) x1 -= bufferLen;
            var frac = readPosL - x0;
            var outL = bufferL[x0] * (1 - frac) + bufferL[x1] * frac;
            
            // Write input sample
            bufferL[write] = inputSample + outL * feedback;

            // Same for the right line (shared buffer for now for testing)
            var dR = float.Clamp(1, delay + (1 - lfo) * depth, bufferLen);
            var readPosR = write - dR;
            if (readPosR < 0) readPosR = (int)float.Floor(readPosR) + bufferLen;

            // Linear interpolation
            x0 = (int)readPosR;
            x1 = x0 + 1;
            if (x1 >= bufferLen) x1 -= bufferLen;
            frac = readPosR - x0;
            var outR = bufferR[x0] * (1 - frac) + bufferR[x1] * frac;
            
            // Mix
            var o = i + startIndex;
            outputLeft[o] += outL * gain;
            outputRight[o] += outR * gain;
            
            var mono = (outL + outR) / 2;
            outputReverb[i] += mono * reverbGain;
            outputDelay[i] += mono * delayGain;

            // Write input sample and advance
            bufferR[write] = inputSample + outR * feedback;

            if (++write >= bufferLen) write = 0;
            if ((phase += rateInc) >= 1) phase -= 1;
        }

        _write = write;
        _phase = phase;
        _preLPFz = z;
    }

    public override Effect.ChorusProcessorSnapshot GetSnapshot() =>
        new()
        {
            PreLowPass          = _preLowPass,
            Depth               = _depth,
            Delay               = _delay,
            SendLevelToDelay    = _sendLevelToDelay,
            SendLevelToReverb   = _sendLevelToReverb,
            Rate                = _rate,
            Feedback            = _feedback,
            Level               = _level,
        };
}