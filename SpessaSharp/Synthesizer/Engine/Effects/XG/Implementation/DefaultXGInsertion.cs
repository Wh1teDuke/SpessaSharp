using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

/// <summary>
/// The default implementation for <see cref="DefaultXGInsertion"/>
/// </summary>
public sealed class DefaultXGInsertion(
    int sampleRate, int maxBufferSize): DefaultXGEffect(
    Map, XGNoEffect.Builder, 0x49_00, sampleRate, maxBufferSize),
    XGInsertionBlock
{
    private static readonly Dictionary<int, XGEffectProcessor.Constructor> Map = new()
    { { 0x00_00, XGNoEffect.Builder }, };

    /// <summary>
    /// The channel routed through the insertion effect.
    /// 127 (OFF) by default, so the block is disabled until assigned.
    /// </summary>
    public int PartNumber { get; set; } = 127;

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

    public override XGInsertionBlock.Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new XGInsertionBlock.Snapshot
        {
            PartNumber = PartNumber,
            Type = snapshot.Type,
            Params = snapshot.Params,
        };
    }

    public void ApplySnapshot(XGInsertionBlock.Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        PartNumber = snapshot.PartNumber;
    }
}