using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG;

/// <summary>
/// Represents a Yamaha XG Insertion Effect block (EFFECT 2).
/// Unlike variation, it has no return, pan or sends:
/// it always runs in insertion mode on a single <c>partNumber</c> channel.
/// </summary>
public sealed class XGInsertionBlock(
    int sampleRate, int maxBufferSize): XGEffectBlock(
    Map, XGNoEffect.Builder, 0x49_00, sampleRate, maxBufferSize)
{
    private static readonly Dictionary<int, XGEffectProcessor.Constructor> Map = new()
    { { 0x00_00, XGNoEffect.Builder }, };
    
    /// <summary> A snapshot of a <see cref="XGInsertionBlock"/>. </summary>
    public sealed class Snapshot : XGEffectBlock.Snapshot
    {
        /// <summary>
        /// The channel routed through the insertion effect
        /// (0-63 parts, 127 OFF).
        /// </summary>
        public required int PartNumber;
    }

    /// <summary>
    /// The channel routed through the insertion effect.
    /// 127 (OFF) by default, so the block is disabled until assigned.
    /// </summary>
    public int PartNumber = 127;

    public override void Reset()
    {
        base.Reset();
        PartNumber = 127;
    }

    /// <summary>
    /// Process the effect and <b>overwrites the input</b>.
    /// The DSP honors Dry/Wet.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="sampleCount">
    /// The amount of samples to mix. This will never be larger than
    /// <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent
    /// <see cref="Synthesizer"/> instance.
    /// </param>
    public void ProcessInsertion(
        Span<float> inputLeft,
        Span<float> inputRight,
        int sampleCount)
    {
        Processor.Process(
            inputLeft, 
            inputRight,
            OutputLeft,
            OutputRight,
            sampleCount,
            true);
        inputLeft.CopyTo(OutputLeft);
        inputRight.CopyTo(OutputRight);
    }

    public override Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new Snapshot
        {
            PartNumber = PartNumber,
            Type = snapshot.Type,
            Params = snapshot.Params,
        };
    }

    public void ApplySnapshot(Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        PartNumber = snapshot.PartNumber;
    }
}