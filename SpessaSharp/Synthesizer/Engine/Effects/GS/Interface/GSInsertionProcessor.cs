using SpessaSharp.Synthesizer.Engine.Effects.GS.Implementation.Insertion;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

/// <summary>
/// Represents a GS-compatible Insertion EFX processor.
/// </summary>
public interface GSInsertionProcessor
{
    /// <summary>
    /// Constructor for a GS insertion processor.
    /// </summary>
    public delegate GSInsertionProcessor Constructor(
        int sampleRate, int maxBufferSize);
    
    internal static readonly Constructor[] List = 
    [
        (_, _) => new ThruFX(),
        (sr, _) => new StereoEQFX(sr),
        (sr, _) => new PhaserFX(sr),
        (sr, _) => new AutoPanFX(sr),
        (sr, _) => new AutoWahFX(sr),
        (sr, mbs) => new PhAutoWahFX(sr, mbs),
        (sr, _) => new TremoloFX(sr),
    ];
    
    /// <summary>
    /// The EFX type of this processor, stored as <c>MSB &lt;&lt; | LSB</c>.
    /// For example <c>0x30</c>, <c>0x10</c> is <c>0x3010</c>.
    /// </summary>
    public int Type { get; }

    /// <summary>
    /// <c>0-1</c> (floating point)<br/>
    /// This parameter sets the amount of insertion sound that will be sent to the reverb.
    /// Higher values result in more sound being sent.
    /// </summary>
    public float SendLevelToReverb { get; set; }

    /// <summary>
    /// <c>0-1</c> (floating point)<br/>
    /// This parameter sets the amount of insertion sound that will be sent to the chorus.
    /// Higher values result in more sound being sent.
    /// </summary>
    public float SendLevelToChorus { get; set; }

    /// <summary>
    /// <c>0-1</c> (floating point)<br/>
    /// This parameter sets the amount of insertion sound that will be sent to the delay.
    /// Higher values result in more sound being sent.
    /// </summary>
    public float SendLevelToDelay { get; set; }
    
    /// <summary>
    /// Resets the params to their default values.
    /// This does not need to reset send levels.
    /// </summary>
    void Reset();
    
    /// <summary> Sets an EFX parameter. </summary>
    /// <param name="parameter">The parameter number (0x03-0x16).</param>
    /// <param name="value">The new value (0-127).</param>
    void SetParameter(int parameter, int value);
    
    /// <summary> Process the effect and ADDS it to the output. </summary>
    /// <param name="inputLeft">The left input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The right input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="outputReverb">The mono input for reverb. It always starts at index 0.</param>
    /// <param name="outputChorus">The mono input for chorus. It always starts at index 0.</param>
    /// <param name="outputDelay">The mono input for delay. It always starts at index 0.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    void Process(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,

        Span<float> outputLeft,
        Span<float> outputRight,

        Span<float> outputReverb,
        Span<float> outputChorus,
        Span<float> outputDelay,

        int startIndex,
        int sampleCount);
}

/// <summary>
/// Represents stored GS-compatible insertion processor data.
/// </summary>
public sealed class GSInsertionProcessorSnapshot
{
    /// <summary>
    /// The EFX type of this processor, stored as <c>MSB &lt;&lt; | LSB</c>.
    /// For example <c>0x30</c>, <c>0x10</c> is <c>0x3010</c>.
    /// </summary>
    public required int Type;

    /// <summary>
    /// 20 parameters for the effect. These depend on effect type.
    /// After that 3 effect sends follow (index 20, 21, 22),
    /// the length totaling to 23.
    /// Value <c>255</c> means "no change" for that parameter.
    /// </summary>
    public required ArraySegment<byte> Params;
}