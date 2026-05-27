using System.Diagnostics;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;

internal static class SDTA
{
    /*
    Sdta structure:

    LIST chunk
    - "sdta" ASCII string
    - smpl chunk
    - - raw data
     */
    
    /// <summary>In bytes, from the start of sdta-LIST to the first actual sample</summary>
    const int SDTA_TO_DATA_OFFSET =
        4 + // "LIST"
        4 + // Sdta size
        4 + // "sdta"
        4 + // "smpl"
        4;  // Smpl size

    public static List<ArraySegment<byte>> Get(
        SoundBank bank,
        List<uint> smplStartOffsets,
        List<uint> smplEndOffsets,
        bool compress,
        bool decompress,
        SoundBank.Encode? encodeFunc = null,
        SoundBank.ProgressFunc? progressFunc = null)
    {
        // Write smpl: write int16 data of each sample linearly
        // Get size (calling getAudioData twice doesn't matter since it gets cached)
        var writtenCount = 0;
        var smplChunkSize = 0L;
        
        var sampleData = new List<ArraySegment<byte>>();

        foreach (var s in bank.Samples)
        {
            if (compress && encodeFunc != null)
                s.CompressSample(encodeFunc);
            else if (decompress)
                s.SetAudioData(s.GetAudioData(), s.Rate);
            
            // Raw data: either copy s16le or encoded vorbis or encode manually if overridden
            // Use set timeout so the thread doesn't die
            var r = s.GetRawData(true);
            writtenCount++;
            
            progressFunc?.Invoke(writtenCount / (float)bank.Samples.Count);
            
            Debug.WriteLine($"Encode sample {writtenCount}. {s.Name} of {
                bank.Samples.Count}. Compressed: {s.IsCompressed}.");
            
            /* 6.1 Sample Data Format in the smpl Sub-chunk
            Each sample is followed by a minimum of forty-six zero
            valued sample data points. These zero valued data points are necessary to guarantee that any reasonable upward pitch shift
            using any reasonable interpolator can loop on zero data at the end of the sound.
            This doesn't apply to sf3 tho
             */
            smplChunkSize = checked(
                smplChunkSize + r.Count + (s.IsCompressed ? 0 : 92));
            sampleData.Add(r);
            
            if (smplChunkSize > uint.MaxValue) throw new ArgumentException(
                "Cannot serialize very large soundbanks");
        }

        var addByte = smplChunkSize % 2 != 0;
        if (addByte)
            smplChunkSize++;

        var sdta = new List<ArraySegment<byte>>(bank.Samples.Count * 2 + 1);
        var sdtaHeader = new byte[SDTA_TO_DATA_OFFSET];
        sdta.Add(sdtaHeader);
        
        var sdtaSeg = (ArraySegment<byte>)sdtaHeader;
        
        // Avoid using writeRIFFChunk for performance
        // Sdta chunk
        Util.WriteBinaryString(ref sdtaSeg, "LIST");
        // "sdta" + full smpl length
        Util.WriteLittleEndian(
            ref sdtaSeg, 
            unchecked((int)(smplChunkSize + SDTA_TO_DATA_OFFSET - 8)), 
            4);
        Util.WriteBinaryString(ref sdtaSeg, "sdta");
        Util.WriteBinaryString(ref sdtaSeg, "smpl");
        Util.WriteLittleEndian(
            ref sdtaSeg, unchecked((int)smplChunkSize), 4);

        var offset = 0u;
        var pad = new byte[92];

        // Write out
        for (var i = 0; i < bank.Samples.Count; i++)
        {
            var sample = bank.Samples[i];
            var data = sampleData[i];
            sdta.Add(data);
            
            var startOffset = 0u;
            var endOffset = 0u;
            if (sample.IsCompressed)
            {
                // Sf3 offset is in bytes
                startOffset = offset;
                endOffset = (uint)(startOffset + data.Count);
            }
            else
            {
                // Sf2 in sample data points
                startOffset = offset / 2; // Inclusive
                endOffset = (uint)(startOffset + data.Count / 2);
                offset += 92; // 46 sample data points
                sdta.Add(pad);
            }

            offset = (uint)(offset + data.Count);
            smplStartOffsets.Add(startOffset);
            smplEndOffsets.Add(endOffset);
        }
        
        if (addByte) sdta.Add(new byte[1]);

        return sdta;
    }
}