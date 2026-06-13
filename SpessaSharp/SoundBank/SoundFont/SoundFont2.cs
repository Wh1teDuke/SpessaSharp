using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;

/// <summary>Parses a soundfont2 file </summary>
internal static class SoundFont2
{
    public static SoundBank Load(RootChunk buffer, bool sfe)
    {
        var bank = new SoundBank(
            sfe ? SoundBank.BankType.SFE : SoundBank.BankType.SF2);
        
        SpessaLog.Info("Parsing a SoundFont2 file...");
        
        // Read RIFF header
        var fourCC = buffer.PeekString(4);
        VerifyTexts(fourCC, "riff", "rifs");
        var rf64 = Util.EqualsIgnoreCase(fourCC, "rifs");
        if (rf64) SpessaLog.Info("RIFF64 Detected!");

        // Read the main chunk (don't verify as we just did)
        /*discard*/buffer.PeekRIFFChunk(rf64);
        
        var type = buffer.ReadString(4);
        VerifyTexts(type, "sfbk", "sfpk", "sfen");

        /*
        Some SF2Pack description:
        this is essentially sf2, but the entire smpl chunk is compressed (we only support Ogg Vorbis here)
        and the only other difference is that the main chunk isn't "sfbk" but rather "sfpk"
         */
        var isSF2Pack = Ascii.EqualsIgnoreCase(type, "sfpk");
        
        // INFO
        var infoChunk = buffer.ReadRIFFChunk(rf64);
        var infoChunkData = infoChunk.Data;
        
        VerifyHeader(infoChunk, "list");
        
        var infoString = Util.ReadBinaryString(ref infoChunkData, 4);
        VerifyText(infoString, "info");

        RIFFChunk? xdtaChunk = null;
        
        while (infoChunkData.Count > 0)
        {
            var chunk = RIFFChunk.Read(ref infoChunkData, rf64);
            var chunkData = chunk.Data;

            // Special cases
            switch (chunk.Header)
            {
                case ("ifil")_:
                case ("iver")_:
                    var major = Util.ReadLittleEndian(ref chunkData, 2);
                    var minor = Util.ReadLittleEndian(ref chunkData, 2);

                    bank.Info = chunk.Header == "ifil"
                        ? bank.Info with { Version      = (major, minor) }
                        : bank.Info with { RomVersion   = (major, minor) };
                    break;
                // Dmod: default modulators
                case ("DMOD")_:
                    // Override default modulators
                    bank.DefaultModulators.Clear();
                    bank.DefaultModulators.AddRange(
                        Read.Modulators(chunkData));
                    bank.CustomDefaultModulators = true;
                    break;
                case ("LIST")_:
                    // Possible xdta
                    var listType = Util.ReadBinaryString(ref chunkData, 4);
                    if (Ascii.Equals(listType, "xdta"))
                    {
                        SpessaLog.Info("Extended SF2 found!");
                        xdtaChunk = chunk;
                    }
                    break;
                case ("ICRD")_:
                    bank.Info = bank.Info with { 
                        CreationDate = Util.ParseDateString(Util.ToString(
                            Util.ReadBinaryString(ref chunkData, chunkData.Count))) };
                    break;
                case ("ISFT")_:
                    bank.Info = bank.Info with { Software = Text() };
                    break;
                case ("IPRD")_:
                    bank.Info = bank.Info with { Product = Text() };
                    break;
                case ("IENG")_:
                    bank.Info = bank.Info with { Engineer = Text() };
                    break;
                case ("ICOP")_:
                    bank.Info = bank.Info with { Copyright = Text() };
                    break;
                case ("INAM")_:
                    bank.Info = bank.Info with { Name = Text() };
                    break;
                case ("ICMT")_:
                    bank.Info = bank.Info with { Comment = Text() };
                    break;
                case ("irom")_:
                    bank.Info = bank.Info with { RomInfo = Text() };
                    break;
                case ("isng")_:
                    bank.Info = bank.Info with { SoundEngine = Text() };
                    break;

                string Text() =>
                    Util.ToString(Util.ReadBinaryString(chunkData));
            }
        }
        
        SpessaLog.Info(bank.Info.ToString());
        // https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md
        (
            RIFFChunk phdr,
            RIFFChunk pbag,
            RIFFChunk pmod,
            RIFFChunk pgen,
            //
            RIFFChunk inst,
            RIFFChunk ibag,
            RIFFChunk imod,
            RIFFChunk igen,
            //
            RIFFChunk shdr
        )? xChunks = null;
        
        if (xdtaChunk != null)
        {
            // Read the hydra chunks
            // SpessaSharp: When 'xdta' is read, xdtaChunk is unaffected.
            var xdtaChunkData = xdtaChunk.Value.Data[4..];
            xChunks = (
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64),
                RIFFChunk.Read(ref xdtaChunkData, rf64));
        }
        
        // SDTA
        var sdtaChunk = buffer.PeekRIFFChunk(rf64);
        VerifyHeader(sdtaChunk, "list");
        VerifyText(buffer.ReadString(4), "sdta");
        
