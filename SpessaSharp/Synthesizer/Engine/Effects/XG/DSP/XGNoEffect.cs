using SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;

public sealed class XGNoEffect: XGEffectProcessor
{
    private static readonly XGNoEffect Instance = new();
    public static readonly Constructor Builder = (_, _) => Instance; 
    
    public override int Type => 0x00_00;
    
    public override void SetParameter(int param, int value) {}

    public override void Process(
        ReadOnlySpan<float> inputLeft, 
        ReadOnlySpan<float> inputRight, 
        Span<float> outputLeft, 
        Span<float> outputRight, 
        int sampleCount, bool isInsertion)
    {
        // Silence, honoring the OVERWRITE behavior.
        outputLeft[..sampleCount].Clear();
        outputRight[..sampleCount].Clear();
    }

    public override void Reset()
    {
        // Noop
    }

    public override int[] GetSnapshot() => new int[16];
}