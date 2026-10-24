using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;


public interface XGVariationParameter : 
    XGSystemEffectParameter,
    XGInsertionParameter
{
    /// <summary>
    /// True if variation is in the insertion mode.
    /// The systems work as following:
    /// <c>system</c> routes all channels via sends (like reverb and chorus).
    /// <c>insertion</c> routes a single <c>partNumber</c> channel straight through. Note that variation is processed last, after all insertion effects.
    /// </summary>
    public bool InsertionMode { get; set; }

    /// <summary>
    /// The amount of variation being sent to the reverb effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// <remarks>This is only active in the <c>system</c> mode.</remarks>
    /// </summary>
    public int SendToReverb { get; set; }

    /// <summary>
    /// The amount of variation being sent to the chorus effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// <remarks>This is only active in the <c>system</c> mode.</remarks>
    /// </summary>
    public int SendToChorus { get; set; }
}

/// <summary>
/// The Variation section provides a wealth of additional effects.
/// It features some of the same effects found in the
/// Reverb, Chorus and Insertion sections. This is not mere redundancy; it allows you to use two types of Reverb, Chorus or other effects on different
/// Voices.
/// The Variation section of effects can be applied either to a single selected
/// Part or to all Parts, depending on the connection setting: Insertion or System.
/// This is a Yamaha XG-compatible variation interface.
/// It is used when <see cref="Midi.System"/> is <c>XG</c>.
/// <see cref="Synthesizer"/> allows you to supply a custom variation block.
/// A custom variation block must implement this interface.
/// Editing the parameters can be done via XG system exclusive messages.
/// <remarks>
/// Refer to <see href="https://usa.yamaha.com/files/download/other_assets/1/318081/MU128E2.pdf">MU128 SOUND LIST &amp; MIDI DATA</see> (p.41-442) for more information.
/// </remarks>
/// </summary>
public interface XGVariationBlock:
    XGVariationParameter,
    XGSystemEffectBlock,
    XGInsertionBlock
{
    public new class Snapshot : XGInsertionBlock.Snapshot, XGVariationParameter
    {
        public bool InsertionMode { get; set; }
        public int SendToReverb { get; set; }
        public int SendToChorus { get; set; }
    }

    /// <summary>
    /// Process the effect in _system_ mode and <b>adds</b> it to the output.
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
        int sampleCount);
    
    new Snapshot GetSnapshot();
    void ApplySnapshot(Snapshot snapshot);
}