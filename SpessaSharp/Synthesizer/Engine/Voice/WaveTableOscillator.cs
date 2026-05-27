using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>Plays back raw audio data at an arbitrary playback rate</summary>
public abstract class WaveTableOscillator
{
    /// <summary>Is the loop on?</summary>
    public bool IsLooping = false;
    /// <summary>Sample data of the voice.</summary>
    public ArraySegment<float>? SampleData;
    /// <summary>Playback step (rate) for sample pitch correction.</summary>
    public float PlaybackStep = 0;
    /// <summary>Start position of the loop. Inclusive.</summary>
    public int LoopStart = 0;
    /// <summary>End position of the loop. Exclusive.</summary>
    public int LoopEnd = 0;
    /// <summary>Length of the loop.</summary>
    internal int LoopLength = 0;
    /// <summary>End position of the sample. Exclusive.</summary>
    public int End = 0;
    /// <summary>The current cursor of the sample.</summary>
    public float Cursor = 0;

    /// <summary> Fills the output buffer with raw sample data using a given interpolation. </summary>
    /// <param name="sampleCount">The amount of samples to write into the buffer.</param>
    /// <param name="tuningRatio">The tuning ratio to apply.</param>
    /// <param name="outputBuffer">The output buffer to write to.</param>
    /// <returns></returns>
    public abstract bool Process(
        int sampleCount,
        float tuningRatio,
        Span<float> outputBuffer);

    public sealed class Linear : WaveTableOscillator
    {
        public override bool Process(
            int sampleCount, float tuningRatio, Span<float> outputBuffer)
        {
            var step = tuningRatio * PlaybackStep;
            var sampleData = SampleData!.Value.AsSpan();
            var cursor = Cursor;
            
            ref var sampleDataRef = 
                ref MemoryMarshal.GetReference(sampleData);
            ref var outputRef =
                ref MemoryMarshal.GetReference(outputBuffer);

            if (IsLooping)
            {
                var loopEnd = LoopEnd;
                var loopLen = LoopLength;
                
                for (var i = 0; i < sampleCount; i++) 
                {
                    // Check for loop first (end is exclusive so it's `<=` and not `<`)
                    // Testcase: https://github.com/spessasus/spessasynth_core/issues/90
                    // Extreme playback rates:
                    // Sample can loop more than once per frame! (that's why this 'while' is here)
                    // Testcase: saw_and_square_wave.sf2
                    while (cursor >= loopEnd) cursor -= loopLen;

                    // Grab the 2 nearest points
                    var floor = (int)cursor;
                    var ceil = floor + 1;

                    // Ensure that the point above does not go over the loop
                    if (ceil >= loopEnd) ceil -= loopLen;

                    var fraction = cursor - floor;

                    // Grab the samples and interpolate
                    var upper = Unsafe.Add(ref sampleDataRef, ceil);
                    var lower = Unsafe.Add(ref sampleDataRef, floor);
                    Unsafe.Add(ref outputRef, i) = lower + (upper - lower) * fraction;
                    
                    cursor += step;
                }
            }
            else
            {
                var end = End;
                
                for (var i = 0; i < sampleCount; i++) 
                {
                    // Linear interpolation
                    var floor = (int)cursor;
                    var ceil = floor + 1;

                    // Flag the voice as finished if needed
                    if (ceil >= end) 
                    {
                        // Fill the rest with zeros (testcase drum_spam_test.mid)
                        outputBuffer[i .. sampleCount].Clear();
                        return false;
                    }

                    var fraction = cursor - floor;

                    // Grab the samples and interpolate
                    var upper = Unsafe.Add(ref sampleDataRef, ceil);
                    var lower = Unsafe.Add(ref sampleDataRef, floor);
                    Unsafe.Add(ref outputRef, i) = lower + (upper - lower) * fraction;

                    cursor += step;
                }
            }
            
            Cursor = cursor;
            return true;
        }
    }

    public sealed class Nearest : WaveTableOscillator
    {
        public override bool Process(
            int sampleCount, 
            float tuningRatio, 
            Span<float> outputBuffer)
        {
            var step = tuningRatio * PlaybackStep;
            var sampleData = SampleData!.Value.AsSpan();
            var cursor = Cursor;

            ref var sampleDataRef = 
                ref MemoryMarshal.GetReference(sampleData);
            ref var outputRef =
                ref MemoryMarshal.GetReference(outputBuffer);

            if (IsLooping)
            {
                var loopEnd = LoopEnd;
                var loopLen = LoopLength;
                
                for (var i = 0; i < sampleCount; i++) 
                {
                    // Check for loop first (end is exclusive so it's `<=` and not `<`)
                    // Testcase: https://github.com/spessasus/spessasynth_core/issues/90
                    // Testcase for this type of loop checking: LiveHQ Classical Guitar finger off
                    // (5 long loop in mode 3)
                    // Extreme playback rates:
                    // Sample can loop more than once per frame! (that's why this 'while' is here)
                    // Testcase: saw_and_square_wave.sf2
                    while (cursor >= loopEnd) cursor -= loopLen;

                    // Grab the nearest neighbor
                    Unsafe.Add(ref outputRef, i) = Unsafe.Add(ref sampleDataRef, (int)cursor);
                    cursor += step;
                }
            }
            else
            {
                var end = End;
                
                for (var i = 0; i < sampleCount; i++) 
                {
                    // Flag the voice as finished if needed
                    if (cursor >= end) 
                    {
                        // Fill the rest with zeros (testcase drum_spam_test.mid)
                        outputBuffer[i .. sampleCount].Clear();
                        return false;
                    }
                    
                    Unsafe.Add(ref outputRef, i) = Unsafe.Add(ref sampleDataRef, (int)cursor);
                    cursor += step;
                }
            }
            
            Cursor = cursor;
            return true;
        }
    }

