namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

public sealed class PhAutoWahFX: Effect.InsertionProcessor
{
    private const int DEFAULT_LEVEL = 127;
    
    public override int Type => 0x11_08; // CHANGE THIS
    
    /// <summary>
    /// Sets the stereo location of the phaser sound. L63 is far left, 0
    /// is center, and R63 is far right.<br/>
    /// [0;127]
    /// </summary>
    private int _phPan = 0;

    /// <summary>
    /// Sets the stereo location of the aut-wah sound. L63 is far left, 0
    /// is center, and R63 is far right.<br/>
    /// [0;127]
    /// </summary>
    private int _awPan = 127;

    /// <summary>
    /// Adjusts the output level.<br/>
    /// [0;1]
    /// </summary>
    private float _level = DEFAULT_LEVEL / 127f;

    private readonly PhaserFX _phaser;
    private readonly AutoWahFX _autoWah;
    private readonly float[] _bufferPh;
    private readonly float[] _bufferAw;

    public PhAutoWahFX(int sampleRate, int maxBufferSize)
    {
        SendLevelToReverb = 40f / 127f;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
        
        _phaser = new PhaserFX(sampleRate);
        _autoWah = new AutoWahFX(sampleRate);
        _bufferPh = new float[maxBufferSize];
        _bufferAw = new float[maxBufferSize];

        _phaser.SendLevelToReverb = 0;
        _phaser.SendLevelToChorus = 0;
        _phaser.SendLevelToDelay = 0;
        _autoWah.SendLevelToReverb = 0;
        _autoWah.SendLevelToChorus = 0;
        _autoWah.SendLevelToDelay = 0;

        Reset();
    }
    
    public override void Reset()
    {
        _phPan = 0;
        _awPan = 127;
        _level = DEFAULT_LEVEL / 127f;
        _phaser.Reset();
        _autoWah.Reset();
        // Level
        _phaser.SetParameter(PhaserFX.Param.Level, 127);
        _autoWah.SetParameter(AutoWahFX.Param.Level, 127);
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
        
        var bufferPh = _bufferPh.AsSpan();
        var bufferAw = _bufferAw.AsSpan();
        
        // Process phaser
        _bufferPh.AsSpan().Clear();
        // Only takes input from left!
        _phaser.Process(
            inputLeft,
            inputLeft,
            bufferPh,
            bufferPh,
            bufferPh, // Level 0, ignored
            bufferPh, // Level 0, ignored
            bufferPh, // Level 0, ignored
            0,
            sampleCount);
        
        // Process auto wah
        _bufferAw.AsSpan().Clear();
        // Only takes input from right!
        _autoWah.Process(
            inputRight,
            inputRight,
            bufferAw,
            bufferAw,
            bufferAw, // Level 0, ignored
            bufferAw, // Level 0, ignored
            bufferAw, // Level 0, ignored
            0,
            sampleCount);
        
        var phPan = _phPan;
        var phL = InsUtil.PanTableLeft[phPan];
        var phR = InsUtil.PanTableRight[phPan];
        var awPan = _awPan;
        var awL = InsUtil.PanTableLeft[awPan];
        var awR = InsUtil.PanTableRight[awPan];
        
        for (var i = 0; i < sampleCount; i++) 
        {
            // Divide by 2 since processor mixes both left and right into it
            var outPhaser = bufferPh[i] * .5f * level;
            var outAutoWah = bufferAw[i] * .5f * level;

            // Pan
            var outL = outPhaser * phL + outAutoWah * awL;
            var outR = outPhaser * phR + outAutoWah * awR;

            // Mix
            var idx = startIndex + i;
            outputLeft[idx] += outL;
            outputRight[idx] += outR;
            var mono = (outL + outR) * .5f;
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }
    }

    public override void SetParameter(int parameter, int value)
    {
        switch (parameter)
        {
            case >= 0x03 and <= 0x07:
                _phaser.SetParameter(parameter, value);
                return;
            case >= 0x08 and <= 0x0e:
                _autoWah.SetParameter(parameter - 5, value);
                return;
            default:
            switch (parameter) 
            {
                default: break;

                case 0x12: 
                    _phPan = value;
                    break;

                case 0x13: 
                    _phaser.SetParameter(0x16, value);
                    break;

                case 0x14: 
                    _awPan = value;
                    break;

                case 0x15: 
                    _autoWah.SetParameter(0x16, value);
                    break;

                case 0x16: 
                    _level = value / 127f;
                    break;
            }

            break;
        }
    }
}