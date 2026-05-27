namespace SpessaSharp.Synthesizer.Engine.Effects.Insertion;

public sealed class ThruFX: Effect.InsertionProcessor
{
    public override int Type => 0x00_00;

    public ThruFX()
    {
        SendLevelToReverb = 40f / 127f;
        SendLevelToChorus = 0;
        SendLevelToDelay = 0;
    }

    public override void Reset()
    { 
        // Empty
    }
    
    public override void Process(
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

    public override void SetParameter(int parameter, int value)
    {
        // discard parameter
        // discard value
    }
}