using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Read;

internal static class ReaderXMF
{
    public enum MetaDataType
    {
        XMFFileType, NodeName, NodeIDNumber, ResourceFormat, FilenameOnDisk,
        FilenameExtensionOnDisk, MacOSFileTypeAndCreation, MimeType,
        Title, CopyrightNotice, Comment,
        /// <summary> Node Name of the FileNode containing the SMF image to autostart when the XMF file loads </summary>
        AutoStart,
        /// <summary> Used to preload specific SMF and DLS file images. </summary>
        Preload,
        /// <summary> RP-42a (https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp42.pdf) </summary>
        ContentDescription,
        /// <summary> RP-47 (https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp47.pdf) </summary>
        ID3MetaData,
    }

    public static readonly int[] MetaDataTypeID =
    [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];
    
    public static readonly string[] MetaDataTypeName =
        Enum.GetNames<MetaDataType>();

    public enum ReferenceType
    {
        InLineResource, InFileResource, InFileNode, ExternalFile, ExternalXMF,
        XMFFileURIAndNodeID,
    }
    
    public static readonly int[] ReferenceTypeID =
        [1, 2, 3, 4, 5, 6];
    
    public static ReferenceType ReferenceTypeOf(int id)
    {
        var idx = ReferenceTypeID.IndexOf(id);
        ArgumentOutOfRangeException
            .ThrowIfNegative(idx, "Unknown reference type");
        return (ReferenceType)idx;
    }

    public enum ResourceFormat
    {
        StandardMIDIFile, StandardMIDIFileType1, 
        DLS1, DLS2, DLS2_2, MobileDLS,
        Unknown, Folder,
    }
    
    public static readonly int[] ResourceFormatID =
        [0, 1, 2, 3, 4, 5, -1, -2];
    
    public static ResourceFormat ResourceFormatOf(int id)
    {
        var idx = ResourceFormatID.IndexOf(id);
        ArgumentOutOfRangeException
            .ThrowIfNegative(idx, "Unknown resource format");
        return (ResourceFormat)idx;
    }

    public enum FormatType
    { Standard, MMA, Registered, NonRegistered, }

    public static readonly int[] FormatTypeID =
        [0, 1, 2, 3];
    
    public static FormatType FormatTypeOf(int id)
    {
        var idx = FormatTypeID.IndexOf(id);
        ArgumentOutOfRangeException
            .ThrowIfNegative(idx, "Unknown format type");
        return (FormatType)idx;
    }

    public enum Unpacker
    { None, MMAUnpacker, Registered, NonRegistered, }

    public readonly record struct InternalUnpackerType(
        Unpacker ID,
        int? StandardID = null,
        int? ManufacturerID = null,
        int? ManufacturerInternalID = null,
        int DecodedSize = -1);

    private sealed class XMFNode
    {
        public int Length { get; }
        /// <summary> 0 means it's a file node </summary>
        public int ItemCount { get; }
        public bool IsFile => ItemCount == 0;
        public int MetadataLength { get; }
        public readonly Dictionary<string, object> MetaData = new(
            StringComparer.InvariantCultureIgnoreCase);
        public ArraySegment<byte> NodeData { get; }
        public readonly List<XMFNode> InnerNodes = [];
        public bool PackedContent { get; }
        public readonly List<InternalUnpackerType> NodeUnpackers = [];
        public ResourceFormat ResourceFormat { get; } =
            ResourceFormat.Unknown;
        public ReferenceType ReferenceType { get; }
        

