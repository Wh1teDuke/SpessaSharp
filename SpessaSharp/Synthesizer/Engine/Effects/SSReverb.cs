namespace SpessaSharp.Synthesizer.Engine.Effects;

public sealed class SSReverb: Effect.ReverbProcessor
{
    /// <summary> Dattorro reverb processor. </summary>
    private readonly DattorroReverb _dattorro;
    
    /// <summary>  Left delay line, also used for the mono delay. (character 6) </summary>
    private readonly SSDelay.Line _delayLeft;
    
    /// <summary>  Right delay line. </summary>
    private readonly SSDelay.Line _delayRight;
    
    /// <summary>  Output of the left (and mono) delay. </summary>
    private readonly float[] _delayLeftOutput;

    /// <summary>  Output of the right delay. </summary>
    private readonly float[] _delayRightOutput;

    /// <summary>  Input into the left delay. Mixed dry input and right output. </summary>
    private readonly float[] _delayLeftInput;

    /// <summary>  Pre LPF buffer for the delay characters. </summary>
    private readonly float[] _delayPreLPF;
    
    /// <summary>  Sample rate of the processor. </summary>
    private readonly int _sampleRate;
    
    /// <summary>  Cutoff frequency </summary>
    private float _preLPFfc = 8_000f;
    
    /// <summary>  Alpha </summary>
    private float _preLPFa = 0;
    
    /// <summary>  Previous value </summary>
    private float _preLPFz = 0;
    
    /// <summary>  Reverb time coefficient for different reverb characters. </summary>
    private float _characterTimeCoefficient = 1;
    
    /// <summary>  Reverb gain coefficient for different reverb characters. </summary>
    private float _characterGainCoefficient = 1;
    
    /// <summary> Reverb pre-lowpass coefficient for different reverb characters.</summary>
    private float _characterLPFCoefficient = 0;

    /// <summary> Gain for the delay output.</summary>
    private float _delayGain = 1;

    /// <summary> Panning delay feedback gain (from the right to the left delay).</summary>
    private float _panDelayFeedback = 0;
        
    private int _delayFeedback = 0;
    private int _character = 0;
    private int _time = 0;
    private int _preDelayTime = 0;
    private int _level = 0;
    private int _preLowPass = 0;

    public SSReverb(int sampleRate, int maxBufferSize)
    {
        _sampleRate = sampleRate;
        _delayLeftOutput = NewFloatArray();
        _delayRightOutput = NewFloatArray();
        _delayLeftInput = NewFloatArray();
        _delayPreLPF = NewFloatArray();
        
        _dattorro = new DattorroReverb(sampleRate);

        _delayLeft = new SSDelay.Line(sampleRate);
        _delayRight = new SSDelay.Line(sampleRate);

        return;
        
        float[] NewFloatArray() => new float[maxBufferSize];
    }

    public override int DelayFeedback
    {
        get => _delayFeedback;
        set
        {
            _delayFeedback = value;
            UpdateFeedback();
        }
    }

    public override int Character
    {
        get => _character;
        set
        {
            _character = value;

            _dattorro.Damping = .005f;

            _characterTimeCoefficient = 1;
            _characterGainCoefficient = 1;
            _characterLPFCoefficient = 0;

            _dattorro.InputDiffusion1 = .75f;
            _dattorro.InputDiffusion2 = .625f;
            _dattorro.DecayDiffusion1 = .7f;
            _dattorro.DecayDiffusion2 = .5f;
            _dattorro.ExcursionRate = .5f;
            _dattorro.ExcursionDepth = .7f;

            // Tested all characters on level = 64, preset: Hall2
            // File: gs_reverb_character_test.ts, compare spessasynth to SC-VA
            // Tuned by me, though I'm not very good at it :-)
            switch (value) 
            {
                case 0: 
                    // Room1
                    _dattorro.Damping = .85f;
                    _characterTimeCoefficient = .9f;
                    _characterGainCoefficient = .9f;
                    _characterLPFCoefficient = .2f;
                    break;

                case 1: 
                    // Room2
                    _dattorro.Damping = .2f;
                    _characterGainCoefficient = .7f;
                    _characterTimeCoefficient = 1f;
                    _dattorro.DecayDiffusion2 = .64f;
                    _dattorro.DecayDiffusion1 = .6f;
                    _characterLPFCoefficient = .2f;
                    break;

                case 2: 
                    // Room3
                    _dattorro.Damping = .56f;
                    _characterGainCoefficient = .75f;
                    _characterTimeCoefficient = 1f;
                    _dattorro.DecayDiffusion2 = .64f;
                    _dattorro.DecayDiffusion1 = .6f;
                    _characterLPFCoefficient = .1f;
                    break;

                case 3: 
                    // Hall1
                    _dattorro.Damping = .3f;
                    _characterGainCoefficient = 1.25f;
                    _characterTimeCoefficient = 1.3f;
                    _characterLPFCoefficient = 0;
                    _dattorro.DecayDiffusion2 = .7f;
                    _dattorro.DecayDiffusion1 = .66f;
                    break;

                case 4: 
                    // Hall2
                    _characterGainCoefficient = 1f;
                    _characterTimeCoefficient = 1.2f;
                    _characterLPFCoefficient = .1f;
                    _dattorro.Damping = .1f;
                    _dattorro.DecayDiffusion2 = .69f;
                    _dattorro.DecayDiffusion1 = .67f;
                    
                    break;

                case 5: 
                    // Plate
                    _characterGainCoefficient = .75f;
                    _dattorro.Damping = .65f;
                    _characterTimeCoefficient = .5f;
                    break;
            }

            // Update values
            UpdateTime();
            UpdateGain();
            UpdateLowPass();
            UpdateFeedback();
            _delayLeft.Clear();
            _delayRight.Clear();
        }
    }
    
