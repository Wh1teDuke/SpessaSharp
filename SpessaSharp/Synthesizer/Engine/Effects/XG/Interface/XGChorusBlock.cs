using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;


/// <summary> Parameters for the XG chorus effect. </summary>
public interface XGChorusParameter
{
    /// <summary>
    /// The amount of chorus being sent to the reverb effect.
    /// 0 is none, 64 is 100% and 127 is 200%.
    /// </summary>
    public int SendToReverb { get; set; }
}

/// <summary>
/// Chorus broadens the spatial image of the sound, adding depth and richness.
/// This is a Yamaha XG-compatible chorus interface.
/// It is used when <see cref="Midi.System"/> is <c>XG</c>.
/// <see cref="Synthesizer"/> allows you to supply a custom chorus block.
/// A custom chorus block must implement this interface.
/// Editing the parameters can be done via XG system exclusive messages.
/// <remarks>
/// Refer to <see href="https://usa.yamaha.com/files/download/other_assets/1/318081/MU128E2.pdf">MU128 SOUND LIST &amp; MIDI DATA</see> (p.40-41) for more information.
/// </remarks>
/// </summary>
public interface XGChorusBlock: 
    XGSystemEffectBlock,
    XGChorusParameter
{
    /// <summary> A snapshot of a <see cref="XGChorusBlock"/>. </summary>
    public new sealed class Snapshot : 
        XGSystemEffectBlock.Snapshot, XGChorusParameter
    {
        /// <summary> The amount of chorus being sent to the reverb effect. </summary>
        public int SendToReverb { get; set; }
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
        int sampleCount);

    new Snapshot GetSnapshot();

    void ApplySnapshot(Snapshot snapshot);
}