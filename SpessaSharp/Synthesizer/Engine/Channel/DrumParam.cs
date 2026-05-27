namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary></summary>
/// <param name="Pitch">Pitch offset in cents.</param>
/// <param name="Gain">Gain multiplier.</param>
/// <param name="ExclusiveClass">Exclusive class override.</param>
/// <param name="Pan">Pan, 1-64-127, 0 is random. This adds to the channel pan!</param>
/// <param name="ReverbGain">Reverb multiplier.</param>
/// <param name="ChorusGain">Chorus multiplier.</param>
/// <param name="DelayGain">Delay multiplier.</param>
/// <param name="RxNoteOn">If note on should be received.</param>
/// <param name="RxNoteOff">If note off should be received. Note: Due to the way sound banks implement drums (as 100s release time), this means killing the voice on note off, not releasing it.</param>
public readonly record struct DrumParameters(
    int Pitch,
    float Gain,
    int ExclusiveClass,
    int Pan,
    float ReverbGain,
    float ChorusGain,
    float DelayGain,
    bool RxNoteOn,
    bool RxNoteOff
)
{
    public static readonly DrumParameters Default = new(
        0,
        1,
        0,
        64,
        0,
        1,
        1,
        true,
        false);
}