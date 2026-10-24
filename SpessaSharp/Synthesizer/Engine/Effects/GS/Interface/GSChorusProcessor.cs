using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

/// <summary>
/// GS-compatible chorus parameters.
/// Also see <see cref="GSChorusProcessor"/> to see how to implement a custom GS-compatible chorus processor.
/// </summary>
public interface GSChorusParameter: GSSystemEffectParameter
{
    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the level at which the chorus sound is re-input (fed back) into the
    /// chorus. By using feedback, a denser chorus sound can be created.
    /// Higher values result in a greater feedback level.
    /// </summary>
    public int Feedback { get; set; }
    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the delay time of the chorus effect.
    /// </summary>
    public int Delay { get; set; }

    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the speed (frequency) at which the chorus sound is modulated.
    /// Higher values result in faster modulation.
    /// </summary>
    public int Rate { get; set; }
    
    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the depth at which the chorus sound is modulated.
    /// Higher values result in deeper modulation.
    /// </summary>
    public int Depth { get; set; }
    
    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the amount of chorus sound that will be sent to the reverb.
    /// Higher values result in more sound being sent.
    /// </summary>
    public int SendLevelToReverb { get; set; }
    
    /// <summary>
    /// <c>0-127</c>
    /// This parameter sets the amount of chorus sound that will be sent to the delay.
    /// Higher values result in more sound being sent.
    /// </summary>
    public int SendLevelToDelay { get; set; }
}

/// <summary>
/// Chorus broadens the spatial image of the sound, adding depth and richness.
/// This is a Roland GS-compatible chorus interface.
/// It is used when <see cref="Midi.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
/// <see cref="Synthesizer"/> allows you to supply a custom chorus processor.
/// A custom chorus processor must implement this interface.
/// Editing the parameters can be done via GS/GM2 system exclusive messages.
/// <remarks>
/// Refer to <see href="https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf">SC-8850 Owner's Manual</see> (p.79, 235-236) for more information.
/// </remarks>
/// </summary>
public interface GSChorusProcessor: GSChorusParameter
{
    /// <summary>
    /// Process the effect and **adds** it to the output.
    /// </summary>
    /// <param name="input">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
    /// <param name="outputDelay">The mono input for delay. It always starts at index 0.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">
    /// The amount of samples to mix. This will never be larger than
    /// <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent
    /// <see cref="Synthesizer"/> instance.
    /// </param>
    public void Process(
        ReadOnlySpan<float> input,
        Span<float> outputLeft,
        Span<float> outputRight,
        Span<float> outputReverb,
        Span<float> outputDelay,
        int startIndex,
        int sampleCount);

    /// <summary> Gets a snapshot of this effect processor instance.
    /// </summary>
    /// <returns></returns>
    public GSChorusParameter GetSnapshot();
}