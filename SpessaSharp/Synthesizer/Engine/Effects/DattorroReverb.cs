using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Effects;

/// <summary>
/// Dattorro Reverb Node by khoin on GitHub, public domain.<br/>
/// https://github.com/khoin/DattorroReverbNode/<br/>
/// Adapted for spessasynth by spessasus.<br/>
/// </summary>
internal sealed class DattorroReverb
{
    // Params

    /// <summary> Min: 0, max: sample rate - 1 </summary>
    public int PreDelay = 0;
    /// <summary> Min: 0, max: 1 </summary>
    public float PreLPF = .5f;
    /// <summary> Min: 0, max: 1 </summary>
    public float InputDiffusion1 = .75f;
    /// <summary> Min: 0, max: 1 </summary>
    public float InputDiffusion2 = .625f;
    /// <summary> Min: 0, max: 1 </summary>
    public float Decay = .5f;
    /// <summary> Min: 0, max: 0.999999 </summary>
    public float DecayDiffusion1 = .7f;
    /// <summary> Min: 0, max: 0.999999 </summary>
    public float DecayDiffusion2 = .5f;
    /// <summary> Min: 0, max: 1 </summary>
    public float Damping = .005f;
    /// <summary> Min: 0, max: 2 </summary>
    public float ExcursionRate = .1f;
    /// <summary> Min: 0, max: 2 </summary>
    public float ExcursionDepth = .2f;
    public float Gain = 1;

    private readonly int _sampleRate;
    private float _lp1 = 0;
    private float _lp2 = 0;
    private float _lp3 = 0;
    private float _excPhase = 0;
    private int _pDWrite = 0;

    private readonly short[] _taps;
    private readonly float[] _pDelay;
    private readonly int _pDLength;
    
    private readonly (float[], int, int, int)[] _delays;
    
    public DattorroReverb(int sampleRate)
    {
        _sampleRate = sampleRate;

        // Pre-delay is always one-second long
        _pDLength = sampleRate;
        _pDelay = new float[_pDLength];

        var delays = (ReadOnlySpan<float>)[
            0.004_771_345f, 0.003_595_309f, 0.012_734_787f, 0.009_307_483f,
            0.022_579_886f, 0.149_625_349f, 0.060_481_839f, 0.124_995_8f,
            0.030_509_727f, 0.141_695_508f, 0.089_244_313f, 0.106_280_031f
        ];

        _delays = new (float[], int, int, int)[delays.Length];
        for (var i = 0; i < delays.Length; i++)
        {
            // MakeDelayLine

            // Len, array, write, read, mask
            var len = Util.Round(delays[i] * _sampleRate);
            var nextPow2 = (int)BitOperations.RoundUpToPowerOf2((uint)len);
            _delays[i] = (new float[nextPow2], len - 1, 0, nextPow2 - 1);
        }

        var t = (ReadOnlySpan<float>)[
            0.008_937_872f, 0.099_929_438f, 0.064_278_754f, 0.067_067_639f,
            0.066_866_033f, 0.006_283_391f, 0.035_818_689f, 0.011_861_161f,
            0.121_870_905f, 0.041_262_054f, 0.089_815_53f, 0.070_931_756f,
            0.011_256_342f, 0.004_065_724f
        ];

        _taps = new short[t.Length];
        for (var i = 0; i < t.Length; i++)
            _taps[i] = (short)Util.Round(t[i] * _sampleRate);
    }

    /// <summary> Note: input is zero-based, while the outputs are startIndex based! </summary>
    /// <param name="input"></param>
    /// <param name="outputLeft"></param>
    /// <param name="outputRight"></param>
    /// <param name="startIndex"></param>
    /// <param name="sampleCount"></param>
    public void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        var pd = PreDelay;
        var fi = InputDiffusion1;
        var si = InputDiffusion2;
        var dc = Decay;
        var ft = DecayDiffusion1;
        var st = DecayDiffusion2;
        var dp = 1 - Damping;
        var ex = ExcursionRate / _sampleRate;
        var ed = (ExcursionDepth * _sampleRate) / 1_000f;
        var blockStart = _pDWrite;
        var pDelay = _pDelay.AsSpan();
        var delays = _delays.AsSpan();
        var taps = _taps.AsSpan();
        var gain = Gain;

        ref var pDelayRef = ref MemoryMarshal.GetReference(pDelay);
        ref var tapsRef = ref MemoryMarshal.GetReference(taps);
        ref var outputLeftRef = ref MemoryMarshal.GetReference(outputLeft);
        ref var outputRightRef = ref MemoryMarshal.GetReference(outputRight);
        
        // Write to predelay
        for (var j = 0; j < sampleCount; j++)
            pDelay[(blockStart + j) % _pDLength] = input[j];

