using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpessaSharp.MIDI;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

/// <summary> Represents a proper DLS instrument, with regions and articulation. DLS</summary>
internal sealed class DLSInstrument
{
    public MidiPatch.Full MidiPatch;
    
    public readonly DLSArticulation Articulation = new ();
    public readonly List<DLSRegion> Regions = [];
    
    public string Name
    {
        get => MidiPatch.Name;
        set => MidiPatch = MidiPatch with { Name = value };
    }

    public int BankMSB
    {
        get => MidiPatch.BankMSB;
        set => MidiPatch = MidiPatch with 
            { Data = MidiPatch.Data with { BankMSB = value }};
    }

    public int BankLSB
    {
        get => MidiPatch.BankLSB;
        set => MidiPatch = MidiPatch with 
            { Data = MidiPatch.Data with { BankLSB = value }};
    }

    public int Program
    {
        get => MidiPatch.Program;
        set => MidiPatch = MidiPatch with 
            { Data = MidiPatch.Data with { Program = value }};
    }

    public bool IsGMGSDrum
    {
        get => MidiPatch.IsGMGSDrum;
        set => MidiPatch = MidiPatch with 
            { Data = MidiPatch.Data with { IsGMGSDrum = value }};
    }

    public static DLSInstrument Copy(DLSInstrument input)
    {
        var output = new DLSInstrument { MidiPatch = input.MidiPatch, };
        
        output.Articulation.CopyFrom(input.Articulation);
        foreach (var region in input.Regions)
            output.Regions.Add(DLSRegion.Copy(region));
        
        return output;
    }

    public static DLSInstrument Read(
        ReadOnlySpan<DownloadableSoundsSample> samples,
        RIFFChunk mainChunk)
    {
        var chunks = DLSVerifier.VerifyAndReadList(
            mainChunk, new RIFFChunk.DLSFourCC("ins "));

        var instrumentHeader = chunks.FindIndex(c => c.Header == "insh");
        if (instrumentHeader == -1)
            throw SpessaException.ParsingSoundBank("No instrument header!");
        
        // Read the instrument name in INFO
        var instrumentName = "";
        if (RIFFChunk.FindListType(chunks, new RIFFChunk.FourCC("INFO")) is {} infoChunk)
        {
            var data = infoChunk.Data;
            var info = RIFFChunk.Read(ref data);
            while (info.Header != "INAM")
                info = RIFFChunk.Read(ref data);
            var infoData = info.Data;
            instrumentName = Util.ToString(
                Util.ReadBinaryString(ref infoData, infoData.Count)).Trim();
        }

        if (string.IsNullOrWhiteSpace(instrumentName))
            instrumentName = "Unnamed Instrument";

        var instrument = new DLSInstrument();
        instrument.Name = instrumentName;
        
        // Read instrument header
        var ihData = chunks[instrumentHeader].Data;
        var regions = Util.ReadLittleEndian(ref ihData, 4);
        
        /*
         * Specifies the MIDI bank location. Bits 0-6 are defined as MIDI CC32 and bits 8-14 are
         * defined as MIDI CC0. Bits 7 and 15-30 are reserved and should be written to zero. If the
         * F_INSTRUMENT_DRUMS flag (Bit 31) is equal to 1 then the instrument is a drum
         * instrument; if equal to 0 then the instrument is a melodic instrument.
         */
        var ulBank = Util.ReadLittleEndian(ref ihData, 4);
        /*
         * Specifies the MIDI Program Change (PC) value. Bits 0-6 are defined as PC value and bits 7-
         * 31 are reserved and should be written to zero.
         */
        var ulInstrument = Util.ReadLittleEndian(ref ihData, 4);

        instrument.Program      = ulInstrument      & 127;
        instrument.BankMSB      = (ulBank >>> 8)    & 127;
        instrument.BankLSB      = ulBank            & 127;
        instrument.IsGMGSDrum   = (ulBank >>> 31) > 0;
        
        Debug.WriteLine($"Parsing {instrumentName} ...");
        
        // List of regions
        var chunksSpan = CollectionsMarshal.AsSpan(chunks);
        if (RIFFChunk.FindListType(
                chunksSpan,
                new RIFFChunk.FourCC("lrgn")) is not { } regionListChunk)
            throw SpessaException.ParsingSoundBank("No region list!");
        
        instrument.Articulation.Read(chunksSpan);

        // Read regions
        var rlcData = regionListChunk.Data;
        
        for (var i = 0; i < regions; i++)
        {
            var chunk = RIFFChunk.Read(ref rlcData);
            DLSVerifier.VerifyHeader(chunk, new RIFFChunk.FourCC("LIST"));
            
            var cData = chunk.Data;
            var type = Util.ReadBinaryString(ref cData, 4);
            
            if (!(Ascii.Equals(type, "rgn ") || Ascii.Equals(type, "rgn2")))
                DLSVerifier.ParsingError(
                    $"Invalid DLS region! Expected 'rgn ' or 'rgn2', got '{
                        Util.ToString(type)}'");

            if (DLSRegion.Read(samples, chunk) is {} region)
                instrument.Regions.Add(region);
        }

        return instrument;
    }

