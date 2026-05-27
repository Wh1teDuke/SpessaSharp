namespace SpessaSharp.SoundBank;

public interface ISf2Channel
{
    /// <summary>All MIDI controller values for modulation.</summary>
    public ReadOnlySpan<short> GetMidiControllers { get; }
    
    /// <summary> Other MIDI parameters. </summary>
    public (int Pressure, int PitchWheel, float PitchWheelRange) GetMidiParameters { get; }
}