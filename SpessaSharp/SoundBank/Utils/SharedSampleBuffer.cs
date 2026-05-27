namespace SpessaSharp.SoundBank.Utils;

public static class SharedSampleBuffer
{
    private const int _defBuffSize = 1/*MB*/ * 1024/*KB*/ * 1024/*B*/;
    private static readonly Lock _lock = new();
    private static float[]? _buffer;
    private static int _index;
    
    public static ArraySegment<float> New(int len)
    {
        using var _ = _lock.EnterScope();

        while (true)
        {
            _buffer ??= new float[Math.Max(len, _defBuffSize)];

            if (_buffer.Length - _index < len)
            {
                _buffer = null;
                _index = 0;
                continue;
            }

            break;
        }

        var newSeg = new ArraySegment<float>(_buffer, _index, len);
        _index += len;

        return newSeg;
    }
}