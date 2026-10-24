using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;


/// <summary>
/// The default implementation for <see cref="XGEffectBlock"/>
/// This class represents a single XG effect block.
/// Each block contains a specific processor and may be an insertion or system effect.
/// This class does the mixing and routing of the effect.
/// </summary>
public abstract class DefaultXGEffect: XGEffectBlock
{
    /// <summary> The currently used processor. </summary>
    protected DefaultXGEffectProcessor Processor;

    /// <summary>
    /// Staging buffers for the raw DSP output, before block mixing.
    /// Always 0-based, up to `maxBufferSize` samples.
    /// </summary>
    protected readonly float[] OutputLeft;
    protected readonly float[] OutputRight;

    /// <summary>
    /// The list of effects available in this block. Effect type: processor.
    /// </summary>
    private readonly Dictionary<int, DefaultXGEffectProcessor> _effectMap = [];
    private readonly DefaultXGEffectProcessor _fallbackProcessor;

    private readonly int _defaultType;
    private int _type;

    /// <summary> Initializes the new XG effect block. </summary>
    /// <param name="effectMap">The map of all effect constructors to initialize.</param>
    /// <param name="fallbackProcessor">The fallback processor in case the real processor is missing.</param>
    /// <param name="defaultType">The default effect type.</param>
    /// <param name="sampleRate">The sample rate, in Hertz.</param>
    /// <param name="maxBufferSize">
    /// The maximum buffer size the synthesizer can render at once.
    /// Attempting to <c>.Process()</c> more samples than this will result in an error.
    /// </param>
    protected DefaultXGEffect(
        Dictionary<int, DefaultXGEffectProcessor.Constructor> effectMap,
        DefaultXGEffectProcessor.Constructor fallbackProcessor,
        int defaultType,
        int sampleRate,
        int maxBufferSize)
    {
        foreach (var (key, val) in effectMap)
            _effectMap[key] = val(sampleRate, maxBufferSize);
        
        _fallbackProcessor = fallbackProcessor(sampleRate, maxBufferSize);
        Processor = _effectMap.GetValueOrDefault(
            defaultType, _fallbackProcessor);
        
        _defaultType = defaultType;
        _type = defaultType;

        OutputLeft = new float[maxBufferSize];
        OutputRight = new float[maxBufferSize];
    }
    
    public int Type => _type;

    /// <summary>
    /// Sets the type of the processor.
    /// Per XG spec, BASIC EFFECT (LSB = 0) will be used if the exact match is missing.
    /// If both are missing, fallback will be used.
    /// The newly selected processor is reset to its type defaults,
    /// mirroring the GS insertion behavior.
    /// </summary>
    /// <param name="type">The 16-bit type value to use.</param>
    public void SetType(int type)
    {
        _type = type;
        Processor =
            _effectMap.TryGetValue(type, out var effect)
                ? effect
                : _effectMap.TryGetValue(type & 0xff_00, out effect)
                    ? effect
                    : _fallbackProcessor;
        Processor.Reset();
    }

    /// <summary> Resets this block to default values, including the processor type. </summary>
    public virtual void Reset() => SetType(_defaultType);

    /// <summary> Sets the given effect parameter to the given value. </summary>
    /// <param name="param">The parameter number (0-based).</param>
    /// <param name="value">The value: 14-bit for two-byte params, 7-bit for single-byte params.</param>
    public void SetParameter(int param, int value) =>
        Processor.SetParameter(param, value);

    /// <summary> Gets a snapshot of this XG effect block. </summary>
    /// <returns></returns>
    public virtual XGEffectBlock.Snapshot GetSnapshot() => 
        new()
        {
            Type = Type,
            Params = Processor.GetSnapshot(),
        };

    /// <summary> Restores this XG effect block from a snapshot. </summary>
    /// <param name="snapshot"> The snapshot to restore. </param>
    public void ApplySnapshot(XGEffectBlock.Snapshot snapshot)
    {
        SetType(snapshot.Type);
        var sParams = snapshot.Params;
        for (var i = 0; i < 16 && i < sParams.Length; i++)
            SetParameter(i, sParams[i]);
    }
}