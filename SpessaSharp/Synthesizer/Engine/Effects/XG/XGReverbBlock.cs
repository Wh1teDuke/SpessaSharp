using SpessaSharp.Synthesizer.Engine.Effects.XG.DSP;
using SpessaSharp.Synthesizer.Engine.Effects.XG.Framework;

namespace SpessaSharp.Synthesizer.Engine.Effects.XG;

/// <summary> Represents the XG Reverb Effect block. </summary>
/// <param name="sampleRate"></param>
/// <param name="maxBufferSize"></param>
public sealed class XGReverbBlock(
    int sampleRate, int maxBufferSize): XGSystemEffectBlock(
    Map, XGNoEffect.Builder, 0x01_00, sampleRate, maxBufferSize)
{
    private static readonly Dictionary<int, XGEffectProcessor.Constructor> Map = new()
    { { 0x00_00, XGNoEffect.Builder }, };

    /// <summary>
    /// Process the effect and <b>adds</b> it to the output.
    /// </summary>
    /// <param name="inputLeft">The input buffer to process. It always starts at index 0.</param>
    /// <param name="inputRight">The input buffer to process. It always starts at index 0.</param>
    /// <param name="outputLeft">The left output buffer.</param>
    /// <param name="outputRight">The right output buffer.</param>
    /// <param name="startIndex">The index to start mixing at into the output buffers.</param>
    /// <param name="sampleCount">
    /// The amount of samples to mix. This will never be larger than
    /// <see cref="Synthesizer.Options.MaxBufferSize"/> of the parent
    /// <see cref="Synthesizer"/> instance.
    /// </param>
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