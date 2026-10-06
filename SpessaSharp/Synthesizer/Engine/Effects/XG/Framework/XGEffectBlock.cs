using System.Numerics.Tensors;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;


/// <summary>
/// This class represents a single XG effect block.
/// Each block contains a specific processor and may be an insertion or system effect.
/// This class does the mixing and routing of the effect.
/// </summary>
public abstract class XGEffectBlock
{
    /// <summary> A snapshot of a <see cref="XGEffectBlock"/>. </summary>
    public class Snapshot
    {
        /// <summary> The 16-bit effect type. </summary>
        public required int Type;
        /// <summary> The 16 parameter values (14-bit wide params, 7-bit single-byte params). </summary>
        public required int[] Params;
    }
    
    /// <summary> The currently used processor. </summary>
    protected XGEffectProcessor Processor;

    /// <summary>
    /// Staging buffers for the raw DSP output, before block mixing.
    /// Always 0-based, up to `maxBufferSize` samples.
    /// </summary>
    protected readonly float[] OutputLeft;
    protected readonly float[] OutputRight;

    private readonly Dictionary<int, XGEffectProcessor> _effectMap = [];
    private readonly XGEffectProcessor _fallbackProcessor;

    private readonly int _defaultType;
    private int _type;

    protected XGEffectBlock(
        Dictionary<int, XGEffectProcessor.Constructor> effectMap,
        XGEffectProcessor.Constructor fallbackProcessor,
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
    public virtual Snapshot GetSnapshot() => 
        new()
        {
            Type = Type,
            Params = Processor.GetSnapshot(),
        };

    /// <summary> Restores this XG effect block from a snapshot. </summary>
    /// <param name="snapshot"> The snapshot to restore. </param>
    protected void ApplySnapshot(Snapshot snapshot)
    {
        SetType(snapshot.Type);
        var sParams = snapshot.Params;
        for (var i = 0; i < 16 && i < sParams.Length; i++)
            SetParameter(i, sParams[i]);
    }
}

public abstract class XGSystemEffectBlock(
    Dictionary<int, XGEffectProcessor.Constructor> effectMap,
    XGEffectProcessor.Constructor fallbackProcessor,
    int defaultType,
    int sampleRate,
    int maxBufferSize)
    : XGEffectBlock(effectMap, fallbackProcessor, defaultType, sampleRate, maxBufferSize)
{
    /// <summary> A snapshot of a <see cref="XGEffectBlockSnapsot"/> </summary>
    public class Snapshot: XGEffectBlock.Snapshot
    {
        /// <summary> The return (level) of the effect. </summary>
        public required int ReturnLevel;
        /// <summary> The stereo panning of the effect. </summary>
        public required int Pan;
    }
    
    private const int MIN_PAN = 1;
    private const int MAX_PAN = 127;
    private const int PAN_RESOLUTION = MAX_PAN - MIN_PAN;

    private static readonly float[] PanTableLeft = new float[PAN_RESOLUTION + 1];
    private static readonly float[] PanTableRight = new float[PAN_RESOLUTION + 1];

    // Initialize pan lookup tables
    static XGSystemEffectBlock()
    {
        for (var pan = MIN_PAN; pan <= MAX_PAN; pan++) 
        {
            // Clamp to 0-1
            var realPan = (pan - MIN_PAN) / (float)PAN_RESOLUTION;
            var tableIndex = pan - MIN_PAN;
            var (s, c) = MathF.SinCos((MathF.PI / 2) * realPan);
            PanTableLeft[tableIndex] = c;
            PanTableRight[tableIndex] = s;
        }
    }
    
    /// <summary>
    /// The return (level) of the effect. 0 is silence (-Inf dB), 64 is normal (0 dB) and 127 is double the volume (+6 dB).
    /// </summary>
    public int ReturnLevel = 64;

    /// <summary>
    /// The stereo panning of this effect. 1 is hard left, 64 is center, 127 is hard right.
    /// </summary>
    public int Pan = 64;

    public virtual void Reset()
    {
        base.Reset();
        ReturnLevel = 64;
        Pan = 64;
    }

    public override Snapshot GetSnapshot()
    {
        var snapshot = base.GetSnapshot();
        return new Snapshot
        {
            Pan = Pan,
            ReturnLevel = ReturnLevel,
            Type = snapshot.Type,
            Params = snapshot.Params,
        };
    }

    public void ApplySnapshot(Snapshot snapshot) 
    {
        base.ApplySnapshot(snapshot);
        ReturnLevel = snapshot.ReturnLevel;
        Pan = snapshot.Pan;
    }

    /// <summary>
    /// Mixes the staged wet output into the system effect destination, applying return and pan.
    /// </summary>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    protected void MixSystemEffect(
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        var gain = ReturnLevel / 64f;
        var panIndex = Math.Clamp(Pan, MIN_PAN, MAX_PAN) - MIN_PAN;
        var gainLeft = PanTableLeft[panIndex] * gain;
        var gainRight = PanTableRight[panIndex] * gain;
        
        var outLeft = OutputLeft.AsSpan(startIndex, sampleCount);
        var outRight = OutputRight.AsSpan(startIndex, sampleCount);
        
        var destLeft = outputLeft.Slice(startIndex, sampleCount);
        var destRight = outputRight.Slice(startIndex, sampleCount);

        TensorPrimitives.MultiplyAdd(
            outLeft, gainLeft, destLeft, destLeft);
        TensorPrimitives.MultiplyAdd(
            outRight, gainRight, destRight, destRight);
    }

    /// <summary>
    /// Adds the wet output into a send buffer (e.g. chorus into reverb).
    /// Send reads from the wet signal before return, so it sounds even at return 0.
    /// Mixing always starts at index 0.
    /// </summary>
    /// <param name="outputLeft">The left send buffer.</param>
    /// <param name="outputRight">The right send buffer.</param>
    /// <param name="sampleCount">The amount of samples to mix.</param>
    /// <param name="send">The send amount, where 64 is 100%;</param>
    protected void AddSend(
        Span<float> outputLeft, 
        Span<float> outputRight,
        int sampleCount,
        int send)
    {
        // Common scenario
        if (send == 0) return;
        var gain = send / 64f;
        
        var outLeft = OutputLeft.AsSpan(0, sampleCount);
        var outRight = OutputRight.AsSpan(0, sampleCount);

        TensorPrimitives.MultiplyAdd(
            outLeft, gain, outputLeft, outputLeft);
        TensorPrimitives.MultiplyAdd(
            outRight, gain, outputRight, outputRight);
    }
}