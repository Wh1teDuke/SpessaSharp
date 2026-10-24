using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

/// <summary>
/// Represents the XG Variation Effect block.
/// Unlike reverb and chorus, it can run either as a system effect
/// (all channels via sends) or as an insertion effect
/// (a single <c>partNumber</c> channel routed straight through it).
/// </summary>
public sealed class DefaultXGVariation(int sampleRate, int maxBufferSize)
    : DefaultXGSystemEffect(Map, XGThru.Builder, 0x05_00, sampleRate, maxBufferSize),
        XGVariationBlock
{
    private static readonly Dictionary<int, DefaultXGEffectProcessor.Constructor> Map = new()
    {
        { 0x_00_00, XGNoEffect.Builder },
        { 0x_40_00, XGThru.Builder },
    };

    /// <summary>
    /// True if variation is in the insertion mode.
    /// The systems work as following:
    /// <c>system</c> routes all channels via sends (like reverb and chorus).
    /// <c>insertion</c> routes a single <c>partNumber</c> channel straight through. Note that variation is processed last, after all insertion effects.
    /// </summary>
    public bool InsertionMode { get; set; } = true;

    /// <summary>
    /// The channel routed through the effect in insertion mode.
    /// 127 (OFF) by default, so the block is disabled until assigned.
    /// Note that any value which is not a valid channel number (for example 63 when there are only 16 channels) is also treated as OFF.
    /// </summary>
    public int PartNumber { get; set; } = 127;

    /// <summary>
    /// The amount of variation being sent to the reverb effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// </summary>
    public int SendToReverb { get; set; } = 0;

    /// <summary>
    /// The amount of variation being sent to the chorus effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// </summary>
    public int SendToChorus { get; set; } = 0;

    public override void Reset()
    {
        base.Reset();
        InsertionMode = true;
        PartNumber = 127;
        SendToReverb = 0;
        SendToChorus = 0;
    }

    /// <summary>
    /// Process the effect in _system_ mode and **adds** it to the output.
    /// Feeds the chorus and reverb buffers according to the send amounts.
    /// The DSP's wet is fixed at 100% in this case.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="chorusLeft">The left chorus send buffer. It always starts at index 0.</param>
    /// <param name="chorusRight">The right chorus send buffer. It always starts at index 0.</param>
    /// <param name="reverbLeft">The left reverb send buffer. It always starts at index 0.</param>
    /// <param name="reverbRight">The right reverb send buffer. It always starts at index 0.</param>
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
        Span<float> chorusLeft,
        Span<float> chorusRight,
        Span<float> reverbLeft,
        Span<float> reverbRight,
        int startIndex,
        int sampleCount)
    {
        Processor.Process(
            inputLeft,
            inputRight,
            OutputLeft,
            OutputRight,
            sampleCount,
            false);
        MixSystemEffect(outputLeft, outputRight, startIndex, sampleCount);
        
        // Variation gets sent to chorus/reverb regardless of return,
        // For example, even at 0 and part having only variation applied,
        // The reverb still sounds with variation to reverb being 127.
        AddSend(chorusLeft, chorusRight, sampleCount, SendToChorus);
        AddSend(reverbLeft, reverbRight, sampleCount, SendToReverb);
    }

    /// <summary>
    /// Process the effect in insertion mode and <b>overwrites</b> it to the input.
    /// The DSP honors Dry/Wet in this case.
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
        // Variation in system mode ignores sends
        // See MU128 manual page 154
        OutputLeft.CopyTo(inputLeft);
        OutputRight.CopyTo(inputRight);
    }

    XGInsertionBlock.Snapshot XGInsertionBlock.GetSnapshot() =>
        GetSnapshot();

    public void ApplySnapshot(XGInsertionBlock.Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        PartNumber = snapshot.PartNumber;
    }

    public override XGVariationBlock.Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new XGVariationBlock.Snapshot
        {
            InsertionMode = InsertionMode,
            PartNumber   = PartNumber,
            SendToReverb = SendToReverb,
            SendToChorus = SendToChorus,
            Type = snapshot.Type,
            Params = snapshot.Params,
            Return = snapshot.Return,
            Pan = snapshot.Pan,
        };
    }

    public void ApplySnapshot(XGVariationBlock.Snapshot snapshot)
    {
        base.ApplySnapshot(snapshot);
        InsertionMode = snapshot.InsertionMode;
        PartNumber = snapshot.PartNumber;
        SendToReverb = snapshot.SendToReverb;
        SendToChorus = snapshot.SendToChorus;
    }
}