    public override int Time
    {
        get => _time;
        set
        {
            _time = value;
            UpdateTime();
        }
    }

    public override int PreDelayTime
    {
        get => _preDelayTime;
        set
        {
            _preDelayTime = value;
            // Predelay is 0-127 ms
            _dattorro.PreDelay = (int)((value / 1_000f) * _sampleRate);
        }
    }

    public override int Level
    {
        get => _level;
        set
        {
            _level = value;
            UpdateGain();
        }
    }

    public override int PreLowPass
    {
        get => _preLowPass;
        set
        {
            _preLowPass = value;
            // Maps to around 8000-300 Hz
            _preLPFfc = 8_000f * float.Pow(.63f, _preLowPass);
            var decay = float.Exp((-2 * MathF.PI * _preLPFfc) / _sampleRate);
            _preLPFa = 1 - decay;
            UpdateLowPass();
        }
    }

    /// <summary> </summary>
    /// <param name="input">0-based</param>
    /// <param name="outputLeft">startIndex-based</param>
    /// <param name="outputRight">startIndex-based</param>
    /// <param name="startIndex"></param>
    /// <param name="sampleCount"></param>
    public override void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        switch (_character)
        {
            default:
                // Reverb
                _dattorro.Process(
                    input,
                    outputLeft,
                    outputRight,
                    startIndex,
                    sampleCount
                );
                return;

            case 6:
            {
                // Delay
                // Process pre-lowpass
                ReadOnlySpan<float> delayIn = null;

                if (_preLowPass > 0)
                {
                    var preLPF = _delayPreLPF;
                    var z = _preLPFz;
                    var a = _preLPFa;
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var x = input[i];
                        z += a * (x - z);
                        preLPF[i] = z;
                    }

                    _preLPFz = z;
                    delayIn = preLPF;
                }
                else delayIn = input;

                // Process delay
                _delayLeft.Process(delayIn, _delayLeftOutput, sampleCount);

                // Mix down
                var g = _delayGain;
                var delay = _delayLeftOutput.AsSpan();
                for (int i = 0, o = startIndex; i < sampleCount; i++, o++)
                {
                    var sample = delay[i] * g;
                    outputRight[o] += sample;
                    outputLeft[o] += sample;
                }

                return;
            }

            case 7:
            {
                // Panning Delay
                // Process pre-lowpass
                ReadOnlySpan<float> delayIn = null;

                if (_preLowPass > 0)
                {
                    var preLPF = _delayPreLPF;
                    var z = _preLPFz;
                    var a = _preLPFa;
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var x = input[i];
                        z += a * (x - z);
                        preLPF[i] = z;
                    }

                    _preLPFz = z;
                    delayIn = preLPF;
                }
                else delayIn = input;

                // Mix right into left
                var fb = _panDelayFeedback;
                var delayLeftInput = _delayLeftInput.AsSpan();
                var delayLeftOutput = _delayLeftOutput.AsSpan();
                var delayRightOutput = _delayRightOutput.AsSpan();

                for (var i = 0; i < sampleCount; i++)
                    delayLeftInput[i] = delayIn[i] + delayRightOutput[i] * fb;

                // Process left
                _delayLeft.Process(
                    delayLeftInput, delayLeftOutput, sampleCount);

                // Process right
                _delayRight.Process(
                    delayLeftOutput, delayRightOutput, sampleCount);

                // Mix
                var g = _delayGain;
                for (int i = 0, o = startIndex; i < sampleCount; i++, o++)
                {
                    outputLeft[o] += delayLeftOutput[i] * g;
                    outputRight[o] += delayRightOutput[i] * g;
                }

                return;
            }
        }
    }

    public override Effect.ReverbProcessorSnapshot GetSnapshot() =>
        new()
        {
            Level           = _level,
            PreLowPass      = _preLowPass,
            Character       = _character,
            Time            = _time,
            DelayFeedback   = _delayFeedback,
            PreDelayTime    = _preDelayTime,
        };

    private void UpdateFeedback()
    {
        // Logarithmic time it seems
        // It gets way higher the closer you get to 127
        var x = _delayFeedback / 127f;
        var exp = 1 - float.Pow(1 - x, 1.9f);
        if (_character == 6)
            _delayLeft.Feedback = exp * .73f;
        else
        {
            _delayLeft.Feedback = _delayRight.Feedback = 0;
            _panDelayFeedback = exp * .73f;
        }
    }
    
    private void UpdateLowPass() =>
        _dattorro.PreLPF = float.Min(
            1, .1f + (7f - _preLowPass) / 14f + _characterLPFCoefficient);

    private void UpdateGain()
    {
        const float delayGain = 1.5f;
        _dattorro.Gain =
            (_level / 345f) * _characterGainCoefficient;
        // SC-VA: Delay seems to be quite loud
        _delayGain = (_level / 127f) * delayGain;
    }

    private void UpdateTime()
    {
        var t = _time / 127f;
        _dattorro.Decay = _characterTimeCoefficient * (.05f + .65f * t);

        // Delay at 127 is exactly 0.4468 seconds
        // The minimum value (delay 0) seems to be 21 samples
        var timeSamples = Math.Max(21, (int)(t * _sampleRate * .4468f));

        if (_character == 7)
            // Half the delay time
            _delayRight.Time = 
                _delayLeft.Time = (int)float.Floor(timeSamples / 2f);
        else
            // Delay left is used as the main delay
            _delayLeft.Time = timeSamples;
    }
}