    public static DLSInstrument FromSF(
        BasicPreset preset, ReadOnlySpan<BasicSample> samples)
    {
        var instrument = new DLSInstrument { MidiPatch = preset.Patch, };
        
        Debug.WriteLine($"Converting Preset '{preset}' to DLS Instrument ...");
        
        // Combine preset and instrument zones into a single instrument zone (region) list
        var inst = preset.ToFlattenedInstrument();
        
        foreach (var z in inst.Zones)
            instrument.Regions.Add(DLSRegion.FromSFZone(z, samples));

        return instrument;
    }

    public ArraySegment<byte> Write()
    {
        SpessaLog.Info($"Writing Preset '{Name}' ...");

        var chunks = 
            new List<ArraySegment<byte>>([WriteHeader()]);

        var regionChunks = Regions.Select(r => r.Write()).ToArray();
        chunks.Add(RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("lrgn"), regionChunks, false, true));
        
        // This will mostly be false as SF2 -> DLS can't have both global and local regions,
        // So it only has global, hence this check.
        if (Articulation.Len > 0) chunks.Add(Articulation.Write());
        
        // Write the name
        var iname = RIFFChunk.Write(
            new RIFFChunk.FourCC("INAM"), Util.GetStringBytes(Name, true));
        chunks.Add(RIFFChunk.Write(new RIFFChunk.FourCC("INFO"), iname, false, true));
        
        return RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("ins "), 
            CollectionsMarshal.AsSpan(chunks), false, true);
    }
    
    /// <summary>Performs the full DLS to SF2 instrument conversion.</summary>
    /// <param name="bank"></param>
    public void ToSFPreset(SoundBank bank)
    {
        var preset = new BasicPreset(bank) { Patch = MidiPatch, };
        var instrument = new BasicInstrument();

        instrument.Name = preset.Name;
        preset.CreateZone(instrument);
        
        // Global articulation
        Articulation.ToSFZone(instrument.GlobalZone);
        
        foreach (var region in Regions)
            region.ToSFZone(
                instrument, CollectionsMarshal.AsSpan(bank.Samples));
        
        // Globalizes!
        instrument.Globalize();
        
        // Override reverb and chorus with 1000 instead of 200
        // Reverb
        if (instrument.GlobalZone.Modulators.All(
                m => m.Destination != Generator.Type.ReverbEffectsSend))
            instrument.GlobalZone.Modulators.Add(
                DownloadableSounds.DefaultModulators.DEFAULT_DLS_REVERB);
        
        // Chorus
        if (instrument.GlobalZone.Modulators.All(
                m => m.Destination != Generator.Type.ChorusEffectsSend))
            instrument.GlobalZone.Modulators.Add(
                DownloadableSounds.DefaultModulators.DEFAULT_DLS_CHORUS);
        
        // Remove generators with default values
        instrument.GlobalZone.Generators.RemoveAll(Generator.HasDefaultValue);
        
        bank.Presets.Add(preset);
        bank.Instruments.Add(instrument);
    }

    private ArraySegment<byte> WriteHeader()
    {
        // Insh: instrument header
        var seg = (Span<byte>)stackalloc byte[12];
        var buff = seg;
        
        Util.WriteDword(ref seg, Regions.Count); // CRegions
        // Bank MSB is in bits 8-14
        var ulBank = ((BankMSB & 127) << 8) | (BankLSB & 127);
        // Bit 32 means drums
        if (IsGMGSDrum) ulBank |= 1 << 31;
        
        Util.WriteDword(ref seg, ulBank); // UlBank
        Util.WriteDword(ref seg, Program & 127); // UlInstrument

        return RIFFChunk.Write(new RIFFChunk.FourCC("insh"), buff);
    }
}