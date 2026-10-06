namespace SpessaSharp.Synthesizer.Engine.Effects.Implementation;

public sealed class DelayLine
{
    public float Feedback = 0;
    public float Gain = 1;

    private readonly float[] _buffer;
    private readonly int _bufferLen;
    private int _writeIndex = 0;
        
    // Samples
    private int _time;

    public DelayLine(int maxDelay)
    {
        _buffer = new float[maxDelay];
        _bufferLen = _buffer.Length;
        _time = maxDelay - 5;
    }

    public int Time
    {
        get => _time;
        set
        {
            _time = Math.Min(value, _bufferLen);
            Clear();
        }
    }

    public void Clear() => _buffer.AsSpan().Clear();
        
    /// <summary> OVERWRITES the output </summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <param name="sampleCount"></param>
    public void Process(
        ReadOnlySpan<float> input, Span<float> output, int sampleCount) 
    {
        var writeIndex = _writeIndex;
        var delay = _time;
        var buffer = _buffer.AsSpan();
        var bufferLength = _bufferLen;
        var feedback = Feedback;
        var gain = Gain;

        for (var i = 0; i < sampleCount; i++) 
        {
            // Read
            var readIndex = writeIndex - delay;
            if (readIndex < 0) readIndex += bufferLength;
            var delayed = buffer[readIndex];
            output[i] = delayed * gain;

            // Write
            buffer[writeIndex] = input[i] + delayed * feedback;

            // Then wrap!
            if (++writeIndex >= bufferLength) writeIndex = 0;
        }

        _writeIndex = writeIndex;
    }
}