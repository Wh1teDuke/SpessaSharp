using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Effects.Implementation;

/// <summary>
/// Dattorro Reverb Node by khoin on GitHub, public domain.<br/>
/// https://github.com/khoin/DattorroReverbNode/<br/>
/// Adapted for spessasynth by spessasus.<br/>
/// Further optimized with micro optimizations, check tsx performance_test before changing<br/>
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
    private float _lp1;
    private float _lp2;
    private float _lp3;
    
    // Separate lfo phases to allow safe wrapping at 2pi
    private float _excPhase1;
    private float _excPhase2;
    
    private int _pDWrite;

    private readonly short[] _taps;
    private readonly float[] _pDelay;
    private readonly int _pDMask;
    
    // Flattened delays compared to original
    private readonly float[][] _delayBuffers = new float[12][];
    private readonly int[] _delayWrite = new int[12];
    private readonly int[] _delayRead = new int[12];
    private readonly int[] _delayMask = new int[12];
    
    public DattorroReverb(int sampleRate)
    {
        _sampleRate = sampleRate;

        // Pre-delay is always one-second long
        _pDMask = (int)BitOperations.RoundUpToPowerOf2((uint)sampleRate) - 1;
        _pDelay = new float[_pDMask + 1];

        var delays = (ReadOnlySpan<float>)[
            0.004_771_345f, 0.003_595_309f, 0.012_734_787f, 0.009_307_483f,
            0.022_579_886f, 0.149_625_349f, 0.060_481_839f, 0.124_995_8f,
            0.030_509_727f, 0.141_695_508f, 0.089_244_313f, 0.106_280_031f
        ];

        for (var i = 0; i < delays.Length; i++)
        {
            // MakeDelayLine
            // Len, array, write, read, mask
            var len = Util.Round(delays[i] * _sampleRate);
            var nextPow2 = (int)BitOperations.RoundUpToPowerOf2((uint)len);
            _delayBuffers[i] = new float[nextPow2];
            _delayWrite[i] = len - 1;
            _delayRead[i] = 0;
            _delayMask[i] = nextPow2 - 1;
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

    /// <summary>
    /// Note: input is zero-based, while the outputs are startIndex based!
    /// ADDS to the output
    /// </summary>
    public void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        // Cache everything we can
        var pd = PreDelay;
        var fi = InputDiffusion1;
        var si = InputDiffusion2;
        var dc = Decay;
        var ft = DecayDiffusion1;
        var st = DecayDiffusion2;
        var dp = 1 - Damping;
        var ex = ExcursionRate / _sampleRate;
        var ed = (ExcursionDepth * _sampleRate) / 1_000f;

        var (lp1, lp2, lp3) = (_lp1, _lp2, _lp3);
        var (p1, p2) = (_excPhase1, _excPhase2);
        
        var blockStart = _pDWrite;
        var pDelay = _pDelay.AsSpan();
        var pDMask = _pDMask;
        var taps = _taps.AsSpan();
        var gain = Gain;
        
        // Cache array accesses too
        var d0 = _delayBuffers[0].AsSpan();
        var w0 = _delayWrite[0];
        var r0 = _delayRead[0];
        var m0 = _delayMask[0];
        var d1 = _delayBuffers[1].AsSpan();
        var w1 = _delayWrite[1];
        var r1 = _delayRead[1];
        var m1 = _delayMask[1];
        var d2 = _delayBuffers[2].AsSpan();
        var w2 = _delayWrite[2];
        var r2 = _delayRead[2];
        var m2 = _delayMask[2];
        var d3 = _delayBuffers[3].AsSpan();
        var w3 = _delayWrite[3];
        var r3 = _delayRead[3];
        var m3 = _delayMask[3];
        var d4 = _delayBuffers[4].AsSpan();
        var w4 = _delayWrite[4];
        var r4 = _delayRead[4];
        var m4 = _delayMask[4];
        var d5 = _delayBuffers[5].AsSpan();
        var w5 = _delayWrite[5];
        var r5 = _delayRead[5];
        var m5 = _delayMask[5];
        var d6 = _delayBuffers[6].AsSpan();
        var w6 = _delayWrite[6];
        var r6 = _delayRead[6];
        var m6 = _delayMask[6];
        var d7 = _delayBuffers[7].AsSpan();
        var w7 = _delayWrite[7];
        var r7 = _delayRead[7];
        var m7 = _delayMask[7];
        var d8 = _delayBuffers[8].AsSpan();
        var w8 = _delayWrite[8];
        var r8 = _delayRead[8];
        var m8 = _delayMask[8];
        var d9 = _delayBuffers[9].AsSpan();
        var w9 = _delayWrite[9];
        var r9 = _delayRead[9];
        var m9 = _delayMask[9];
        var d10 = _delayBuffers[10].AsSpan();
        var w10 = _delayWrite[10];
        var r10 = _delayRead[10];
        var m10 = _delayMask[10];
        var d11 = _delayBuffers[11].AsSpan();
        var w11 = _delayWrite[11];
        var r11 = _delayRead[11];
        var m11 = _delayMask[11];

        const float TWO_PI = 6.283_185_307_179_586f;

        for (var i = 0; i < sampleCount; i++)
        {
            // Write/read predelay
            pDelay[(blockStart + i) & pDMask] = input[i];
            var inSample = pDelay[(blockStart + i - pd) & pDMask];

            // Lowpass filter
            lp1 += PreLPF * (inSample - lp1);
            
            // Pre-tank
            var read0 = d0[r0];
            var pre = lp1 - fi * read0;
            d0[w0] = pre;

            var read1 = d1[r1];
            pre = fi * (pre - read1) + read0;
            d1[w1] = pre;

            var read2 = d2[r2];
            pre = fi * pre + read1 - si * read2;
            d2[w2] = pre;

            var read3 = d3[r3];
            pre = si * (pre - read3) + read2;
            d3[w3] = pre;

            var split = si * pre + read3;
            
            // Excursions
            // Could be optimized?
            var exc = ed * (1 + float.Cos(p1));
            var exc2 = ed * (1 + float.Sin(p2));

            // Left loop
            var read11 = d11[r11];

            // Inlined readDelayCAt(4, exc)
            var f4 = exc - (int)exc;
            var i4 = (int)exc + r4 - 1;
            var x4_0 = d4[i4++ & m4];
            var x4_1 = d4[i4++ & m4];
            var x4_2 = d4[i4++ & m4];
            var x4_3 = d4[i4 & m4];
            var a4 = (3 * (x4_1 - x4_2) - x4_0 + x4_3) * 0.5f;
            var b4 = 2 * x4_2 + x4_0 - (5 * x4_1 + x4_3) * 0.5f;
            var c4 = (x4_2 - x4_0) * 0.5f;
            var readC4 = ((a4 * f4 + b4) * f4 + c4) * f4 + x4_1;
            // End of inline

            var temp = split + dc * read11 + ft * readC4;
            d4[w4] = temp; // Tank diffuse 1

            d5[w5] = readC4 - ft * temp; // Long delay 1

            var read5 = d5[r5];
            lp2 += dp * (read5 - lp2); // Damp 1

            var read6 = d6[r6];
            temp = dc * lp2 - st * read6;
            d6[w6] = temp; // Tank diffuse 2

            d7[w7] = read6 + st * temp; // Long delay 2
            
            // Right loop
            var read7 = d7[r7];

            // Inline readDelayCAt(8, exc2)
            var f8 = exc2 - (int)exc2;
            var i8 = (int)exc2 + r8 - 1;
            var x8_0 = d8[i8++ & m8];
            var x8_1 = d8[i8++ & m8];
            var x8_2 = d8[i8++ & m8];
            var x8_3 = d8[i8 & m8];
            var a8 = (3 * (x8_1 - x8_2) - x8_0 + x8_3) * 0.5f;
            var b8 = 2 * x8_2 + x8_0 - (5 * x8_1 + x8_3) * 0.5f;
            var c8 = (x8_2 - x8_0) * 0.5f;
            var readC8 = ((a8 * f8 + b8) * f8 + c8) * f8 + x8_1;
            // End of inline

            temp = split + dc * read7 + ft * readC8;
            d8[w8] = temp; // Tank diffuse 3

            d9[w9] = readC8 - ft * temp; // Long delay 3

            var read9 = d9[r9];
            lp3 += dp * (read9 - lp3); // Damp 2

            var read10 = d10[r10];
            temp = dc * lp3 - st * read10;
            d10[w10] = temp; // Tank diffuse 4

            d11[w11] = read10 + st * temp; // Long delay 4
            
            // Mix down
            var leftSample =
                d9[(r9 + taps[0]) & m9] +
                d9[(r9 + taps[1]) & m9] -
                d10[(r10 + taps[2]) & m10] +
                d11[(r11 + taps[3]) & m11] -
                d5[(r5 + taps[4]) & m5] -
                d6[(r6 + taps[5]) & m6] -
                d7[(r7 + taps[6]) & m7];
            
            var rightSample =
                d5[(r5 + taps[7]) & m5] +
                d5[(r5 + taps[8]) & m5] -
                d6[(r6 + taps[9]) & m6] +
                d7[(r7 + taps[10]) & m7] -
                d9[(r9 + taps[11]) & m9] -
                d10[(r10 + taps[12]) & m10] -
                d11[(r11 + taps[13]) & m11];
            
            // Write out
            var idx = i + startIndex;
            outputLeft[idx] += leftSample * gain;
            outputRight[idx] += rightSample * gain;
            
            // Update LFOs and wrap them
            // Different values for stereo effect
            p1 += ex * TWO_PI;
            p2 += ex * 6.2847f;
            if (p1 > TWO_PI) p1 -= TWO_PI;
            if (p2 > TWO_PI) p2 -= TWO_PI;
            
            // Advance delays
            w0 = (w0 + 1) & m0;
            r0 = (r0 + 1) & m0;
            w1 = (w1 + 1) & m1;
            r1 = (r1 + 1) & m1;
            w2 = (w2 + 1) & m2;
            r2 = (r2 + 1) & m2;
            w3 = (w3 + 1) & m3;
            r3 = (r3 + 1) & m3;
            w4 = (w4 + 1) & m4;
            r4 = (r4 + 1) & m4;
            w5 = (w5 + 1) & m5;
            r5 = (r5 + 1) & m5;
            w6 = (w6 + 1) & m6;
            r6 = (r6 + 1) & m6;
            w7 = (w7 + 1) & m7;
            r7 = (r7 + 1) & m7;
            w8 = (w8 + 1) & m8;
            r8 = (r8 + 1) & m8;
            w9 = (w9 + 1) & m9;
            r9 = (r9 + 1) & m9;
            w10 = (w10 + 1) & m10;
            r10 = (r10 + 1) & m10;
            w11 = (w11 + 1) & m11;
            r11 = (r11 + 1) & m11;
        }

        // Update preDelay index
        _pDWrite = (blockStart + sampleCount) & pDMask;
        
        // Save state
        _lp1 = lp1;
        _lp2 = lp2;
        _lp3 = lp3;
        _excPhase1 = p1;
        _excPhase2 = p2;

        _delayWrite[0] = w0;
        _delayRead[0] = r0;
        _delayWrite[1] = w1;
        _delayRead[1] = r1;
        _delayWrite[2] = w2;
        _delayRead[2] = r2;
        _delayWrite[3] = w3;
        _delayRead[3] = r3;
        _delayWrite[4] = w4;
        _delayRead[4] = r4;
        _delayWrite[5] = w5;
        _delayRead[5] = r5;
        _delayWrite[6] = w6;
        _delayRead[6] = r6;
        _delayWrite[7] = w7;
        _delayRead[7] = r7;
        _delayWrite[8] = w8;
        _delayRead[8] = r8;
        _delayWrite[9] = w9;
        _delayRead[9] = r9;
        _delayWrite[10] = w10;
        _delayRead[10] = r10;
        _delayWrite[11] = w11;
        _delayRead[11] = r11;
    }
}