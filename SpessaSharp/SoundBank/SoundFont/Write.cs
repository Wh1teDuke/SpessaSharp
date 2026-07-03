using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;


/// <summary>
/// Returned structure containing extended SF2 chunks.
/// </summary>
public sealed class ExtendedSF2Chunks
{
    /// <summary>The PDTA part of the chunk.</summary>
    public required ArraySegment<byte> pdta;
    /// <summary> The XDTA (https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md) part of the chunk. </summary>
    public required ArraySegment<byte> xdta;
}

/// <summary> Options for writing a SoundFont2 file. </summary>
/// <param name="Compress">If the soundfont should be compressed with a given function. This changes the version to 3.0.</param>
/// <param name="ProgressFunc">A function to show progress for writing large banks. It can be undefined.</param>
/// <param name="Software">The `ISFT` field to set when writing. If unset, "SpessaSynth" is written. This field indicates the last software that was used to edit this sound bank.</param>
/// <param name="WriteDefaultModulators">If the DMOD chunk should be written. Recommended. Note that it will only be written if the modulators are unchanged.</param>
/// <param name="WriteExtendedLimits">If the XDTA chunk should be written to allow virtually infinite parameters. Recommended. Note that it will only be written needed.</param>
/// <param name="Decompress">If an SF3 bank should be decompressed back to SF2. Not recommended. This changes the version to 2.4.</param>
public readonly record struct SF2WriteOptions(
    bool Compress,
    SoundBank.ProgressFunc? ProgressFunc,
    string? Software,
    bool WriteDefaultModulators,
    bool WriteExtendedLimits,
    bool Decompress)
{
    public static readonly SF2WriteOptions Default = new(
        Compress:                   false,
        ProgressFunc:               null,
        Software:                   null,
        WriteDefaultModulators:     true,
        WriteExtendedLimits:        true,
        Decompress:                 false
    );
}

/// <summary>Options for writing an SFE 4 file.</summary>
/// <param name="rf64">
/// If the RIFS (64-bit RIFF chunks) should be used.
/// Increases maximum size from 4GB to effectively infinite.
/// Recommended, since SFE 4 is effectively incompatible with SF2.
/// </param>
public readonly record struct SFEWriteOptions(
    SF2WriteOptions Base,
    bool rf64)
{
    public static readonly SFEWriteOptions Default = new (
        SF2WriteOptions.Default, true);
}


/// <summary> Write indexes for tracking writing a SoundFont file. </summary>
public sealed class SoundFontWriteIndexes
{
    /// <summary> Generator start index. </summary>
    public int Gen;
    /// <summary> Modulator start index. </summary>
    public int Mod;
    /// <summary> Zone start index. </summary>
    public int Bag;
    /// <summary> Preset/instrument start index. </summary>
    public int HDR;
}

internal static class Write
{
    /// <summary>
    /// Writes the sound bank as an SF2 file.</summary>
    /// <param name="bank"></param>
    /// <param name="options">The options for writing.</param>
    /// <param name="output">The binary file data.</param>
    public static void SF2(
        SoundBank bank, SF2WriteOptions options, Stream output)
    {
        SF(
            bank, 
            options, 
            options.WriteDefaultModulators, 
            options.WriteExtendedLimits, 
            false, 
            false, 
            output);
    }
    
    /// <summary>Writes the sound bank as an SFE 4 file.</summary>
    /// <param name="bank"></param>
    /// <param name="options"></param>
    /// <param name="output"></param>
    public static void SFE(
        SoundBank bank, SFEWriteOptions options, Stream output)
    {
        SF(
            bank, 
            options.Base, 
            true, true, true, options.rf64, 
            output);
    }
    
