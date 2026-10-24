using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

/// <summary>
/// GS-compatible reverb parameters.
/// Also see <see cref="GSReverbProcessor"/> to see how to implement a custom GS-compatible reverb processor.
/// </summary>
public interface GSReverbParameter: GSSystemEffectParameter
{
    /// <summary>
    /// <c>0-7</c><br/>
    /// This parameter selects the type of reverb. 0–5 are reverb effects, and 6 and 7 are delay
    /// effects.
    /// </summary>
    public int Character { get; set; }
    
    /// <summary>
    /// <c>0-7</c><br/>
    /// This parameter sets the time over which the reverberation will continue.
    /// Higher values result in longer reverberation.
    /// </summary>
    public int Time { get; set; }
    
    /// <summary>
    /// <c>0-7</c><br/>
    /// This parameter is used when the Reverb Character is set to 6 or 7, or the Reverb Type
    /// is set to Delay or Panning Delay (Rev Character 6, 7). It sets the way in which delays
    /// repeat. Higher values result in more delay repeats.
    /// </summary>
    public int DelayFeedback { get; set; }
    
    /// <summary>
    /// <c>0-7</c><br/>
    /// This parameter sets the delay time until the reverberant sound is heard.
    /// Higher values result in a longer pre-delay time, simulating a larger reverberant space.
    /// </summary>
    public int PreDelayTime { get; set; }
}

/// <summary>
/// Reverb is an effect that adds reverberation to a sound, as you would hear in a concert
/// hall.
/// This is a Roland GS-compatible reverb interface.
/// It is used when <see cref="Midi.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
/// {@link SpessaSynthProcessor} allows you to supply a custom reverb processor.
/// A custom reverb processor must implement this interface.
/// Editing the parameters can be done via GS/GM2 system exclusive messages.
/// <remarks>
/// Refer to <see href="https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf">SC-8850 Owner's Manual</see> (p.79, 235-236) for more information.
/// </remarks>
/// </summary>
public interface GSReverbProcessor: GSReverbParameter
{
    /// <summary>Process the effect and <b>adds</b> it to the output.</summary>
    /// <param name="input">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">
    /// The amount of samples to mix. This will never be larger than
    /// <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent
    /// <see cref="Synthesizer"/> instance.
    /// </param>
    void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount);

    /// <summary> Gets a snapshot of this effect processor instance. </summary>
    GSReverbParameter GetSnapshot();
}