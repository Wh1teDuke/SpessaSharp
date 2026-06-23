using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SpessaSharp.Utils;

/*
 Example of a RIFFChunk (Sound Font)
 RIFF(sfbk)-> (39900960 Bytes)
   LIST(INFO)-> (412 Bytes)
               ifil; (4 Bytes)
               INAM; (24 Bytes)
               isng; (10 Bytes)
               IPRD; (8 Bytes)
               IENG; (82 Bytes)
               ISFT; (10 Bytes)
               ICRD; (32 Bytes)
               ICMT; (96 Bytes)
               ICOP; (74 Bytes)
   LIST(sdta)-> (39720040 Bytes)
               smpl; (39720032 Bytes)
   LIST(pdta)-> (180472 Bytes)
               phdr; (11780 Bytes)
               pbag; (4904 Bytes)
               pmod; (7520 Bytes)
               pgen; (20864 Bytes)
               inst; (4510 Bytes)
               ibag; (10540 Bytes)
               imod; (10040 Bytes)
               igen; (52880 Bytes)
               shdr; (57362 Bytes) 
 */


/// <summary> Represents a RIFF chunk. </summary>
/// <param name="Header">The chunks FourCC code.</param>
/// <param name="Size">Chunk's size, in bytes.</param>
/// <param name="Data">Chunk's binary data. Note that this will have a length of 0 if "readData" was set to false.</param>
/// <param name="HeaderSize">The size of the chunk's header in bytes. This varies for 32-bit and 64-bit RIFF chunks.</param>
internal readonly record struct RIFFChunk(
    RIFFChunk.FourCC Header,
    int Size,
    ArraySegment<byte> Data,
    int HeaderSize = 8)
{
    public readonly record struct FourCC(string Str)
    {
        public enum Type
        {
            GenericRIFF, WAV, SoundBankInfo, SF2Info, SF2Chunk,
            DLSInfo, DLSChunk, RMIDInfo,
        }
        
        private static readonly Type[] Types = Enum.GetValues<Type>();

        public new Type? GetType()
        {
            foreach (var t in Types)
                if (Is(t)) return t;
            return null;
        }

        public bool Is(Type t) => t switch
        {
            Type.GenericRIFF => Str is "RIFF" or "RIFS" or "LIST" or "INFO",
            Type.WAV => Str is "wave" or "cue " or "fmt ",
            Type.SoundBankInfo => Str is "name" or "version" or "creationDate"
                or "soundEngine" or "engineer" or "product" or "copyright" or "comment"
                or "subject" or "romInfo" or "software" or "romVersion",
            Type.SF2Info => Str is "ifil" or "isng" or "irom" or "iver"
                or "DMOD" or "LIST" || Is(Type.GenericRIFF),
            Type.SF2Chunk => Str is "pdta" or "xdta" or "sdta"
                or "smpl" or "sm24" or "phdr" or "pbag" or "pmod"
                or "pgen" or "inst" or "ibag" or "imod" or "igen"
                or "shdr",
            Type.DLSInfo => Str is "ISBJ" || Is(Type.GenericRIFF),
            Type.DLSChunk => Str is "dls " or "dlid" or "cdl "
                or "ptbl" or "vers" or "colh" or "wvpl" or "wsmp"
                or "data" or "lart" or "lar2" or "art2" or "art1"
                or "lrgn" or "rgnh" or "wlnk" or "lins" or "ins "
                or "insh" or "rgn " or "rgn2"
                // Proprietary MobileBAE instrument aliasing chunk
                or "pgal" || Is(Type.WAV),
            Type.RMIDInfo => Str is "INAM" // Name
                or "IPRD" // Album
                or "IALB" // Album two
                or "IART" // Artist
                or "IGNR" // Genre
                or "IPIC" // Picture
                or "ICOP" // Copyright
                or "ICRD" // Creation date
                or "ICRT" // Creation date (old spessasynth)
                or "ICMT" // Comment
                or "IENG" // Engineer
                or "ISFT" // Software
                or "ISBJ" // Subject
                or "IENC" // Info encoding
                or "MENC" // MIDI encoding
                or "DBNK", // Bank offset
            _ => throw new ArgumentOutOfRangeException(nameof(t), t, null)
        };

        public override string ToString() => Str;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EqualsIgnoreCase(FourCC other) =>
            string.Equals(
                Str, other.Str, StringComparison.InvariantCultureIgnoreCase);

        public static bool operator ==(FourCC x, string y) => x.Str == y;
        public static bool operator !=(FourCC x, string y) => !(x == y);
        
        public static bool operator ==(FourCC x, ReadOnlySpan<byte> y) => Ascii.Equals(x.Str, y);
        public static bool operator !=(FourCC x, ReadOnlySpan<byte> y) => !(x == y);
    }

    public readonly record struct DLSFourCC(string Str);
    
    internal readonly ArraySegment<byte> Base = Data;
    
    /// <summary> Reads a RIFF chunk from an array. </summary>
    /// <param name="dataArray">The array to read from.</param>
    /// <param name="rf64">If the chunk uses a 64-bit size.</param>
    /// <param name="readData">If the data should be read as well.</param>
    /// <returns></returns>
    public static RIFFChunk Read(
        ref ArraySegment<byte> dataArray,
        bool rf64 = false,
        bool readData = true)
    {
        if (dataArray.Count < 8)
        {
            dataArray = [];
            return new RIFFChunk(new FourCC(""), 0, dataArray);
        }
        
        var header = new FourCC(Util.ToString(
            Util.ReadBinaryString(ref dataArray, 4)));

        int size;
        if (rf64)
        {
            // SpessaSharp implementation note:
            // For the first massive chunk this will overflow, but it's being discarded anyway.
            // Let's hope there are no issues in the future.
            var s = Util.ReadLittleEndian64(ref dataArray, 8);
            size = (int)s;
        }
        else
            size = Util.ReadLittleEndian(ref dataArray, 4);
        
        // Not all RIFF files are compliant
        if (header.Str.IsWhiteSpace())
        {
            // Safeguard against evil DLS files
            // The test case: CrysDLS v1.23.dls
            // https://github.com/spessasus/spessasynth_core/issues/5
            size = 0;
        }

        var chunkData = readData ? dataArray[..size] : [];

        if (readData)
        {
            dataArray = dataArray[size..];
            if (size % 2 != 0) dataArray = dataArray[1..];
        }
        
        return new RIFFChunk(header, size, chunkData, rf64 ? 12 : 8);
    }

    /// <summary>Writes a RIFF chunk correctly</summary>
    /// <param name="header">The fourCC code of the header.</param>
    /// <param name="data">Chunk data</param>
    /// <param name="rf64">If the chunk uses a 64-bit size.</param>
    /// <param name="isList">Adds "LIST" as the chunk type and writes the actual type at the start of the data</param>
    /// <returns>The binary data</returns>
    /// <exception cref="Exception"></exception>
    public static ArraySegment<byte> Write(
        FourCC header,
        ReadOnlySpan<byte> data,
        bool rf64 = false,
        bool isList = false)
    {
        if (header.Str.Length != 4)
            throw new ArgumentException(
                $"Invalid header length: {header.Str.Length}");

        // FourCC + 8 bytes for 64-bit size
        var dataStartOffset = rf64 ? 12 : 8;
        var headerWritten = header.Str;
        var dataLen = data.Length;
        var writtenSize = dataLen;
        if (isList)
        {
            // Written header is LIST and the passed header is the first 4 bytes of chunk data
            dataStartOffset += 4;
            writtenSize     += 4;
            headerWritten   = "LIST";
        }

        var finalSize = dataStartOffset + dataLen;
        if (finalSize % 2 != 0)
        {
            // Pad byte does not get included in the size
            finalSize++;
        }
        
        var outArray = new byte[finalSize];
        var outSeg = (ArraySegment<byte>)outArray;
        // FourCC ("RIFF", "LIST", "pdta" etc.)
        Util.WriteBinaryString(ref outSeg, headerWritten);

        // Chunk size
        Debug.Assert(writtenSize > 0);
        if (rf64) Util.WriteQword(ref outSeg, writtenSize);
        else Util.WriteDword(ref outSeg, writtenSize);
        if (isList)
        {
            // List type (e.g. "INFO")
            Util.WriteBinaryString(ref outSeg, header.Str);
        }

        data.CopyTo(outArray.AsSpan(dataStartOffset));
        return outArray;
    }

    /// <summary>Writes RIFF chunk given binary blobs. </summary>
    /// <param name="header">The fourCC code of the header.</param>
    /// <param name="chunks">Binary chunk data parts, will be combined in order.</param>
    /// <param name="isList">If a "LIST" should be set as the chunk type and the actual type should be written at the start of the data.</param>
    /// <param name="rf64">If the chunk uses a 64-bit size.</param>
    /// <returns>The binary data</returns>
    public static ArraySegment<byte> WriteParts(
        FourCC header,
        ReadOnlySpan<ArraySegment<byte>> chunks,
        bool rf64 = false,
        bool isList = false)
    {
        // FourCC + 8 bytes for 64-bit size
        var dataOffset = rf64 ? 12 : 8;
        var headerWritten = header.Str;
        var dataLen = Util.Sum(chunks, s => s.Count);
        var writtenSize = dataLen;

        if (isList)
        {
            // Written header is LIST and the passed header is the first 4 bytes of chunk data
            dataOffset += 4;
            writtenSize += 4;
            headerWritten = "LIST";
        }
        
        var finalSize = dataOffset + dataLen;
        if (finalSize % 2 != 0)
        {
            // Pad byte does not get included in the size
            finalSize++;
        }

        var outArray = new byte[finalSize];
        var outSeg = (ArraySegment<byte>)outArray;
        
        // FourCC ("RIFF", "LIST", "pdta" etc.)
        Util.WriteBinaryString(ref outSeg, headerWritten);
        
        // Chunk size
        if (rf64) Util.WriteQword(ref outSeg, writtenSize);
        else Util.WriteDword(ref outSeg, writtenSize);
        if (isList)
        {
            // List type (e.g. "INFO")
            Util.WriteBinaryString(ref outSeg, header.Str);
        }

        foreach (var c in chunks)
        {
            c.CopyTo(outSeg);
            dataOffset += c.Count;
            outSeg = outSeg[c.Count..];
        }

        return outArray;
    }
    
    /// <summary>'GetParts' + 'WriteParts'</summary>
    internal static void WriteParts(
        FourCC header,
        ReadOnlySpan<
            object/*ArraySegment<byte>|List<ArraySegment<byte>>*/> chunks,
        bool rf64,
        Stream stream)
    {
        var dataOffset = rf64 ? 12L : 8L;
        var headerWritten = header.Str;
        var dataLen = 0L;

        foreach (var c in chunks)
        {
            dataLen += c switch
            {
                List<ArraySegment<byte>> l => l.Aggregate(
                    0L, (t, s) => t + s.Count),
                ArraySegment<byte> d => d.Count,
                _ => throw new Exception("Impossible: " + c.GetType()),
            };
        }

        var writtenSize = checked((uint)dataLen);// TODO
        var finalSize = dataOffset + dataLen;

        if (finalSize % 2 != 0)
        {
            // Pad byte does not get included in the size
            finalSize++;
        }

        var headerBytes = new byte[rf64 ? 12 : 8];
        var headerSeg = (ArraySegment<byte>)headerBytes;
        
        // FourCC ("RIFF", "LIST", "pdta" etc.)
        Util.WriteBinaryString(ref headerSeg, headerWritten);
        
        // Chunk size
        if (rf64) Util.WriteQword(ref headerSeg, dataLen);
        else Util.WriteDword(ref headerSeg, unchecked((int)writtenSize));
        
        stream.Write(headerBytes);

        foreach (var c in chunks)
        {
            switch (c)
            {
                case List<ArraySegment<byte>> l:
                    foreach (var d in l) Write(d);
                    break;
                case ArraySegment<byte> d:
                    Write(d);
                    break;
                default:
                    throw new Exception("Impossible: " + c.GetType());

                void Write(ArraySegment<byte> seg)
                {
                    stream.Write(seg);
                    dataOffset += seg.Count;
                }
            }
        }

        var rem = (Span<byte>)stackalloc byte[
            (int)(finalSize - stream.Position)];
        rem.Clear();
        stream.Write(rem);
    }

    /// <summary> Finds a given type in a list. </summary>
    /// <param name="collection"></param>
    /// <param name="type"></param>
    /// <remarks>Also skips the current index to after the list FourCC.</remarks>
    /// <returns></returns>
    public static RIFFChunk? FindListType(
        Span<RIFFChunk> collection, FourCC type)
    {
        foreach (ref var c in collection)
        {
            if (!c.Header.Str.Equals("LIST"))
                continue;
            
            c = c with { Data = c.Base[4..] };
            var t = Util.ReadBinaryString(c.Base[..4]);
            if (Ascii.Equals(t, type.Str)) return c;
        }
        
        return null;
    }

    public static RIFFChunk? FindListType(
        List<RIFFChunk> collection,
        FourCC type) => FindListType(
            CollectionsMarshal.AsSpan(collection), type);
}