    /// <summary>General writing function for both SFE and SF2.</summary>
    /// <param name="bank">The bank</param>
    /// <param name="options">The options for writing.</param>
    /// <param name="writeDefaultModulators">SFE + SF2 compatible</param>
    /// <param name="writeExtendedLimits">SFE + SF2 compatible</param>
    /// <param name="writeBankLSB">SFE Only</param>
    /// <param name="rf64">SFE Only</param>
    /// <param name="output">The binary file data.</param>
    /// <exception cref="ArgumentException">No compression function supplied</exception>
    /// <exception cref="ArgumentException">Cannot compress and decompress at the same time</exception>
    public static void SF(
        SoundBank bank, 
        SF2WriteOptions options,
        bool writeDefaultModulators,
        bool writeExtendedLimits,
        bool writeBankLSB,
        bool rf64,
        Stream output)
    {
        if (options.Compress)
        {
            if (SoundBank.Vorbis.Encoder == null &&
                bank.Samples.Any(s => !s.IsCompressed))
                throw new ArgumentException(
                    "No compression function supplied but compression enabled.");
            if (options.Decompress)
                throw new ArgumentException(
                    "Decompressed and compressed at the same time.");
        }
        
        SpessaLog.Info("Saving soundbank ...");
        SpessaLog.Info($"Compression: {options.Compress}");
        
        SpessaLog.Info("Writing INFO ...");
        
        // Write INFO
        var infoArrays = new List<ArraySegment<byte>>();

        var info = bank.Info;
        if (options.Compress || bank.Samples.Any(s => s.IsCompressed))
            // Set version to 3
            info = info with { Version = (Major: 3, Minor: 0) };

        if (options.Decompress)
            // Set version to 2.4
            info = info with { Version = (Major: 2, Minor: 4) };

        bank.Info = info;
        info = info with { Software = options.Software ?? "SpessaSharp" }; // :^)
        
        // Write info
        // Go with the SFSpec order (write functions auto skip if null)
        // Version writing needs special handling
        var ifilData = (Span<byte>)stackalloc byte[4];
        var buff = ifilData;
        
        Util.WriteWord(ref ifilData, (short)info.Version.Major);
        Util.WriteWord(ref ifilData, (short)info.Version.Minor);
        infoArrays.Add(RIFFChunk.Write(new RIFFChunk.FourCC("ifil"), buff, rf64));
        
        // Special comment case: merge subject and comment
        string? commentText = null;
        if (info.Comment != null)
            commentText = info.Comment;
        if (!string.IsNullOrEmpty(info.Subject))
            commentText += (info.Comment != null ? "\n" : "") + info.Subject;
        
        // soundEngine
        if (info.SoundEngine != null)
            WriteSF2Info(new RIFFChunk.FourCC("isng"), info.SoundEngine);
        // name
        WriteSF2Info(new RIFFChunk.FourCC("INAM"), info.Name);
        // romInfo
        if (info.RomInfo != null)
            WriteSF2Info(new RIFFChunk.FourCC("irom"), info.RomInfo);
        // romVersion
        if (info.RomVersion is {} romVersion)
        {
            buff.Clear();
            ifilData = buff;
            
            Util.WriteWord(ref ifilData, (short)romVersion.Major);
            Util.WriteWord(ref ifilData, (short)romVersion.Minor);
            infoArrays.Add(RIFFChunk.Write(new RIFFChunk.FourCC("iver"), buff, rf64));
        }
        // creationDate
        WriteSF2Info(new RIFFChunk.FourCC("ICRD"), Util.ToIsoString(info.CreationDate));
        // engineer
        if (info.Engineer != null)
            WriteSF2Info(new RIFFChunk.FourCC("IENG"), info.Engineer);
        // product
        if (info.Product != null)
            WriteSF2Info(new RIFFChunk.FourCC("IPRD"), info.Product);
        // copyright
        if (info.Copyright != null)
            WriteSF2Info(new RIFFChunk.FourCC("ICOP"), info.Copyright);
        // comment
        if (commentText != null)
            WriteSF2Info(new RIFFChunk.FourCC("ICMT"), commentText);
        // software
        if (info.Software != null)
            WriteSF2Info(new RIFFChunk.FourCC("ISFT"), info.Software);
        // subject
        // > Merged with the comment

        // Do not write unchanged default modulators
        var unchangedDefaultModulators = true;
        foreach (var m in bank.DefaultModulators)
        {
            unchangedDefaultModulators = !Modulator
                .SPESSASYNTH_DEFAULT_MODULATORS.Contains(m);
            if (!unchangedDefaultModulators) break;
        }

        if (unchangedDefaultModulators && writeDefaultModulators)
        {
            var mods = bank.DefaultModulators;
            SpessaLog.Info($"Writing {mods.Count} default modulators ...");
            
            var dmodSize = Modulator.ByteSize + mods.Count * Modulator.ByteSize;
            var dmodData = (Span<byte>)stackalloc byte[dmodSize];
            var dmodSeg = dmodData;

            foreach (var mod in mods) 
                mod.Write(ref dmodSeg);
            
            // Terminal modulator, is zero
            Util.WriteLittleEndian(
                ref dmodSeg, 0, Modulator.ByteSize);
            infoArrays.Add(RIFFChunk.Write(new RIFFChunk.FourCC("DMOD"), dmodData, rf64));
        }
        
        SpessaLog.Info("Writing SDTA ...");
        
        // Write sdta
        var smplStartOffsets = new List<uint>();
        var smplEndOffsets = new List<uint>();
        var sdtaChunk = SDTA.Get(
            bank,
            smplStartOffsets,
            smplEndOffsets,
            rf64,
            
            options.Compress,
            options.Decompress,
            SoundBank.Vorbis.Encoder,
            options.ProgressFunc);
        
        SpessaLog.Info("Writing PDTA ...");
        
        // Write pdta
        // Go in reverse so the indexes are correct
        // Instruments
        
        SpessaLog.Info("Writing SHDR ...");
        var shdrChunk = SHDR.Get(
            bank, smplStartOffsets, smplEndOffsets, rf64);
        
        // Note:
        // https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md

        SpessaLog.Info("Writing instruments ...");
        var instData = WriteSF2Elements(bank, rf64, false);
        
        SpessaLog.Info("Writing presets ...");
        var presData = WriteSF2Elements(bank, rf64, true, writeBankLSB);

        var chunks = (ExtendedSF2Chunks[])[
            presData.HDR,
            presData.Bag,
            presData.Mod,
            presData.Gen,
            //
            instData.HDR,
            instData.Bag,
            instData.Mod,
            instData.Gen,
            //
            shdrChunk,
        ];
        
        // Combine in the soundfont spec order
        var pdtaChunk = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("pdta"),
            chunks.Select(c => c.pdta).ToArray(),
            rf64, true);

