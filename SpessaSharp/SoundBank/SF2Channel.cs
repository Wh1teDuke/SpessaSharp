namespace SpessaSharp.SoundBank;

/// <summary>
///  Represents a channel in an SF2-compatible synthesizer.
/// Its data is used for computing  <see cref="Modulator"/>s.
/// </summary>
public interface ISf2Channel
{
    /// <summary>All MIDI controller values for modulation.</summary>
    public ReadOnlySpan<short> GetMidiControllers { get; }
    
    /// <summary> Other MIDI parameters. </summary>
    public (int Pressure, int PitchWheel, float PitchWheelRange,
        byte[] PolyPressures) GetMidiParameters { get; }
}