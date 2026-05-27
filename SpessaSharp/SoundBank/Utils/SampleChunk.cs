using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.Utils;

internal sealed class SampleChunk
{
    // From Stream
    private FileStream? _stream;
    private long _streamOff; // Stream Position at the moment of this Segment creation
    private uint _chunkOff;  // Stream Position of this chunk
    private byte[]? _chunk;
    
    // From byte[]
    private ArraySegment<float>? _float;
    // From float[]
    private ArraySegment<byte>? _byte;

    public long Start => _stream != null
        ? _streamOff : _float?.Offset ?? _byte!.Value.Offset;
    
    public bool IsFloat => _float.HasValue;

    public void EnsureChunkSize(long end)
    {
        if (_stream == null) return;

        var len = end - _chunkOff;

        if (_chunk != null && _chunk.Length >= len) return;

        if (_chunk != null)
            _stream.Position -= 10 * 1_024 * 1_024;
            
        var pos = _stream.Position;
        var rem = _stream.Length - pos;
        var newLen = Math.Min(
            rem, 1/*GB*/ * 1_024/*MB*/ * 1_024/*KB*/ * 1_024/*B*/);
        _chunk      = new byte[newLen];
        _chunkOff   = (uint)pos;

        var read = _stream.Read(_chunk);
        if (read != _chunk.Length) 
            throw SpessaException.ParsingSoundBank(new EndOfStreamException());
    }

    public int FixOffset(uint i) => (int)(_stream == null ? i : i - _chunkOff);

    // Absolute position
    public ArraySegment<byte> SliceByte(long start, long end)
    {
        if (_byte is { } seg)
        {
            if (start > int.MaxValue || end > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(start));
            var arr = seg.Array!;
            return new ArraySegment<byte>(arr, (int)start, (int)(end - start));
        }

        start -= _chunkOff;
        end -= _chunkOff;

        return new ArraySegment<byte>(_chunk!, (int)start, (int)(end - start));
    }

    // Absolute position
    public ArraySegment<float> SliceFloat(long start, long end)
    {
        if (start > int.MaxValue || end > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(start));
        
        var seg = _float!.Value;
        return seg[(int)start .. (int)end];
    }

    public static SampleChunk Of(FileStream stream) =>
        new() { _stream = stream, _streamOff = stream.Position, };
    
    public static SampleChunk Of(ArraySegment<float> buffer) =>
        new() { _float = buffer, };
    
    public static SampleChunk Of(ArraySegment<byte> buffer) =>
        new() { _byte = buffer, };


    public static readonly SampleChunk Empty = Of(ArraySegment<byte>.Empty);
}