    public sealed class Hermite : WaveTableOscillator
    {
        public override bool Process(
            int sampleCount, 
            float tuningRatio, 
            Span<float> outputBuffer)
        {
            var step = tuningRatio * PlaybackStep;
            var sampleData = SampleData!.Value.AsSpan();
            var cursor = Cursor;
            ref var sampleDataRef = 
                ref MemoryMarshal.GetReference(sampleData);
            ref var outputRef =
                ref MemoryMarshal.GetReference(outputBuffer);

            if (IsLooping)
            {
                var loopEnd = LoopEnd;
                var loopLen = LoopLength;
                
                for (var i = 0; i < sampleCount; i++)
                {
                    // Check for loop first (end is exclusive so it's `<=` and not `<`)
                    // Testcase: https://github.com/spessasus/spessasynth_core/issues/90
                    // Extreme playback rates:
                    // Sample can loop more than once per frame! (that's why this 'while' is here)
                    // Testcase: saw_and_square_wave.sf2
                    while (cursor >= loopEnd) cursor -= loopLen;

                    // Grab the 4 points
                    var y0 = (int)cursor; // Point before the cursor.
                    var y1 = y0 + 1; // Point after the cursor
                    var y2 = y0 + 2; // Point 1 after the cursor
                    var y3 = y0 + 3; // Point 2 after the cursor
                    var t = cursor - y0; // The distance from y0 to cursor [0;1]
                    // Y0 is not handled here
                    // As it's floor of cur which is handled above
                    
                    // Ensure that the points above do not go over the loop
                    if (y1 >= loopEnd) y1 -= loopLen;
                    if (y2 >= loopEnd) y2 -= loopLen;
                    if (y3 >= loopEnd) y3 -= loopLen;

                    // Grab the samples
                    var xm1 = Unsafe.Add(ref sampleDataRef, y0);
                    var x0  = Unsafe.Add(ref sampleDataRef, y1);
                    var x1  = Unsafe.Add(ref sampleDataRef, y2);
                    var x2  = Unsafe.Add(ref sampleDataRef, y3);

                    // Interpolate
                    // https://www.musicdsp.org/en/latest/Other/93-hermite-interpollation.html
                    var c = (x1 - xm1) * 0.5f;
                    var v = x0 - x1;
                    var w = c + v;
                    var a = w + v + (x2 - x0) * 0.5f;
                    var b = w + a;

                    Unsafe.Add(ref outputRef, i) = ((a * t - b) * t + c) * t + x0;

                    cursor += step;
                }
            }
            else
            {
                var end = End;
                
                for (var i = 0; i < sampleCount; i++)
                {
                    // Grab the 4 points
                    var y0 = (int)cursor; // Point before the cursor.
                    var y1 = y0 + 1; // Point after the cursor
                    var y2 = y0 + 2; // Point 1 after the cursor
                    var y3 = y0 + 3; // Point 2 after the cursor
                    var t = cursor - y0; // The distance from y0 to cursor [0;1]

                    // Flag as finished if needed
                    if (y3 >= end)
                    {
                        // Fill the rest with zeros (testcase drum_spam_test.mid)
                        outputBuffer[i .. sampleCount].Clear();
                        return false;
                    }
                    
                    // Grab the samples
                    var xm1 = Unsafe.Add(ref sampleDataRef, y0);
                    var x0 = Unsafe.Add(ref sampleDataRef, y1);
                    var x1 = Unsafe.Add(ref sampleDataRef, y2);
                    var x2 = Unsafe.Add(ref sampleDataRef, y3);

                    // Interpolate
                    // https://www.musicdsp.org/en/latest/Other/93-hermite-interpollation.html
                    var c = (x1 - xm1) * 0.5f;
                    var v = x0 - x1;
                    var w = c + v;
                    var a = w + v + (x2 - x0) * 0.5f;
                    var b = w + a;

                    Unsafe.Add(ref outputRef, i) = ((a * t - b) * t + c) * t + x0;

                    cursor += step;
                }
            }
            
            Cursor = cursor;
            return true;
        }
    }
}