using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

/// <summary> Parameters for the XG insertion effect. </summary>
public interface XGInsertionParameter
{
    /// <summary>
    /// The channel routed through the insertion effect, 0-based.
    /// 127 or a value that's above the current channel count means OFF.
    /// </summary>
    public int PartNumber { get; set; }
}

/// <summary>
/// The Insertion effects provide additional effects for processing individual Parts.
/// The Insertion effects are set up for Insertion routing and can be applied only
/// to a single selected Part.
/// <see cref="Synthesizer"/> currently has 4 insertion effects. More than one effect can be applied to a single Part (channel).
/// This is a Yamaha XG-compatible insertion interface.
/// It is used when <see cref="Midi.System"/> is <c>XG</c>.
/// <see cref="Synthesizer"/> allows you to supply a custom insertion block.
/// A custom insertion block must implement this interface.
/// Editing the parameters can be done via XG system exclusive messages.
/// <remarks>
/// Refer to <see href="https://usa.yamaha.com/files/download/other_assets/1/318081/MU128E2.pdf">MU128 SOUND LIST &amp; MIDI DATA</see> (p.42-44) for more information.
/// </remarks>
/// </summary>
public interface XGInsertionBlock:
    XGInsertionParameter,
    XGEffectBlock
{
    /// <summary> A snapshot of a <see cref="XGInsertionBlock"/>. </summary>
    public new class Snapshot : XGSystemEffectBlock.Snapshot, XGInsertionParameter
    {
        public int PartNumber { get; set; }
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
        int sampleCount);

    new Snapshot GetSnapshot();

    void ApplySnapshot(Snapshot snapshot);
}