        public XMFNode(ref ArraySegment<byte> binaryData)
        {
            var nodeStart = binaryData;
            Length = Util.ReadVLengthQuantity(ref binaryData);
            ItemCount = Util.ReadVLengthQuantity(ref binaryData);
            
            // Header Length
            var headerLength = Util.ReadVLengthQuantity(ref binaryData);
            var readBytes = binaryData.Offset - nodeStart.Offset;

            var remainingHeader = headerLength - readBytes;
            var headerData = Util.SliceThenInc(
                ref binaryData, remainingHeader);
            
            MetadataLength = Util.ReadVLengthQuantity(ref headerData);

            var metadataChunk = Util.SliceThenInc(
                ref headerData, MetadataLength);

            while (metadataChunk.Count > 0)
            {
                var firstSpecifierByte = metadataChunk[0];
                string? key = null;
                if (firstSpecifierByte == 0)
                {
                    metadataChunk = metadataChunk[1..];
                    int? fieldSpecifier = Util.ReadVLengthQuantity(ref metadataChunk);
                    if (MetaDataTypeID.IndexOf(fieldSpecifier.Value) 
                        is var idx && idx != -1)
                    {
                        key = MetaDataTypeName[idx];
                    }
                    else
                    {
                        Debug.WriteLine($"Unknown field specifier: {fieldSpecifier}");
                        key = $"unknown_{fieldSpecifier}";
                    }
                }
                else
                {
                    // This is the length of string
                    var strLen = Util.ReadVLengthQuantity(ref metadataChunk);
                    var fieldSpecifierStr = Util.ToString(
                        Util.ReadBinaryString(ref metadataChunk, strLen));
                    key = fieldSpecifierStr;
                }
                
                var numberOfVersions = Util.ReadVLengthQuantity(
                    ref metadataChunk);
                if (numberOfVersions == 0)
                {
                    var dataLength = Util.ReadVLengthQuantity(ref metadataChunk);
                    var contentsChunk = Util.SliceThenInc(
                        ref metadataChunk, dataLength);
                    var formatID = Util.ReadVLengthQuantity(ref contentsChunk);
                    // Text only
                    MetaData[key] = formatID < 4
                        ? Util.ToString(Util.ReadBinaryString(
                            ref contentsChunk, dataLength - 1))
                        : contentsChunk;
                }
                else
                {
                    // Throw new Error ("International content is not supported.");
                    // Skip the number of versions
                    Debug.WriteLine(
                        $"International content: {numberOfVersions}");
                    // Length in bytes
                    // Skip the whole thing!
                    var len = Util.ReadVLengthQuantity(ref metadataChunk);
                    metadataChunk = metadataChunk[len..];
                }
            }

            var unpackerStart = headerData;
            var unpackersLen = Util.ReadVLengthQuantity(ref headerData);
            var diff = headerData.Offset - unpackerStart.Offset;

            headerData = unpackerStart[unpackersLen..];

            if (unpackersLen > 0)
            {
                var unpackersData = 
                    unpackerStart.Slice(diff, unpackersLen);
                PackedContent = true;
                while (unpackersData.Count > 0)
                {
                    var unpackerID = (Unpacker)Util.ReadVLengthQuantity(
                        ref unpackersData);
                    var unpacker = new InternalUnpackerType(
                        ID: unpackerID);
                    
                    switch (unpackerID)
                    {
                        case Unpacker.NonRegistered:
                        case Unpacker.Registered:
                            throw SpessaException.ParsingMidi(
                                $"Unsupported unpackerID: {unpackerID}");
                        case Unpacker.None:
                            unpacker = unpacker with { StandardID =
                                Util.ReadVLengthQuantity(ref unpackersData) };
                            break;
                        case Unpacker.MMAUnpacker:
                            var manufacturerID =
                                unpackersData[0];
                            unpackersData = unpackersData[1..];
                            // One or three byte form, depending on if the first byte is zero
                            if (manufacturerID == 0) 
                            {
                                manufacturerID <<= 8;
                                manufacturerID |= unpackersData[0];
                                unpackersData = unpackersData[1..];
                                manufacturerID <<= 8;
                                manufacturerID |= unpackersData[0];
                                unpackersData = unpackersData[1..];
                            }
                            var manufacturerInternalID =
                                Util.ReadVLengthQuantity(ref unpackersData);
                            unpacker = unpacker with
                            {
                                ManufacturerID = manufacturerID, 
                                ManufacturerInternalID = manufacturerInternalID
                            };
                            break;
                        default:
                            throw SpessaException.ParsingMidi(
                                $"Unknown unpacker ID: {unpackerID}");
                    }

                    unpacker = unpacker with { DecodedSize = 
                        Util.ReadVLengthQuantity(ref unpackersData) };
                    NodeUnpackers.Add(unpacker);
                }
            }
            
            binaryData = nodeStart[headerLength..];
            ReferenceType = ReferenceTypeOf(
                Util.ReadVLengthQuantity(ref binaryData));
            var newLen = (nodeStart.Offset + Length) - binaryData.Offset;
            NodeData = binaryData[..newLen];
            binaryData = nodeStart[newLen..];

            switch (ReferenceType)
            {
                case ReferenceType.InLineResource:
                    break;

                case ReferenceType.ExternalXMF:
                case ReferenceType.InFileNode:
                case ReferenceType.XMFFileURIAndNodeID:
                case ReferenceType.ExternalFile:
                case ReferenceType.InFileResource:
                    throw SpessaException.ParsingMidi(
                        $"Unsupported reference type: {ReferenceType}");
                default:
                    throw SpessaException.ParsingMidi(
                        $"Unknown reference type: {ReferenceType}");
            }
            
            // Read the data
            if (IsFile)
            {
                if (PackedContent)
                {
                    var compressed = NodeData[2..];
                    Debug.WriteLine(
                        $"[WARN] Packed content. Attempting to deflate. Target size: {
                            NodeUnpackers[0].DecodedSize}");
                    try
                    {
                        using var stream = new MemoryStream(
                            compressed.Array!, 
                            compressed.Offset, 
                            compressed.Count);
                        using var output = new MemoryStream();
                        using var deflate = new DeflateStream(
                            stream, CompressionMode.Decompress);
                        deflate.CopyTo(output);
                        NodeData = output.ToArray();
                    }
                    catch (Exception e)
                    {
                        throw SpessaException.ParsingMidi(
                            $"Error unpacking XMF file contents: {e.Message}");
                    }
                }
                
                // Interpret the content
                if (!(MetaData.TryGetValue("resourceFormat", out var resFormat) &&
                      resFormat is ArraySegment<byte> rf))
                    Debug.WriteLine("[WARN] No resource format for this file node!");
                else
                {
                    var formatTypeID = rf[0];

                    if (formatTypeID != FormatTypeID[(int)FormatType.Standard])
                        Debug.WriteLine(
                            $"Non-standard formatTypeID: {formatTypeID}");

                    var resourceFormatID = rf[1];
                    if (ResourceFormatID.Contains(resourceFormatID))
                        ResourceFormat = ResourceFormatOf(resourceFormatID);
                    else
                        Debug.WriteLine(
                            $"Unrecognized resource format: {resourceFormatID}");
                }
            }
            else
            {
                // Folder node
                ResourceFormat = ResourceFormat.Folder;
                var nData = NodeData;
                while (nData.Count > 0)
                {
                    var nStart = nData;
                    var nodeLen = Util.ReadVLengthQuantity(ref nData);
                    var nodeData2 = Util.SliceThenInc(ref nStart, nodeLen);

                    nData = nStart;
                    InnerNodes.Add(new XMFNode(ref nodeData2));
                }
                NodeData = nData;
            }
        }
    }

