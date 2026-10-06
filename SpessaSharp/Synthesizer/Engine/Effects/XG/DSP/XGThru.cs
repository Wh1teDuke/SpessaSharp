using SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;

/// <summary>
/// MU128 manual description:
/// Bypass without applying an effect.
/// </summary>
public sealed class XGThru : XGEffectProcessor
{
    private static readonly XGThru Instance = new();
    public static readonly Constructor Builder = (_, _) => Instance;

    public override int Type => 0x40_00;
    
    public override void SetParameter(int param, int value)
    {}

    public override void Process(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight,
        int sampleCount,
        bool isInsertion)
    {
        if (isInsertion)
        {
            // Thru is bypass, not disabled
            inputLeft.CopyTo(outputLeft);
            inputRight.CopyTo(outputRight);
        }
        else
        {
            outputLeft[..sampleCount].Clear();
            outputRight[..sampleCount].Clear();
        }
    }

    public override void Reset()
    {
        // Noop
    }

    public override int[] GetSnapshot() => new int[16];
}