        // Smpl
        SpessaLog.Info("[WARN] Verifying smpl chunk ...");
        var sampleDataChunk = buffer.PeekRIFFChunk(rf64);
        VerifyHeader(sampleDataChunk, "smpl");
        SampleChunk? sampleData;
        //var sampleDataStartIndex = 0;
        // SF2Pack: the entire data is compressed
        if (isSF2Pack)
        {
            SpessaLog.Info(
                "SF2Pack detected, attempting to decode the smpl chunk ...");

            if (SoundBank.Vorbis.Decoder is not {} decoder)
                throw SpessaException.ParsingSoundBank(
                    new ArgumentException("Vorbis decoder must be supplied"));

            try
            {
                var decoded = decoder(
                    buffer.Slice(end:
                        buffer.Offset +
                        (sdtaChunk.Size - 4 - sdtaChunk.HeaderSize)
                        ).AsSegment());
                SpessaLog.Info(
                    $"Decoded the smpl chunk! Length: {decoded.Count}");
                sampleData = SampleChunk.Of(decoded);
            }
            catch (Exception e)
            {
                throw SpessaException.ParsingSoundBank(
                    $"SF2Pack Ogg Vorbis decode error: {e.Message}");
            }
        }
        else
        {
            sampleData = buffer.BigSlice();
            //sampleDataStartIndex = buffer.Offset;
        }

        var sdtaLen = unchecked(
            (uint)sdtaChunk.Size - 4 - sdtaChunk.HeaderSize);
        SpessaLog.Info(
            $"Skipping sample chunk, length: {sdtaLen}");
        
        buffer = buffer.Slice(start: buffer.Offset + sdtaLen);
        
        // PDTA
        SpessaLog.Info("[WARN] Loading preset data chunk ...");
        var presetChunk = buffer.ReadRIFFChunk(rf64);
        var presetChunkData = presetChunk.Data;
        
        VerifyHeader(presetChunk, "list");
        Util.ReadBinaryString(ref presetChunkData, 4);
        
        // Read the hydra chunks
        var phdrChunk = ReadRiffChunk(ref presetChunkData, "phdr");
        var pbagChunk = ReadRiffChunk(ref presetChunkData, "pbag");
        var pmodChunk = ReadRiffChunk(ref presetChunkData, "pmod");
        var pgenChunk = ReadRiffChunk(ref presetChunkData, "pgen");
        var instChunk = ReadRiffChunk(ref presetChunkData, "inst");
        var ibagChunk = ReadRiffChunk(ref presetChunkData, "ibag");
        var imodChunk = ReadRiffChunk(ref presetChunkData, "imod");
        var igenChunk = ReadRiffChunk(ref presetChunkData, "igen");
        var shdrChunk = ReadRiffChunk(ref presetChunkData, "shdr");
        
        SpessaLog.Info("Parsing samples ...");
        
        /*
         * Read all the samples
         * (the current index points to start of the smpl read)
         */
        /*buffer = new ArraySegment<byte>(
            buffer.Array!, 
            sampleDataStartIndex, 
            buffer.Array!.Length - sampleDataStartIndex);*/

        var samples = SFSample.Read(
            shdrChunk, sampleData, xdtaChunk == null);

        if (xdtaChunk != null && xChunks != null)
        {
            // Apply extensions to samples
            var xSamples = SFSample.Read(
                xChunks.Value.shdr, SampleChunk.Empty, false);

            if (xSamples.Count == samples.Count)
            {
                for (var i = 0; i < samples.Count; i++)
                {
                    var s = samples[i];
                    var x = xSamples[i];

                    s.Name += x.Name;
                    s.LinkedSampleIndex |= x.LinkedSampleIndex << 16;
                }
            }
        }
        
        // Trim names
        foreach (var s in samples)
            s.Name = s.Name.Trim();
        bank.Samples.AddRange(samples);
        
        // Read modulators and generators
        var instrumentGenerators = Read.Generators(igenChunk.Data);
        var instrumentModulators = Read.Modulators(imodChunk.Data);
        
        // Read the instruments
        var instruments = Read.Instruments(instChunk.Data);

        if (xdtaChunk != null && xChunks != null)
        {
            // Apply extensions to instruments
            var xInst = Read.Instruments(xChunks.Value.inst.Data);
            if (xInst.Count == instruments.Count)
            {
                for (var i = 0; i < instruments.Count; i++)
                {
                    ref var inst = ref CollectionsMarshal
                        .AsSpan(instruments)[i];
                    inst.Instrument.Name    += xInst[i].Instrument.Name;
                    inst.ZoneStartIndex     |= xInst[i].ZoneStartIndex << 16;
                }
                // Adjust zone counts
                for (var i = 0; i < instruments.Count; i++)
                {
                    ref var inst = ref CollectionsMarshal
                        .AsSpan(instruments)[i];
                    if (i < instruments.Count - 1)
                    {
                        inst.ZonesCount = instruments[i + 1].ZoneStartIndex -
                                          inst.ZoneStartIndex;
                    }
                }
            }
        }
        