    /// <summary>
    /// Parses an XMF file
    /// </summary>
    /// <param name="midi"></param>
    /// <param name="binaryData"></param>
    /// <param name="fileName"></param>
    /// <exception cref="Exception"></exception>
    public static void Load(
        Midi midi, ArraySegment<byte> binaryData, string? fileName)
    {
        midi.BankOffset = 0;
        // https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/xmf-v1a.pdf
        // https://wiki.multimedia.cx/index.php?title=Extensible_Music_Format_(XMF)
        var sanityCheck = Util.ReadBinaryString(ref binaryData, 4);
        
        if (!Ascii.Equals(sanityCheck, "XMF_"))
            throw SpessaException.ParsingMidi(
                $"Invalid XMF Header! Expected 'XMF_', got '{
                    Util.ToString(sanityCheck)}");

        Debug.WriteLine("Parsing XMF file ...");
        var version = Util.ReadBinaryString(ref binaryData, 4);
        
        Debug.WriteLine($"XMF version: {Util.ToString(version)}");

        // https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp43.pdf
        // Version 2.00 has additional bytes
        if (Ascii.Equals(version, "2.00")) 
        {
            var fileTypeId = Util.ReadBigEndian(ref binaryData, 4);
            var fileTypeRevisionId = Util.ReadBigEndian(ref binaryData, 4);

            Debug.WriteLine(
                $"File Type ID: {
                    fileTypeId}, File Type Revision ID: {fileTypeRevisionId}");
        }
        
        // File length
        /*discard*/Util.ReadVLengthQuantity(ref binaryData);

        var metadataTableLen = Util.ReadVLengthQuantity(ref binaryData);
        // Skip metadata
        binaryData = binaryData[metadataTableLen..];
        
        // Skip to tree root
        var tr = Util.ReadVLengthQuantity(ref binaryData);
        var bda = binaryData.Array!;
        binaryData = new ArraySegment<byte>(bda, tr, bda.Length - tr);
        var rootNode = new XMFNode(ref binaryData);
        ArraySegment<byte>? midiArray = null;
        
        // Find the stuff we care about
        SearchNode(rootNode);
        
        if (midiArray == null)
            throw SpessaException.ParsingMidi("No MIDI data in the XMF file!");
        
        // Send the extracted SMF to the parser
        ReaderMidi.Load(midi, midiArray.Value, fileName);
        return;

        void SearchNode(XMFNode node)
        {
            // Meta
            CheckMeta("nodeName", RMidi.Info.Key.Name);
            CheckMeta("title", RMidi.Info.Key.Name);
            CheckMeta("copyrightNotice", RMidi.Info.Key.Copyright);
            CheckMeta("comment", RMidi.Info.Key.Comment);

            if (node.IsFile)
            {
                switch (node.ResourceFormat)
                {
                    case ResourceFormat.DLS1:
                    case ResourceFormat.DLS2:
                    case ResourceFormat.DLS2_2:
                    case ResourceFormat.MobileDLS:
                        Debug.Write("Found embedded DLS!");
                        midi.EmbeddedSoundBank = node.NodeData;
                        break;

                    case ResourceFormat.StandardMIDIFile:
                    case ResourceFormat.StandardMIDIFileType1:
                        Debug.WriteLine("Found embedded MIDI!");
                        midiArray = node.NodeData;
                        break;

                    case ResourceFormat.Unknown:
                    case ResourceFormat.Folder:
                    default: return;
                }
            }
            else
            {
                foreach (var n in node.InnerNodes)
                    SearchNode(n);
            }

            return;

            void CheckMeta(string xmf, RMidi.Info.Key rmid)
            {
                Debug.Assert(rmid != RMidi.Info.Key.Picture);
                Debug.Assert(rmid != RMidi.Info.Key.CreationDate);
                
                if (node.MetaData.TryGetValue(
                    xmf, out var meta) && meta is string value)
                {
                    midi.RmidiInfo[rmid] = Util.GetStringBytes(value);
                }
            }
        }
    }
}