using System.Runtime.CompilerServices;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS;

public sealed class SSDelay: Effect.DelayProcessor
{
    /// <summary>
    /// SC-8850 manual p.236<br/>
    /// How nice of Roland to provide the conversion values to ms!
    /// </summary>
    /// <param name="Start"></param>
    /// <param name="End"></param>
    /// <param name="TimeStart"></param>
    /// <param name="Resolution"></param>
    private readonly record struct DelayTimeSegment(
        int Start, int End, float TimeStart, float Resolution);
    
    private static readonly DelayTimeSegment[] DelayTimeSegments = [
        new (Start: 0x01, End: 0x14, TimeStart: .1f,    Resolution: .1f),
        new (Start: 0x14, End: 0x23, TimeStart: 2,      Resolution: .2f),
        new (Start: 0x23, End: 0x2d, TimeStart: 5,      Resolution: .5f),
        new (Start: 0x2d, End: 0x37, TimeStart: 10,     Resolution: 1),
        new (Start: 0x37, End: 0x46, TimeStart: 20,     Resolution: 2),
        new (Start: 0x46, End: 0x50, TimeStart: 50,     Resolution: 5),
        new (Start: 0x50, End: 0x5a, TimeStart: 100,    Resolution: 10),
        new (Start: 0x5a, End: 0x69, TimeStart: 200,    Resolution: 20),
        new (Start: 0x69, End: 0x74, TimeStart: 500,    Resolution: 50),
    ];
    
    /// <summary> Cutoff frequency </summary>
    private float _preLPFfc = 8_000f;
    /// <summary> Alpha </summary>
    private float _preLPFa = 0f;
    /// <summary> Previous value </summary>
    private float _preLPFz = 0f;

    private readonly float[] _buffer;
    private readonly int _sampleRate;
    private readonly float[] _delayPreLPF;
    private float _delayLeftMultiplier = .04f;
    private float _delayRightMultiplier = .04f;
    private float _gain = 0;
    private float _reverbGain = 0;
    private float _feedbackGain = 0;

    /// <summary>Samples</summary>
    private int _delayCenter;
    /// <summary>Samples</summary>
    private int _delayLeft;
    /// <summary>Samples</summary>
    private int _delayRight;

    private float _gainCenter = 1;
    private float _gainLeft = 0;
    private float _gainRight = 0;
    private int _writeIndex = 0;

    private int _sendLevelToReverb = 0;
    private int _preLowPass = 0;
    private int _level = 0;
    private int _levelRight = 0;
    private int _levelLeft = 0;
    private int _levelCenter = 127;
    private int _feedback = 16;
    private int _timeRatioRight = 0;
    private int _timeRatioLeft = 0;
    private int _timeCenter = 12;