        // Trim names
        foreach (var i in instruments)
            i.Instrument.Name = i.Instrument.Name.Trim();
        bank.Instruments.AddRange(
            instruments.Select(i => i.Instrument));
        
        var ibagIndexes = Read.ZoneIndexes(ibagChunk.Data);

        if (xdtaChunk != null && xChunks != null)
        {
            var data = xChunks.Value.ibag.Data;
            var extraIndexes = Read.ZoneIndexes(data);
            for (var i = 0; i < ibagIndexes.Mod.Count; i++)
                ibagIndexes.Mod[i] |= extraIndexes.Mod[i] << 16;
            for (var i = 0; i < ibagIndexes.Gen.Count; i++)
                ibagIndexes.Gen[i] |= extraIndexes.Gen[i] << 16;
        }
        
        // Read all the instrument zones (and apply them)
        Read.SFInstrument.ApplyInstrumentZones(
            ibagIndexes,
            instrumentGenerators,
            instrumentModulators,
            bank.Samples,
            instruments);
        
        // Read preset modulators and generators
        var presetGenerators = Read.Generators(pgenChunk.Data);
        var presetModulators = Read.Modulators(pmodChunk.Data);

        // Read presets
        var presets = Read.Presets(phdrChunk.Data, bank);
        
        if (xdtaChunk != null && xChunks != null)
        {
            // Apply extensions to presets
            var xPreset = Read.Presets(xChunks.Value.phdr.Data, bank);
            if (xPreset.Count == presets.Count)
            {
                var presetSpan = CollectionsMarshal.AsSpan(presets);
                for (var i = 0; i < presets.Count; i++)
                {
                    ref var pres = ref presetSpan[i];
                    var xpres = xPreset[i];
                    pres.BasicPreset.Name   += xpres.BasicPreset.Name;
                    pres.ZoneStartIndex     |= xpres.ZoneStartIndex << 16;
                }
                
                // Adjust zone counts
                for (var i = 0; i < presets.Count; i++)
                {
                    if (i >= presets.Count - 1) continue;

                    ref var pres = ref presetSpan[i];
                    pres.ZonesCount = presets[i + 1].ZoneStartIndex -
                                      pres.ZoneStartIndex;
                }
            }
        }
        
        // Trim names
        foreach (var p in presets)
            p.BasicPreset.Name = p.BasicPreset.Name.Trim();
        bank.Presets.AddRange(presets.Select(p => p.BasicPreset));
        
        var pbagIndexes = Read.ZoneIndexes(pbagChunk.Data);

        if (xdtaChunk != null && xChunks != null)
        {
            var extraIndexes = Read.ZoneIndexes(xChunks.Value.pbag.Data);
            for (var i = 0; i < pbagIndexes.Mod.Count; i++)
                pbagIndexes.Mod[i] |= extraIndexes.Mod[i] << 16;
            for (var i = 0; i < pbagIndexes.Gen.Count; i++)
                pbagIndexes.Gen[i] |= extraIndexes.Gen[i] << 16;
        }
        
        Read.ApplyPresetZones(
            pbagIndexes,
            CollectionsMarshal.AsSpan(presetGenerators),
            CollectionsMarshal.AsSpan(presetModulators),
            bank.Instruments,
            presets);
        
        bank.Flush();
        
        Debug.WriteLine($"Parsing finished! {bank.Info.Name} has {
            bank.Presets.Count} presets, {
                bank.Instruments.Count} instruments and {
                    bank.Samples.Count} samples.");
        
        return bank;

        RIFFChunk ReadRiffChunk(ref ArraySegment<byte> data, string expected)
        {
            var chunk = RIFFChunk.Read(ref data, rf64);
            VerifyHeader(chunk, expected);
            return chunk;
        }
    }
    
    private static void VerifyHeader(RIFFChunk chunk, string expected)
    {
        if (chunk.Header.EqualsIgnoreCase(new RIFFChunk.FourCC(expected)))
            return;
        throw SoundBank.ParsingError(
            $"'Invalid chunk header! Expected '{expected}', got '{chunk.Header}'");
    }

    private static void VerifyText(ReadOnlySpan<byte> text, string expected)
    {
        if (Ascii.EqualsIgnoreCase(expected, text))
            return;
        throw SoundBank.ParsingError(
            $"'Invalid FourCC: Expected {expected}, got {Util.ToString(text)}'");
    }
    
    private static void VerifyTexts(
        ReadOnlySpan<byte> text, 
        params ReadOnlySpan<string> expected)
    {
        foreach (var exp in expected)
            if (Util.EqualsIgnoreCase(text, exp))
                return;

        throw SoundBank.ParsingError(
            $"'Invalid FourCC: Expected {
                string.Join(", ", expected.ToArray().Select(s => $"'{s}'"))}, got '{
                Util.ToString(text)}'");
    }
}