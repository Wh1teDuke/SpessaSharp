using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG;

/// <summary> Represents the XG Chorus Effect block. </summary>
public sealed class XGChorusBlock(int sampleRate, int maxBufferSize)
    : XGSystemEffectBlock(Map, XGNoEffect.Builder, 0x41_00, sampleRate, maxBufferSize)
{
    private static readonly Dictionary<int, XGEffectProcessor.Constructor> Map = new()
    { {0x00, XGNoEffect.Builder}, };
    
    /// <summary> A snapshot of a <see cref="XGChorusBlock"/>. </summary>
    public sealed class Snapshot : XGSystemEffectBlock.Snapshot
    {
        /// <summary> The amount of chorus being sent to the reverb effect. </summary>
        public required int SentToReverb;
    }
    
    /// <summary>
    /// The amount of chorus being sent to the reverb effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// </summary>
    public int SendToReverb = 0;

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
        AddSend(reverbLeft, reverbRight, sampleCount, SendToReverb);
    }

    public override Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new Snapshot
        {
            SentToReverb = SendToReverb,
            Type = snapshot.Type,
            Params = snapshot.Params,
            Pan = snapshot.Pan,
            ReturnLevel = snapshot.ReturnLevel,
        };
    }

    public void ApplySnapshot(Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        SendToReverb = snapshot.SentToReverb;
    }
}