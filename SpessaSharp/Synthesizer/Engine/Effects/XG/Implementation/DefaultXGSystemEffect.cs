using System.Numerics.Tensors;
using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

/// <summary>
/// The default implementation for <see cref="XGSystemEffectBlock"/>
/// System effects are global and have a send level for each channel, always adding wet output into them.
/// </summary>
/// <param name="effectMap"></param>
/// <param name="fallbackProcessor"></param>
/// <param name="defaultType"></param>
/// <param name="sampleRate"></param>
/// <param name="maxBufferSize"></param>
public abstract class DefaultXGSystemEffect(
    Dictionary<int, XGEffectProcessor.Constructor> effectMap,
    XGEffectProcessor.Constructor fallbackProcessor,
    int defaultType,
    int sampleRate,
    int maxBufferSize)
    : DefaultXGEffect(effectMap, fallbackProcessor, defaultType, sampleRate, maxBufferSize), 
        XGSystemEffectBlock
{
    private const int MIN_PAN = 1;
    private const int MAX_PAN = 127;
    private const int PAN_RESOLUTION = MAX_PAN - MIN_PAN;

    private static readonly float[] PanTableLeft = new float[PAN_RESOLUTION + 1];
    private static readonly float[] PanTableRight = new float[PAN_RESOLUTION + 1];

    // Initialize pan lookup tables
    static DefaultXGSystemEffect()
    {
        for (var pan = MIN_PAN; pan <= MAX_PAN; pan++) 
        {
            // Clamp to 0-1
            var realPan = (pan - MIN_PAN) / (float)PAN_RESOLUTION;
            var tableIndex = pan - MIN_PAN;
            var (s, c) = MathF.SinCos((MathF.PI / 2) * realPan);
            PanTableLeft[tableIndex] = c;
            PanTableRight[tableIndex] = s;
        }
    }

    /// <summary>
    /// The return (level) of the effect. 0 is silence (-Inf dB), 64 is normal (0 dB) and 127 is double the volume (+6 dB).
    /// </summary>
    public int Return { get; set; } = 64;

    /// <summary>
    /// The stereo panning of this effect. 1 is hard left, 64 is center, 127 is hard right.
    /// </summary>
    public int Pan { get; set; }  = 64;

    public override void Reset()
    {
        base.Reset();
        Return = 64;
        Pan = 64;
    }

    public override XGSystemEffectBlock.Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new XGSystemEffectBlock.Snapshot
        {
            Pan = Pan,
            Return = Return,
            Type = snapshot.Type,
            Params = snapshot.Params,
        };
    }

    public void ApplySnapshot(XGSystemEffectBlock.Snapshot snapshot) 
    {
        base.ApplySnapshot(snapshot);
        Return = snapshot.Return;
        Pan = snapshot.Pan;
    }

    /// <summary>
    /// Mixes the staged wet output into the system effect destination, applying return and pan.
    /// </summary>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    protected void MixSystemEffect(
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        var gain = Return / 64f;
        var panIndex = Math.Clamp(Pan, MIN_PAN, MAX_PAN) - MIN_PAN;
        var gainLeft = PanTableLeft[panIndex] * gain;
        var gainRight = PanTableRight[panIndex] * gain;
        
        var outLeft = OutputLeft.AsSpan(startIndex, sampleCount);
        var outRight = OutputRight.AsSpan(startIndex, sampleCount);
        
        var destLeft = outputLeft.Slice(startIndex, sampleCount);
        var destRight = outputRight.Slice(startIndex, sampleCount);

        TensorPrimitives.MultiplyAdd(
            outLeft, gainLeft, destLeft, destLeft);
        TensorPrimitives.MultiplyAdd(
            outRight, gainRight, destRight, destRight);
    }

    /// <summary>
    /// Adds the wet output into a send buffer (e.g. chorus into reverb).
    /// Send reads from the wet signal before return, so it sounds even at return 0.
    /// Mixing always starts at index 0.
    /// </summary>
    /// <param name="outputLeft">The left send buffer.</param>
    /// <param name="outputRight">The right send buffer.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    /// <param name="send">The send amount, where 64 is 100%;</param>
    protected void AddSend(
        Span<float> outputLeft, 
        Span<float> outputRight,
        int sampleCount,
        int send)
    {
        // Common scenario
        if (send == 0) return;
        var gain = send / 64f;
        
        var outLeft = OutputLeft.AsSpan(0, sampleCount);
        var outRight = OutputRight.AsSpan(0, sampleCount);

        TensorPrimitives.MultiplyAdd(
            outLeft, gain, outputLeft, outputLeft);
        TensorPrimitives.MultiplyAdd(
            outRight, gain, outputRight, outputRight);
    }
}