        for (var i = 0; i < sampleCount; i++)
        {
            _lp1 += PreLPF *
                (Unsafe.Add(ref pDelayRef, (_pDLength + _pDWrite - pd + i) % _pDLength) - _lp1);
            
            // Pre-tank
            var pre = Write(delays, 0, _lp1 - fi * Read(delays, 0));
            pre = Write(delays, 1, fi * (pre - Read(delays, 1)) + Read(delays, 0));
            pre = Write(delays, 2, fi * pre + Read(delays, 1) - si * Read(delays, 2));
            pre = Write(delays, 3, si * (pre - Read(delays, 3)) + Read(delays, 2));

            var split = si * pre + Read(delays, 3);
            
            // Excursions
            // Could be optimized?
            var exc = ed * (1 + float.Cos(_excPhase * 6.28f));
            var exc2 = ed * (1 + float.Sin(_excPhase * 6.2847f));
            
            // Left loop
            var temp = Write(delays, 
                4, split + dc * Read(delays, 11) + ft * ReadCAt(delays, 4, exc)
            ); // Tank diffuse 1
            Write(delays, 5, ReadCAt(delays, 4, exc) - ft * temp); // Long delay 1
            _lp2 += dp * (Read(delays, 5) - _lp2); // Damp 1
            temp = Write(delays, 6, dc * _lp2 - st * Read(delays, 6)); // Tank diffuse 2
            Write(delays, 7, Read(delays, 6) + st * temp); // Long delay 2
            
            // Right loop
            temp = Write(delays, 
                8, split + dc * Read(delays, 7) + ft * ReadCAt(delays, 8, exc2)
            ); // Tank diffuse 3
            Write(delays, 9, ReadCAt(delays, 8, exc2) - ft * temp); // Long delay 3
            _lp3 += dp * (Read(delays, 9) - _lp3); // Damp 2
            temp = Write(delays, 10, dc * _lp3 - st * Read(delays, 10)); // Tank diffuse 4
            Write(delays, 11, Read(delays, 10) + st * temp); // Long delay 4
            
            // Mix down
            var leftSample =
                ReadAt(delays, 9, Unsafe.Add(ref tapsRef, 0)) +
                ReadAt(delays, 9, Unsafe.Add(ref tapsRef, 1)) -
                ReadAt(delays, 10, Unsafe.Add(ref tapsRef, 2)) +
                ReadAt(delays, 11, Unsafe.Add(ref tapsRef, 3)) -
                ReadAt(delays, 5, Unsafe.Add(ref tapsRef, 4)) -
                ReadAt(delays, 6, Unsafe.Add(ref tapsRef, 5)) -
                ReadAt(delays, 7, Unsafe.Add(ref tapsRef, 6));

            var idx = i + startIndex;
            Unsafe.Add(ref outputLeftRef, idx) += leftSample * gain;

            var rightSample =
                ReadAt(delays, 5, Unsafe.Add(ref tapsRef, 7)) +
                ReadAt(delays, 5, Unsafe.Add(ref tapsRef, 8)) -
                ReadAt(delays, 6, Unsafe.Add(ref tapsRef, 9)) +
                ReadAt(delays, 7, Unsafe.Add(ref tapsRef, 10)) -
                ReadAt(delays, 9, Unsafe.Add(ref tapsRef, 11)) -
                ReadAt(delays, 10, Unsafe.Add(ref tapsRef, 12)) -
                ReadAt(delays, 11, Unsafe.Add(ref tapsRef, 13));
            
            Unsafe.Add(ref outputRightRef, idx) += rightSample * gain;

            _excPhase += ex;

            // Advance delays
            foreach (ref var d in delays)
            {
                d.Item2 = (d.Item2 + 1) & d.Item4;
                d.Item3 = (d.Item3 + 1) & d.Item4;
            }
        }

        // Update preDelay index
        _pDWrite = (blockStart + sampleCount) % _pDLength;

        return;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float Write(
            Span<(float[], int, int, int)> delays, int index, float sample)
        {
            ref readonly var d = ref delays[index];
            return d.Item1[d.Item2] = sample;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float Read(
            ReadOnlySpan<(float[], int, int, int)> delays, int index)
        {
            ref readonly var d = ref delays[index];
            return d.Item1[d.Item3];
        }
        
        // Cubic interpolation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float ReadAt(
            ReadOnlySpan<(float[], int, int, int)> delays, int index, int i) 
        {
            ref readonly var delay = ref delays[index];
            return delay.Item1[(delay.Item3 + i) & delay.Item4];
        }
        
        // O. Niemitalo: https://www.musicdsp.org/en/latest/Other/49-cubic-interpollation.html
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float ReadCAt(
            ReadOnlySpan<(float[], int, int, int)> delays, int index, float i)
        {
            ref readonly var d = ref delays[index];
            ref var arr = ref MemoryMarshal.GetArrayDataReference(d.Item1);
            
            var frac = i - (int)i;
            var mask = d.Item4;
            var i0 = (int)i + d.Item3 - 1;

            var x0 = Unsafe.Add(ref arr, (i0 + 0) & mask);
            var x1 = Unsafe.Add(ref arr, (i0 + 1) & mask);
            var x2 = Unsafe.Add(ref arr, (i0 + 2) & mask);
            var x3 = Unsafe.Add(ref arr, (i0 + 3) & mask);

            var a = (3 * (x1 - x2) - x0 + x3)  * .5f;
            var b = 2 * x2 + x0 - (5 * x1 + x3) * .5f;
            var c = (x2 - x0) * .5f;

            return ((a * frac + b) * frac + c) * frac + x1;
        }
    }
}