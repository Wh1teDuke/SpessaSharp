using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;

internal static class SHDR
{
    public static ExtendedSF2Chunks Get(
        SoundBank bank,
        List<uint> smplStartOffsets,
        List<uint> smplEndOffsets,
        bool rf64)
    {
        const int sampleLen = 46;
        var shdrSize = sampleLen * (bank.Samples.Count + 1); // +1 because EOP
        var shdrData = new byte[shdrSize];
        // https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md
        var xshdrData = new byte[shdrSize];
        var shdrSeg = (ArraySegment<byte>)shdrData;
        var xshdrSeg = (ArraySegment<byte>)xshdrData;

        var maxSampleLink = 0;
        for (var i = 0; i < bank.Samples.Count; i++)
        {
            var sample = bank.Samples[i];

            // Sample name
            Util.WriteBinaryString(ref shdrSeg, 
                Util.SafeSlice(sample.Name, end: 20), 20);
            Util.WriteBinaryString(ref xshdrSeg, 
                Util.SafeSlice(sample.Name, start: 20), 20);
            
            // Start offset
            var dwStart = smplStartOffsets[i];
            Util.WriteDword(ref shdrSeg, unchecked((int)dwStart));
            xshdrSeg = xshdrSeg[4..];
            
            // End offset
            var dwEnd = smplEndOffsets[i];
            Util.WriteDword(ref shdrSeg, unchecked((int)dwEnd));
            xshdrSeg = xshdrSeg[4..];
            
            // Loop is stored as relative in sample points, change it to absolute sample points here
            var loopStart = (uint)sample.LoopStart + dwStart;
            var loopEnd = (uint)sample.LoopEnd + dwStart;
            if (sample.IsCompressed)
            {
                // https://github.com/FluidSynth/fluidsynth/wiki/SoundFont3Format
                loopStart   -= dwStart;
                loopEnd     -= dwStart;
            }

            Util.WriteDword(ref shdrSeg, unchecked((int)loopStart));
            Util.WriteDword(ref shdrSeg, unchecked((int)loopEnd));
            
            // Sample rate
            Util.WriteDword(ref shdrSeg, sample.Rate);
            // Pitch and correction
            shdrSeg[0] = (byte)sample.OriginalKey;
            shdrSeg[1] = (byte)sample.PitchCorrection;
            shdrSeg = shdrSeg[2..];
            // Skip all those for xshdr
            xshdrSeg = xshdrSeg[14..];
            // Sample Link
            var sampleLinkIndex = sample.LinkedSample != null
                ? bank.Samples.IndexOf(sample.LinkedSample) : 0;

            Util.WriteWord(
                ref shdrSeg, (short)(Math.Max(0, sampleLinkIndex) & 0xff_ff));
            Util.WriteWord(
                ref xshdrSeg, (short)(Math.Max(0, sampleLinkIndex) >> 16));
            maxSampleLink = Math.Max(maxSampleLink, sampleLinkIndex);
            
            // Sample type: add byte if compressed
            var type = BasicSample.ValueOf(sample.SType);
            if (sample.IsCompressed) type |= SFSample.SF3_BIT_FLIT;
            Util.WriteWord(ref shdrSeg, (short)type);
            xshdrSeg = xshdrSeg[2..];
        }
        
        // Write EOS and zero everything else
        Util.WriteBinaryString(ref shdrSeg, "EOS", sampleLen);
        Util.WriteBinaryString(ref xshdrSeg, "EOS", sampleLen);

        var shdr = RIFFChunk.Write(
            new RIFFChunk.FourCC("shdr"), shdrData, rf64);
        var xshdr = RIFFChunk.Write(
            new RIFFChunk.FourCC("shdr"), xshdrData, rf64);

        return new ExtendedSF2Chunks { pdta = shdr, xdta = xshdr, };
    }
}