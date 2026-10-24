using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG.Implementation;

/// <summary> Represents the XG Reverb Effect block. </summary>
/// <param name="sampleRate"></param>
/// <param name="maxBufferSize"></param>
public sealed class DefaultXGReverb(
    int sampleRate, int maxBufferSize): DefaultXGSystemEffect(
    Map, XGNoEffect.Builder, 0x01_00, sampleRate, maxBufferSize),
        XGReverbBlock
{
    private static readonly Dictionary<int, XGEffectProcessor.Constructor> Map = new()
    { { 0x00_00, XGNoEffect.Builder }, };

    public void Process(
        ReadOnlySpan<float> inputLeft,
        ReadOnlySpan<float> inputRight,
        Span<float> outputLeft,
        Span<float> outputRight,
        int startIndex,
        int sampleCount)
    {
        Processor.Process(
            inputLeft,
            inputRight,
            OutputLeft,
            OutputRight,
            sampleCount,
            false);
        MixSystemEffect(outputLeft, outputRight, startIndex, sampleCount);
    }
}