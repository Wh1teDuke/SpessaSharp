using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

/// <summary>
/// Reverb is an effect that adds reverberation to a sound, as you would hear in a concert hall.
/// This is a Yamaha XG-compatible reverb interface.
/// It is used when <see cref="Midi.System"/> is <c>XG</c>.
/// <see cref="Synthesizer"/> allows you to supply a custom reverb block.
/// A custom reverb block must implement this interface.
/// Editing the parameters can be done via XG system exclusive messages.
/// <remarks>
/// Refer to <see href="https://usa.yamaha.com/files/download/other_assets/1/318081/MU128E2.pdf">MU128 SOUND LIST &amp; MIDI DATA</see> (p.40) for more information.
/// </remarks>
/// </summary>
public interface XGReverbBlock: 
    XGSystemEffectBlock
{
    /// <summary>
    /// Process the effect and <b>adds</b> it to the output.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
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
        int startIndex,
        int sampleCount);
}