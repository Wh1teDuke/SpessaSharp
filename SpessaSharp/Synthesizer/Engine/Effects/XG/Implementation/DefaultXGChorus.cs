using System.Numerics.Tensors;
using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

/// <summary>The default implementation for <see cref="XGChorusBlock"/>.</summary>
public sealed class DefaultXGChorus(int sampleRate, int maxBufferSize)
    : DefaultXGSystemEffect(Map, XGNoEffect.Builder, 0x41_00, sampleRate, maxBufferSize),
        XGChorusBlock
{
    private static readonly Dictionary<int, DefaultXGEffectProcessor.Constructor> Map = new()
    { {0x00, XGNoEffect.Builder}, };

    /// <summary>
    /// The amount of chorus being sent to the reverb effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// </summary>
    public int SendToReverb { get; set; } = 0;

    public override void Reset()
    {
        base.Reset();
        SendToReverb = 0;
    }

    /// <summary>
    /// Process the effect and <b>adds</b> it to the output.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="reverbLeft">The left reverb send buffer.</param>
    /// <param name="reverbRight">The right reverb send buffer.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">
    /// The amount of samples to mix. This will never be larger than
    /// <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent
    /// <see cref="Synthesizer"/> instance.
    /// </param>
    public void Process(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight,
        Span<float> reverbLeft,
        Span<float> reverbRight,
        int startIndex,
        int sampleCount)
    {
        Processor.Process(
            inputLeft, inputRight, OutputLeft, OutputRight, sampleCount, false);
        MixSystemEffect(outputLeft, outputRight, startIndex, sampleCount);
        // Chorus gets sent to reverb regardless of return,
        // Even at 0 and part having only chorus applied,
        // The reverb still sounds with chorus to reverb being 127.
        // Tested on MU2K
        
        // Common scenario
        if (SendToReverb == 0) return;
        var gain = SendToReverb / 64f;
        
        var left = OutputLeft.AsSpan(0, sampleCount);
        var right = OutputRight.AsSpan(0, sampleCount);
        TensorPrimitives.MultiplyAdd(
            reverbLeft[..sampleCount], gain, left, left);
        TensorPrimitives.MultiplyAdd(
            reverbRight[..sampleCount], gain, right, right);
    }

    public override XGChorusBlock.Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new XGChorusBlock.Snapshot
        {
            SendToReverb = SendToReverb,
            Type = snapshot.Type,
            Params = snapshot.Params,
            Pan = snapshot.Pan,
            Return = snapshot.Return,
        };
    }

    public void ApplySnapshot(XGChorusBlock.Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        SendToReverb = snapshot.SendToReverb;
    }
}