    public SSDelay(int sampleRate, int maxBufferSize)
    {
        _sampleRate = sampleRate;
        _buffer = new float[sampleRate];
        _delayPreLPF = new float[maxBufferSize];

        // All delays are capped at 1s
        _delayCenter = (int)float.Floor(.34f * sampleRate);
        _delayLeft = (int)float.Floor(_delayCenter * .04f);
        _delayRight = (int)float.Floor(_delayCenter * .04f);
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

    public override int PreLowPass
    {
        get => _preLowPass;
        set
        {
            _preLowPass = value;
            // GS sure loves weird mappings, huh?
            // Maps to around 8000-300 Hz
            _preLPFfc = 8_000f * float.Pow(.63f, _preLowPass);
            var decay = float.Exp((-2 * MathF.PI * _preLPFfc) / _sampleRate);
            _preLPFa = 1 - decay;
        }
    }
    
    public override int LevelRight
    {
        get => _levelRight; 
        set
        {
            _levelRight = value;
            UpdateGain();
        }
    }
    
    public override int Level
    {
        get => _level;
        set
        {
            const float delayGain = 1.66f;
            _level = value;
            _gain = (value / 127f) * delayGain;
        }
    }
    
    public override int LevelCenter
    {
        get => _levelCenter;
        set
        {
            _levelCenter = value;
            UpdateGain();
        }
    }
    
    public override int LevelLeft
    {
        get => _levelLeft; 
        set
        {
            _levelLeft = value;
            UpdateGain();
        }
    }

    public override int Feedback
    {
        get => _feedback;
        set
        {
            // -64 means max at inverted phase
            // Use 66 for it to not be infinite (-1)
            _feedbackGain = (value - 64) / 66f;
            _feedback = value;
        }
    }

    public override int TimeRatioRight
    {
        get => _timeRatioRight;
        set
        {
            _timeRatioRight = value;
            // DELAY TIME RATIO LEFT and DELAY TIME RATIO RIGHT specify the ratio in relation to DELAY TIME CENTER.
            // The resolution is 100/24(%).
            // Turn that into multiplier
            _delayRightMultiplier = value * (100f / 2_400f);
        }
    }
    
    public override int TimeRatioLeft
    {
        get => _timeRatioLeft;
        set
        {
            _timeRatioLeft = value;
            // DELAY TIME RATIO LEFT and DELAY TIME RATIO RIGHT specify the ratio in relation to DELAY TIME CENTER.
            // The resolution is 100/24(%).
            // Turn that into multiplier
            _delayLeftMultiplier = value * (100f / 2_400f);
        }
    }
    
    public override int TimeCenter
    {
        get => _timeCenter;
        set
        {
            _timeCenter = value;
            
            var delayMs = .1f;
            foreach (ref readonly var segment in DelayTimeSegments.AsSpan())
            {
                if (value < segment.Start || value >= segment.End) continue;
                delayMs =
                    segment.TimeStart +
                    (value - segment.Start) * segment.Resolution;
                break;
            }

            _delayCenter = (int)float.Floor(
                Math.Max(2, _sampleRate * (delayMs / 1_000f)));
            _delayLeft = (int)float.Floor(
                _delayCenter * _delayLeftMultiplier);
            _delayRight = (int)float.Floor(
                _delayCenter * _delayRightMultiplier);
            _buffer.AsSpan().Clear();
        }
    }
    
    /// <summary>Process the effect and ADDS it to the output.</summary>
    /// <param name="input">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    public override void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        Span<float> outputReverb,
        int startIndex,
        int sampleCount)
    {
        // Process pre-lowpass
        ReadOnlySpan<float> delayIn;
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
        else
            delayIn = input;
        
        /*
        Connections are:
        Input connects to all delays,
        center connects to both output and stereo delays,
        stereo delays only connect to the output.
        Also level is separate from reverb send level,
        i.e. level = 0 and reverb send level = 127 will still send sound to reverb.
        
        Center always sends to stereo, regardless of level center in hardware and latest SCVA, older revisions incorrectly don't send it,
        So level center = 0, level left = 127 will still have feedback.
        Also feedback time is always time center, even if only left delay is playing.
        */
        var (gain, reverbGain,
            delayCenter, delayLeft, delayRight, feedbackGain) =
            (_gain, _reverbGain,
            _delayCenter, _delayLeft, _delayRight, _feedback);
        var buffer = _buffer.AsSpan();

        var writeIndex = _writeIndex;
        var bufferLength = buffer.Length;
        var centerGain = _gainCenter * gain;
        var leftGain = _gainLeft * gain;
        var rightGain = _gainRight * gain;

        for (var i = 0; i < sampleCount; i++)
        {
            // Read center
            var centerReadIndex = writeIndex - delayCenter;
            if (centerReadIndex < 0) centerReadIndex += bufferLength;

            // Read left
            var leftReadIndex = (writeIndex - delayLeft) % bufferLength;
            if (leftReadIndex < 0) leftReadIndex += bufferLength;

            // Read right
            var rightReadIndex = (writeIndex - delayRight) % bufferLength;
            if (rightReadIndex < 0) rightReadIndex += bufferLength;

            // Write center
            var o = startIndex + i;
            var delayed = buffer[centerReadIndex];
            var c = delayed * centerGain;
            outputLeft[o] += c;
            outputRight[o] += c;
            outputReverb[o] += c * reverbGain;
            
            // Center feedback, do it first so left and right delay of 0 work fine
            // Testcase: gs_effect_send_level_test
            buffer[writeIndex] = delayIn[i] + delayed * feedbackGain;

            // Write left
            var l = buffer[leftReadIndex] * leftGain;
            outputLeft[o] += l;
            outputReverb[o] += l * reverbGain;

            // Write right
            var r = buffer[rightReadIndex] * rightGain;
            outputRight[o] += r;
            outputReverb[o] += r * reverbGain;

            // Advance and wrap
            if (++writeIndex >= bufferLength) writeIndex = 0;
        }
        
        _writeIndex = writeIndex;
    }

    public override Effect.DelayProcessorSnapshot GetSnapshot() =>
        new()
        {
            Level =  _level,
            PreLowPass =  _preLowPass,
            TimeCenter =  _timeCenter,
            TimeRatioRight = _timeRatioRight,
            TimeRatioLeft = _timeRatioLeft,
            LevelCenter =  _levelCenter,
            LevelLeft =  _levelLeft,
            LevelRight =  _levelRight,
            Feedback =  _feedback,
            SendLevelToReverb =  _sendLevelToReverb,
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateGain()
    {
        // Center gain is applied in post
        _gainCenter = _levelCenter / 127f;
        _gainLeft = _levelLeft / 127f;
        _gainRight = _levelRight / 127f;
    }
}