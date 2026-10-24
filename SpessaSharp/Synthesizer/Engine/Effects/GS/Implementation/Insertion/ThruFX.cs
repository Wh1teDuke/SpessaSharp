using SpessaSharp.Synthesizer.Engine.Effects.GS.Interface;

namespace SpessaSharp.Synthesizer.Engine.Effects.GS.Implementation.Insertion;

public sealed class ThruFX: GSInsertionProcessor
{
    public int Type => 0x00_00;
    public float SendLevelToReverb { get; set; } = 40f / 127f;
    public float SendLevelToChorus { get; set; } = 0;
    public float SendLevelToDelay { get; set; } = 0;

    public void Reset()
    { 
        // Empty
    }
    
    public void Process(
        ReadOnlySpan<float> inputLeft, 
        ReadOnlySpan<float> inputRight, 

        Span<float> outputLeft, 
        Span<float> outputRight, 
        Span<float> outputReverb,
        Span<float> outputChorus, 
        Span<float> outputDelay,

        int startIndex, 
        int sampleCount)
    {
        var (sendLevelToReverb, sendLevelToChorus, sendLevelToDelay) =
            (SendLevelToReverb, SendLevelToChorus, SendLevelToDelay);

        for (var i = 0; i < sampleCount; i++) 
        {
            var sL = inputLeft[i];
            var sR = inputRight[i];
            var idx = startIndex + i;
            outputLeft[idx] += sL;
            outputRight[idx] += sR;

            var mono = (sL + sR) * .5f;
            outputReverb[i] += mono * sendLevelToReverb;
            outputChorus[i] += mono * sendLevelToChorus;
            outputDelay[i] += mono * sendLevelToDelay;
        }
    }

    public void SetParameter(int parameter, int value)
    {
        // discard parameter
        // discard value
    }
}