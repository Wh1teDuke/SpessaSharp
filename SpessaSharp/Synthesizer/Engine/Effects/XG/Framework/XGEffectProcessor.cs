using SpessaSharp.Synthesizer.Engine.Effects.GS;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;


/// <summary>
/// The raw DSP processor for Yamaha XG effects.
/// Reverb, Chorus, Variation and Insertion all use it.
/// </summary>
public abstract class XGEffectProcessor
{
    public delegate XGEffectProcessor Constructor(
        int sampleRate, int maxBufferSize);
    
    /// <summary>
    /// The type of the effect.
    /// 16-bit ID, <c>(MSB &lt;&lt; 8) | LSB</c> (e.g. 0x4100)
    /// </summary>
    public abstract int Type { get; }

    /// <summary> Resets all parameters to default. </summary>
    public abstract void Reset();

    /// <summary>
    /// Sets the given effect parameter to the given value.
    /// </summary>
    /// <param name="param">The parameter number (0-based).</param>
    /// <param name="value">The value: 14-bit for two-byte params, 7-bit for single-byte params.</param>
    public abstract void SetParameter(int param, int value);

    /// <summary>
    /// Process the effect and <b>OVERWRITES</b> it to the output.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="sampleCount">The amount of samples to mix. This will never be larger than <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent <see cref="Synthesizer"/> instance.</param>
    /// <param name="isInsertion">isInsertion Indicates if the effect is in the insertion mode. If false, only wet output is sent. If true, if the effect has the "Dry/Wet" parameter, it honors it.</param>
    public abstract void Process(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight,
        int sampleCount,
        bool isInsertion);

    /// <summary> Gets a snapshot of this effect processor. </summary>
    /// <returns> A copy of the 16 parameter values (14-bit wide params, 7-bit single-byte params). </returns>
    public abstract int[] GetSnapshot();
}