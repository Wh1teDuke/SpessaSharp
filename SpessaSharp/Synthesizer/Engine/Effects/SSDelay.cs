using System.Runtime.CompilerServices;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Effects;

public sealed class SSDelay: Effect.DelayProcessor
{
    public sealed class Line
    {
        public float Feedback = 0;
        public float Gain = 1;

        private readonly float[] _buffer;
        private readonly int _bufferLen;
        private int _writeIndex = 0;
        
        // Samples
        private int _time;

        public Line(int maxDelay)
        {
            _buffer = new float[maxDelay];
            _bufferLen = _buffer.Length;
            _time = maxDelay - 5;
        }

        public int Time
        {
            get => _time;
            set => _time = Math.Min(value, _bufferLen);
        }

        public void Clear() => _buffer.AsSpan().Clear();
        
        /// <summary> OVERWRITES the output </summary>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <param name="sampleCount"></param>
        public void Process(
            ReadOnlySpan<float> input, Span<float> output, int sampleCount) 
        {
            var writeIndex = _writeIndex;
            var delay = _time;
            var buffer = _buffer.AsSpan();
            var bufferLength = _bufferLen;
            var feedback = Feedback;
            var gain = Gain;

            for (var i = 0; i < sampleCount; i++) 
            {
                // Read
                var readIndex = writeIndex - delay;
                if (readIndex < 0) readIndex += bufferLength;
                var delayed = buffer[readIndex];
                output[i] = delayed * gain;

                // Write
                buffer[writeIndex] = input[i] + delayed * feedback;

                // Then wrap!
                if (++writeIndex >= bufferLength) writeIndex = 0;
            }

            _writeIndex = writeIndex;
        }
    }
    
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
    private readonly Line _delayLeft;
    private readonly Line _delayRight;
    private readonly Line _delayCenter;
    private readonly int _sampleRate;
    private readonly float[] _delayCenterOutput;
    private readonly float[] _delayPreLPF;
    private float _delayCenterTime;
    private float _delayLeftMultiplier = .04f;
    private float _delayRightMultiplier = .04f;
    private float _gain = 0;
    private float _reverbGain = 0;

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
        _delayCenterOutput = new float[maxBufferSize];
        _delayPreLPF = new float[maxBufferSize];
        _delayCenterTime = .34f * sampleRate;

        // All delays are capped at 1s
        _delayCenter = new Line(sampleRate);
        _delayLeft = new Line(sampleRate);
        _delayRight = new Line(sampleRate);
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
            _feedback = value;
            // Only the center delay has feedback
            _delayLeft.Feedback = _delayRight.Feedback = 0;
            // -64 means max at inverted phase, so feedback of -1 it is!
            // Use 66 for it to not be infinite
            _delayCenter.Feedback = (value - 64f) / 66f;
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
            _delayRight.Time = Util.Round(_delayCenterTime * _delayRightMultiplier);
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
            _delayLeft.Time = Util.Round(_delayCenterTime * _delayLeftMultiplier);
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

            _delayCenterTime = float.Max(2, _sampleRate * (delayMs / 1_000f));
            _delayCenter.Time = Util.Round(_delayCenterTime);
            _delayLeft.Time = Util.Round(_delayCenterTime * _delayLeftMultiplier);
            _delayRight.Time = Util.Round(_delayCenterTime * _delayRightMultiplier);
        }
    }
    
    public override void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        Span<float> outputReverb,
        int startIndex,
        int sampleCount)
    {
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
        else
            delayIn = input;
        
        /*
        Connections are:
        Input connects to all delays,
        center connects to both output and stereo delays,
        stereo delays only connect to the output.
        Also level is separate from reverb send level,
        i.e. level = 0 and reverb send level = 127 will still send sound to reverb.
         */
        var (gain, reverbGain) = (_gain, _reverbGain);

        // Process center first
        _delayCenter.Process(delayIn, _delayCenterOutput, sampleCount);

        // Mix into output
        var center = _delayCenterOutput.AsSpan();
        for (int i = 0, o = startIndex; i < sampleCount; i++, o++) 
        {
            var sample = center[i];
            outputReverb[i] += sample * reverbGain;
            var outSample = sample * gain;
            outputLeft[o] += outSample;
            outputRight[o] += outSample;
        }
        
        // Add input into delay (stereo delays take input from both)
        for (var i = 0; i < sampleCount; i++)
            center[i] += input[i];

        // Process stereo delays (reuse preLPF array as delays overwrite samples)
        var stereoOut = _delayPreLPF.AsSpan();
        // Left
        _delayLeft.Process(center, stereoOut, sampleCount);
        for (int i = 0, o = startIndex; i < sampleCount; i++, o++) 
        {
            var sample = stereoOut[i];
            outputLeft[o] += sample * gain;
            outputReverb[i] += sample * reverbGain;
        }
        // Right
        _delayRight.Process(center, stereoOut, sampleCount);
        for (int i = 0, o = startIndex; i < sampleCount; i++, o++) 
        {
            var sample = stereoOut[i];
            outputRight[o] += sample * gain;
            outputReverb[i] += sample * reverbGain;
        }
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
        _delayCenter.Gain = _levelCenter / 127f;
        _delayLeft.Gain = _levelLeft / 127f;
        _delayRight.Gain = _levelRight / 127f;
    }
}