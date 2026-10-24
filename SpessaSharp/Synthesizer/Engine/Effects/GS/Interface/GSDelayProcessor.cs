using SpessaSharp.MIDI;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

/// <summary>
/// GS-compatible delay parameters.
/// Also see <see cref="GSDelayProcessor"/> to see how to implement a custom GS-compatible delay processor.
/// </summary>
public interface GSDelayParameter: GSSystemEffectParameter
{
    /// <summary>
    /// 0-115
    /// 0.1ms-340ms-1000ms
    /// The delay effect has three delay times; center, left and
    /// right (when listening in stereo). Delay Time Center sets the delay time of the delay
    /// located at the center.
    /// Refer to SC-8850 Owner's Manual p. 236 for the exact mapping of the values.
    /// </summary>
    public int TimeCenter { get; set; }
    
    /// <summary>
    /// 0-120
    /// 4% - 500%
    /// This parameter sets the delay time of the delay located at the left as a percentage of
    /// the Delay Time Center (up to a max. of 1.0 s).
    /// The resolution is 100/24(%).
    /// </summary>
    public int TimeRatioLeft { get; set; }
    
    /// <summary>
    /// 1-120
    /// 4%-500%
    /// This parameter sets the delay time of the delay located at the right as a percentage of
    /// the Delay Time Center (up to a max. of 1.0 s).
    /// The resolution is 100/24(%).
    /// </summary>
    public int TimeRatioRight { get; set; }
    
    /// <summary>
    /// 0-127
    /// This parameter sets the volume of the central delay. Higher values result in a louder
    /// center delay.
    /// </summary>
    public int LevelCenter { get; set; }
    
    /// <summary>
    /// 0-127
    /// This parameter sets the volume of the left delay. Higher values result in a louder left
    /// delay.
    /// </summary>
    public int LevelLeft { get; set; }
    
    /// <summary>
    /// 0-127
    /// This parameter sets the volume of the right delay. Higher values result in a louder
    /// right delay.
    /// </summary>
    public int LevelRight { get; set; }
    
    /// <summary>
    /// 0-127
    /// (-64)-63
    /// This parameter affects the number of times the delay will repeat. With a value of 0,
    /// the delay will not repeat. With higher values there will be more repeats.
    /// With negative (-) values, the center delay will be fed back with inverted phase.
    /// Negative values are effective with short delay times.
    /// </summary>
    public int Feedback { get; set; }
    
    /// <summary>
    /// 0-127
    /// This parameter sets the amount of delay sound that is sent to the reverb.
    /// Higher values result in more sound being sent.
    /// </summary>
    public int SendLevelToReverb { get; set; }
}

/// <summary>
/// Delay creates echoes. It is also possible to give depth and width to a sound by adding
/// a short delay to the original sound.
/// This is a Roland GS-compatible delay interface.
/// It is used when <see cref="Midi.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
/// <remarks>
/// Delay is disabled in XG mode.
/// </remarks>
/// <remarks>
/// Refer to <see href="https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf">SC-8850 Owner's Manual</see> (p.79, 235-236) for more information.
/// </remarks>
/// </summary>
public interface GSDelayProcessor : GSDelayParameter
{
    /// <summary>Process the effect and <b>adds</b> it to the output.</summary>
    /// <param name="input">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
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
        Span<float> outputReverb,
        int startIndex,
        int sampleCount);
    
    GSDelayParameter GetSnapshot();
}