        var writeXDTA =
            writeExtendedLimits &&
            (instData.WriteXDTA ||
            bank.Presets.Any(p => p.Name.Length > 20) ||
            bank.Instruments.Any(i => i.Name.Length > 20) ||
            bank.Samples.Any(s => s.Name.Length > 20));

        if (writeXDTA)
        {
            SpessaLog.Info("Writing the xdta chunk as 'WriteExtendedLimits' is enabled and at least one condition was met.");
            // https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md
            var xpdtaChunk = RIFFChunk.WriteParts(
                new RIFFChunk.FourCC("xdta"),
                chunks.Select(c => c.xdta).ToArray(),
                rf64, true);
            infoArrays.Add(xpdtaChunk);
        }

        var infoChunk = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("INFO"), 
            CollectionsMarshal.AsSpan(infoArrays), 
            rf64, true);
        SpessaLog.Info("Writing the output file...");

        // Finally, combine everything
        RIFFChunk.WriteParts(
            new RIFFChunk.FourCC(rf64 ? "RIFS" : "RIFF"), 
            [(ArraySegment<byte>)Util.GetStringBytes(writeBankLSB ? "sfen" : "sfbk"), 
                infoChunk, 
                sdtaChunk,
                pdtaChunk],
            rf64,
            output);
        
        output.Flush();
        
        SpessaLog.Info(
            $"Saved successfully! Final file size: {output.Length}");

        return;

        void WriteSF2Info(RIFFChunk.FourCC type, string data) =>
            infoArrays.Add(RIFFChunk.Write(
                type,
                // Pad with zero and ensure even length
                Util.GetStringBytes(data, true, true), rf64));
    }

    private readonly record struct SF2Elements(
        ExtendedSF2Chunks Gen,
        ExtendedSF2Chunks Mod,
        ExtendedSF2Chunks Bag,
        ExtendedSF2Chunks HDR,
        bool WriteXDTA);

    private static SF2Elements WriteSF2Elements(
        SoundBank bank, 
        bool rf64, 
        bool isPreset,
        // Preset only
        bool writeBankLSB = false)
    {
        // Note:
        // https://github.com/spessasus/soundfont-proposals/blob/main/extended_limits.md
        // Get headers
        var elementsLen = isPreset 
            ? bank.Presets.Count : bank.Instruments.Count;
        var genHeader = isPreset ? "pgen" : "igen";
        var modHeader = isPreset ? "pmod" : "imod";
        var bagHeader = isPreset ? "pbag" : "ibag";
        var hdrHeader = isPreset ? "phdr" : "inst";
        var hdrByteSize = isPreset 
            ? BasicPreset.PHDR_BYTE_SIZE 
            : BasicInstrument.INST_BYTE_SIZE;
        
        // Get indexes
        var currentGenIndex = 0;
        var generatorIndexes = new List<int>();
        var currentModIndex = 0;
        var modulatorIndexes = new List<int>();
        var generators = new List<Generator>();
        var modulators = new List<Modulator>();
        var zoneIndex = 0;
        var zoneIndexes = new List<int>();

        if (isPreset)
        {
            foreach (var el in bank.Presets)
            {
                zoneIndexes.Add(zoneIndex);
                WriteGlobal(el.GlobalZone);
                foreach (var zone in el.Zones)
                    WritePreset(zone);
                zoneIndex += el.Zones.Count + 1; // Terminal record
            }
        }
        else
        {
            foreach (var el in bank.Instruments)
            {
                zoneIndexes.Add(zoneIndex);
                WriteGlobal(el.GlobalZone);
                foreach (var zone in el.Zones)
                    WriteInstrument(zone);
                zoneIndex += el.Zones.Count + 1; // Terminal record
            }
        }
        
        // Terminal records
        generators.Add(new Generator(0, 0, false));
        modulators.Add(Modulator.Decoded(0, 0, 0, 0, 0));
        generatorIndexes.Add(currentGenIndex);
        modulatorIndexes.Add(currentModIndex);
        zoneIndexes.Add(zoneIndex);
        
        // Write the parameters
        var genSize = generators.Count * Generator.ByteSize;
        var genData = (Span<byte>)stackalloc byte[genSize];
        var genSeg = genData;
        
        foreach (var g in generators)
            g.Write(ref genSeg);

        var modSize = modulators.Count * Modulator.ByteSize;
        var modData = (Span<byte>)stackalloc byte[modSize];
        var modSeg = modData;
        
        foreach (var m in modulators)
            m.Write(ref modSeg);
        
        var bagSize = modulatorIndexes.Count * BasicZone.BagByteSize;
        var bagData = new ExtendedSF2Chunks
        {
            pdta = Util.Rent<byte>(bagSize),
            xdta = Util.Rent<byte>(bagSize),
        };

        bagData.pdta.AsSpan().Clear();
        bagData.xdta.AsSpan().Clear();

        for (var i = 0; i < modulatorIndexes.Count; i++)
        {
            var modulatorIndex = modulatorIndexes[i];
            var generatorIndex = generatorIndexes[i];
            
            // Bottom WORD: regular ibag
            Util.WriteWord(ref bagData.pdta, (short)(generatorIndex & 0xff_ff));
            Util.WriteWord(ref bagData.pdta, (short)(modulatorIndex & 0xff_ff));
            // Top WORD: extended ibag
            Util.WriteWord(ref bagData.xdta, (short)(generatorIndex >> 16));
            Util.WriteWord(ref bagData.xdta, (short)(modulatorIndex >> 16));
        }
        
        var hdrSize = (elementsLen + 1) * hdrByteSize;
        var hdrData = new ExtendedSF2Chunks
        {
            pdta = Util.Rent<byte>(hdrSize),
            xdta = Util.Rent<byte>(hdrSize),
        };
        
        hdrData.pdta.AsSpan().Clear();
        hdrData.xdta.AsSpan().Clear();
        
        if (isPreset)
            for (var i = 0; i < bank.Presets.Count; i++)
                bank.Presets[i].Write(hdrData, zoneIndexes[i], writeBankLSB);
        else
            for (var i = 0; i < bank.Instruments.Count; i++)
                bank.Instruments[i].Write(hdrData, zoneIndexes[i]);
        
        // Write terminal header records
        if (isPreset) 
        {
            Util.WriteBinaryString(ref hdrData.pdta, "EOP", 20);
            hdrData.pdta = hdrData.pdta[4..];     // Program, bank
            Util.WriteWord(ref hdrData.pdta, (short)(zoneIndex & 0xff_ff));
            hdrData.pdta = hdrData.pdta[12..];    // Library, genre, morphology

            Util.WriteBinaryString(ref hdrData.xdta, "", 20);
            hdrData.xdta = hdrData.xdta[4..];     // Program, bank
            Util.WriteWord(ref hdrData.xdta, (short)(zoneIndex >> 16));
            hdrData.xdta = hdrData.xdta[12..];    // Library, genre, morphology
        } 
        else 
        {
            // Write EOI
            Util.WriteBinaryString(ref hdrData.pdta, "EOI", 20);
            Util.WriteWord(ref hdrData.pdta, (short)(zoneIndex & 0xff_ff));

            Util.WriteBinaryString(ref hdrData.xdta, "", 20);
            Util.WriteWord(ref hdrData.xdta, (short)(zoneIndex >> 16));
        }
        
        var genBuff = Util.Rent<byte>(Generator.ByteSize);
        var modBuff = Util.Rent<byte>(Modulator.ByteSize);
        genBuff.AsSpan().Clear();
        modBuff.AsSpan().Clear();
        
        // Reset bag/hdr
        hdrData.pdta = new ArraySegment<byte>(hdrData.pdta.Array!, 0, hdrSize);
        hdrData.xdta = new ArraySegment<byte>(hdrData.xdta.Array!, 0, hdrSize);
        bagData.pdta = new ArraySegment<byte>(bagData.pdta.Array!, 0, bagSize);
        bagData.xdta = new ArraySegment<byte>(bagData.xdta.Array!, 0, bagSize);

        try
        {
            return new SF2Elements(
                Gen: new ExtendedSF2Chunks
                {
                    pdta = RIFFChunk.Write(new RIFFChunk.FourCC(genHeader), genData, rf64),
                    // Same as pmod, this chunk includes only the terminal generator record to allow reuse of the pdta parser.
                    xdta = RIFFChunk.Write(new RIFFChunk.FourCC(modHeader), genBuff, rf64),
                },
                Mod: new ExtendedSF2Chunks
                {
                    pdta = RIFFChunk.Write(new RIFFChunk.FourCC(modHeader), modData, rf64),
                    // This chunk exists solely to preserve parser compatibility and contains only the terminal modulator record.
                    xdta = RIFFChunk.Write(new RIFFChunk.FourCC(modHeader), modBuff, rf64),
                },
                Bag: new ExtendedSF2Chunks
                {
                    pdta = RIFFChunk.Write(new RIFFChunk.FourCC(bagHeader), bagData.pdta, rf64), 
                    xdta = RIFFChunk.Write(new RIFFChunk.FourCC(bagHeader), bagData.xdta, rf64), 
                },
                HDR: new ExtendedSF2Chunks
                {
                    pdta = RIFFChunk.Write(new RIFFChunk.FourCC(hdrHeader), hdrData.pdta, rf64), 
                    xdta = RIFFChunk.Write(new RIFFChunk.FourCC(hdrHeader), hdrData.xdta, rf64), 
                },
                WriteXDTA: 
                    Math.Max(currentGenIndex,
                        Math.Max(currentModIndex, zoneIndex)) > 0xff_ff);
        }
        finally
        {
            Util.Return(genBuff);
            Util.Return(modBuff);
            Util.Return(hdrData.pdta);
            Util.Return(hdrData.xdta);
            Util.Return(bagData.pdta);
            Util.Return(bagData.xdta);
        }

        void WritePreset(BasicPreset.Zone z) =>
            Write(z.GetWriteGenerators(bank), z.Basic.Modulators);

        void WriteInstrument(BasicInstrument.Zone z) =>
            Write(z.GetWriteGenerators(bank), z.Basic.Modulators);

        void WriteGlobal(BasicZone z) =>
            Write(z.GetWriteGenerators(), z.Modulators);

        void Write(List<Generator> gens, List<Modulator> mods)
        {
            generatorIndexes.Add(currentGenIndex);
            currentGenIndex += gens.Count;
            generators.AddRange(gens);
            
            modulatorIndexes.Add(currentModIndex);
            currentModIndex += mods.Count;
            modulators.AddRange(mods);
        }
    }
}