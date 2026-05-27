using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.Utils;

// Util to handle RIFFChunks with large sdta chunks
internal abstract class RootChunk
{
    public abstract RIFFChunk PeekRIFFChunk();
    public abstract RIFFChunk ReadRIFFChunk();
    public abstract ArraySegment<byte> ReadString(int bytes);
    public abstract RootChunk Slice(long? start = null, long? end = null);
    public abstract ArraySegment<byte> AsSegment();

    public abstract SampleChunk BigSlice(long? count = null);

    public abstract long Offset { get; }

    public sealed class Stream(FileStream stream) : RootChunk
    {
        public override RIFFChunk PeekRIFFChunk() => 
            new Segment(Slice(
                stream.Position, 
                stream.Position + 8, 
                false)).PeekRIFFChunk();

        public override RIFFChunk ReadRIFFChunk()
        {
            var riff = PeekRIFFChunk();
            var size = riff.Size;
            stream.Position -= 8;
            return new Segment(Slice(
                stream.Position, 
                stream.Position + size + 8, 
                false)).ReadRIFFChunk();
        }

        public override ArraySegment<byte> ReadString(int bytes) => 
            new Segment(Slice(
                    stream.Position, 
                    stream.Position + bytes, 
                    false)).ReadString(bytes);

        public override RootChunk Slice(long? start = null, long? end = null)
        {
            var s = start ?? 0;
            var e = end ?? stream.Length;

            if (s <= int.MaxValue && e <= int.MaxValue)
                return new Segment(Slice((int)s, (int)e, true));
            
            var view = new FileStream(stream.SafeFileHandle, FileAccess.Read);
            view.Position = s;
            return new Stream(view);
        }
        
        private ArraySegment<byte> Slice(long start, long end, bool peek)
        {
            var l = end - start;
            var oldPosition = stream.Position;
            stream.Position = start;
            var buff = new byte[l];

            if (stream.Read(buff) < l) 
                throw SpessaException.ParsingSoundBank(
                    new EndOfStreamException());

            if (peek) stream.Position = oldPosition;
            return buff;
        }

        public override ArraySegment<byte> AsSegment() =>
            throw new InvalidOperationException();

        public override SampleChunk BigSlice(long? count = null)
        {
            var len = count ?? stream.Length - stream.Position;
            var view = new FileStream(stream.SafeFileHandle, FileAccess.Read);
            view.Position = stream.Position;
            return SampleChunk.Of(view);
        }

        public override long Offset => stream.Position;
    }

    public sealed class Segment(ArraySegment<byte> buffer) : RootChunk
    {
        private ArraySegment<byte> _buffer = buffer;

        public override RIFFChunk PeekRIFFChunk() => 
            RIFFChunk.Read(ref _buffer, false);

        public override RIFFChunk ReadRIFFChunk() =>
            RIFFChunk.Read(ref _buffer);

        public override ArraySegment<byte> ReadString(int bytes) => 
            Util.ReadBinaryString(ref _buffer, bytes);

        public override RootChunk Slice(long? start = null, long? end = null) =>
            new Segment(_buffer[
                (int)(start - _buffer.Offset ?? 0) ..
                (int)(end   - _buffer.Offset ?? _buffer.Count)]);

        public override ArraySegment<byte> AsSegment() => _buffer;

        public override SampleChunk BigSlice(long? count = null) =>
            SampleChunk.Of(_buffer[.. (int)(count ?? _buffer.Count)]);

        public override long Offset => _buffer.Offset;
    }
}