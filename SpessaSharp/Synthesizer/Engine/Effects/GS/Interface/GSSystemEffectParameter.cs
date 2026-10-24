namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

/// <summary>
/// Parameters shared between all GS system effects.
/// </summary>
public interface GSSystemEffectParameter
{
    /// <summary>
    /// 0-127
    /// This parameter sets the amount of the effect sent to the effect output.
    /// </summary>
    public int Level { get; set; }
    
    /// <summary>
    /// 0-7
    /// A low-pass filter can be applied to the sound coming into the effect to cut the high
    /// frequency range. Higher values will cut more of the high frequencies, resulting in a
    /// more mellow effect sound.
    /// </summary>
    public int PreLowPass { get; set; }
}