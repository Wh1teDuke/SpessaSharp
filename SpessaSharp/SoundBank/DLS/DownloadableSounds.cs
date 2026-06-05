using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.DLS;

/// <summary>Options for writing a DLS file.</summary>
/// <param name="ProgressFunc">A function to show progress for writing large banks. It can be undefined.</param>
/// <param name="Software">The `ISFT` field to set when writing. If unset, `SpessaSynth` is written. This field indicates the last software that was used to edit this sound bank.</param>
public readonly record struct DLSWriteOptions(
    SoundBank.ProgressFunc? ProgressFunc = null, string? Software = null);

internal sealed class DownloadableSounds
{
    internal static class DefaultModulators
    {
        public static readonly Modulator DEFAULT_DLS_REVERB = 
            Modulator.Decoded(
                0x00_db,
                0x0,
                (short)Generator.Type.ReverbEffectsSend,
                1_000,
                0);
        
        public static readonly Modulator DEFAULT_DLS_CHORUS = 
            Modulator.Decoded(
                0x00_dd,
                0x0,
                (short)Generator.Type.ChorusEffectsSend,
                1_000,
                0);
        
        public static readonly Modulator DLS_1_NO_VIBRATO_MOD = 
            Modulator.Decoded(
                0x00_81,
                0x0,
                (short)Generator.Type.VibLFOToPitch,
                0,
                0);
        
        public static readonly Modulator DLS_1_NO_VIBRATO_PRESSURE = 
            Modulator.Decoded(
                0x00_0d,
                0x0,
                (short)Generator.Type.VibLFOToPitch,
                0,
                0);
    }
    
    /// <summary></summary>
    /// <param name="LType"></param>
    /// <param name="Start">Specifies the start point of the loop in samples as an absolute offset from the beginning of the data in the <b>data-ck</b> subchunk of the <b>wave-list</b> wave file chunk.</param>
    /// <param name="Len">Specifies the length of the loop in samples.</param>
    public readonly record struct Loop(Loop.Type LType, int Start, int Len)
    {
        public enum Type { Forward, LoopAndRelease }
    }

    public readonly List<DownloadableSoundsSample> Samples = [];
    public readonly List<DLSInstrument> Instruments = [];

    public SoundBank.InfoData Info = new (
        Name:           "Unnamed DLS sound bank",
        CreationDate:   DateTime.Now,
        Software:       "SpessaSharp",
        SoundEngine:    "DLS Level 2.2",
        Product:        "SpessaSharp DLS",
        Version:        (Major: 2, Minor: 4));

    public static DownloadableSounds Load(Stream stream)
    {
        Debug.WriteLine("Parsing DLS file ...");
        
        // Read the main chunk
        var headerBuff = new byte[12];
        var headerSeg = (ArraySegment<byte>)headerBuff;
        stream.ReadExactly(headerBuff);
        
        var mainHeader = Util.ReadBinaryString(ref headerSeg, 4);
        /*var mainLength*/Util.ReadLittleEndian(ref headerSeg, 4);

        if (!Ascii.EqualsIgnoreCase(mainHeader, "RIFF"))
            throw SpessaException.ParsingSoundBank(
                $"Invalid DLS chunk header! Expected 'RIFF' got {
                    Util.ToString(mainHeader)}");
        
        //var firstChunk = RIFFChunk.Read(ref buffer, false);
        //DLSVerifier.VerifyHeader(firstChunk, new RIFFChunk.FourCC("RIFF"));
        DLSVerifier.VerifyText(
            Util.ReadBinaryString(ref headerSeg, 4),
            new RIFFChunk.DLSFourCC("dls "));
        
        // Read the list
        var chunks = new List<RIFFChunk>();
        var wvplStart = -1L;
        var wvplEnd = -1L;
        
        while (stream.Position < stream.Length)
        {
            headerSeg = headerBuff;
            headerSeg.AsSpan().Clear();
            
            stream.ReadExactly(headerSeg);
            stream.Position -= headerSeg.Count;

            var hStr = Util.ReadBinaryString(headerSeg[8..]);
            headerSeg = headerSeg[4..];
            var hLen = unchecked(
                (uint)Util.ReadLittleEndian(ref headerSeg, 4));

            if (Ascii.EqualsIgnoreCase("wvpl", hStr))
            {
                // Skip for now
                var chunkData = new byte[4];
                hStr.CopyTo(chunkData);
                
                headerSeg = headerBuff;
                hStr = Util.ReadBinaryString(headerSeg[..4]);
             
                wvplStart = stream.Position + headerBuff.Length;
                wvplEnd = stream.Position + 8 + hLen;
                stream.Position = wvplEnd;
                
                chunks.Add(new RIFFChunk(
                    new RIFFChunk.FourCC(Util.ToString(hStr)), -1, chunkData));
            }
            else
            {
                var chunkData = new byte[8 + hLen];
                var chunkSeg = (ArraySegment<byte>)chunkData;
                
                stream.ReadExactly(chunkData);
                chunks.Add(RIFFChunk.Read(ref chunkSeg));
            }
        }

        var dls = new DownloadableSounds();
        
        // Read info
        if (RIFFChunk.FindListType(
            chunks, new RIFFChunk.FourCC("INFO")) is { } infoChunk)
        {
            var data = infoChunk.Data;
            while (data.Count > 0)
            {
                var infoPart = RIFFChunk.Read(ref data);
                var ipData = infoPart.Data;
                
                var text = Util.ToString(
                    Util.ReadBinaryString(ref ipData, ipData.Count));

                dls.Info = infoPart.Header switch
                {
                    ("INAM")_ => dls.Info with { Name      = text },
                    ("ICRD")_ => dls.Info with 
                        { CreationDate = Util.ParseDateString(text) },
                    ("ICMT")_ => dls.Info with { Comment   = text },
                    ("ISBJ")_ => dls.Info with { Subject   = text },
                    ("ICOP")_ => dls.Info with { Copyright = text },
                    ("IENG")_ => dls.Info with { Engineer  = text },
                    ("IPRD")_ => dls.Info with { Product   = text },
                    ("ISFT")_ => dls.Info with { Software  = text },
                    _ => dls.Info
                };
            }
        }
        
        DLSVerifier.PrintInfo(dls);
        
        // Read "colh"
        var colhChunkIdx = chunks.FindIndex(c => c.Header == "colh");
        if (colhChunkIdx == -1)
            DLSVerifier.ParsingError("No colh chunk!");

        var colhChunk = chunks[colhChunkIdx];
        var colhData = colhChunk.Data;

        var instrumentAmount = Util.ReadLittleEndian(ref colhData, 4);
        Debug.WriteLine($"Instruments amount: {instrumentAmount}");
        
        // Read the wave list
        if (RIFFChunk.FindListType(
            chunks, new RIFFChunk.FourCC("wvpl")) is not {} waveListChunk)
        {
            // Does not return
            DLSVerifier.ParsingError("No wvpl chunk!");
            return null!; 
        }

        DLSVerifier.VerifyHeader(waveListChunk, new RIFFChunk.FourCC("LIST"));
        var wlcData = waveListChunk.Base;
        DLSVerifier.VerifyText(
            Util.ReadBinaryString(ref wlcData, 4), 
            new RIFFChunk.DLSFourCC("wvpl"));
        var waveList = new List<RIFFChunk>();
        stream.Position = wvplStart;
        var wvplSeg = AllocateWVPL(wvplStart, wvplEnd);
        
        for (var _ = 0; _ < 5; _++)
        {
            stream.ReadExactly(wvplSeg);
            while (wvplSeg.Count >= 8)
            {
                var lenSeg = wvplSeg[4..];
                var len = Util.ReadLittleEndian(ref lenSeg, 4);
                if (wvplSeg.Count - 8 < len)
                {
                    stream.Position -= wvplSeg.Count;
                    break;
                }
                
                waveList.Add(RIFFChunk.Read(ref wvplSeg));
            }

            if (stream.Position < wvplEnd)
            {
                wvplSeg = AllocateWVPL(stream.Position, wvplEnd);
                continue;
            }

            break;
        }

        //var waveList = DLSVerifier.VerifyAndReadList(
        //    waveListChunk, new RIFFChunk.DLSFourCC("wvpl"));
        
        foreach (var wave in waveList)
            dls.Samples.Add(DownloadableSoundsSample.Read(wave));
        
        // Read the instrument list
        if (RIFFChunk.FindListType(
            chunks, new RIFFChunk.FourCC("lins")) is not {} instrumentListChunk)
        {
            // Does not return
            DLSVerifier.ParsingError("No lins chunk!");
            return null!;
        }

        var instruments = DLSVerifier.VerifyAndReadList(
            instrumentListChunk, new RIFFChunk.DLSFourCC("lins"));
        
        Debug.WriteLine("Loading instruments ...");
        
        if (instruments.Count != instrumentAmount)
            Debug.WriteLine(
                $"[WARN] Colh reported invalid amount of instruments. Detected {
                    instruments.Count}, expected {instrumentAmount}");
        
        foreach (var ins in instruments)
            dls.Instruments.Add(DLSInstrument.Read(
                CollectionsMarshal.AsSpan(dls.Samples), ins));
        
        /*
         MobileBAE Instrument aliasing
         https://github.com/spessasus/spessasynth_core/issues/14
         https://lpcwiki.miraheze.org/wiki/MobileBAE#Proprietary_instrument_aliasing_chunk
         http://onj3.andrelouis.com/phonetones/Software%20and%20Soundbanks/Soundbanks/Beatnik%20mobileBAE/
        */
        var aliasingChunkIndex = chunks.FindIndex(
            c => c.Header == "pgal");

        if (aliasingChunkIndex != -1)
        {
            Debug.WriteLine("Found the instrument aliasing chunk!");

            var pgalData = chunks[aliasingChunkIndex].Data;

            // Check for the unused 4 bytes at the start
            // If the bank doesn't start with 00 01 02 03, skip them
            if (!pgalData[..4].AsSpan().SequenceEqual(
                    (Span<byte>)[0, 1, 2, 3]))
                pgalData = pgalData[4..];
            
            // Read the drum alias
            var drumInstrument = dls.Instruments.Find(i =>
                i.IsGMGSDrum || BankSelectHacks.IsXGDrum(i.BankMSB));

            if (drumInstrument == null)
            {
                Debug.WriteLine(
                    "[WARN] MobileBAE aliasing chunk without a drum preset. Aborting!");
                return dls;
            }

            var drumAliases = pgalData[..128];
            pgalData = pgalData[128..];

            for (var keyNum = 0; keyNum < 128; keyNum++)
            {
                var alias = drumAliases[keyNum];
                if (alias == keyNum)
                    // Skip the same aliases
                    continue;
                
                if (!Util.TryFind(
                    CollectionsMarshal.AsSpan(drumInstrument.Regions), 
                    alias,
                    static (r, alias) =>
                        r.KeyRange.Max == alias && r.KeyRange.Min == alias,
                    out var region))
                {
                    Debug.WriteLine(
                        $"[WARN] Invalid drum alias {keyNum} to {alias}: region does not exist.");
                    continue;
                }

                var copied = DLSRegion.Copy(region);
                copied.KeyRange = (keyNum, keyNum);
                drumInstrument.Regions.Add(copied);
            }
            
            // 4 bytes: Unknown purpose, 'footer'.
            pgalData = pgalData[4..];

            while (pgalData.Count > 0)
            {
                var aliasBankNum = Util.ReadLittleEndian(ref pgalData, 2);
                
                // Little-endian 16-bit value (only 14 bits used): Upper 7 bits: Bank MSB, lower 7 bits: Bank LSB
                var aliasBankLSB = aliasBankNum         & 0x7f;
                var aliasBankMSB = (aliasBankNum >> 7)  & 0x7f;
                var aliasProgram = pgalData[0];
                var nullByte = pgalData[1];
                pgalData = pgalData[2..];
                
                if (nullByte != 0) Debug.WriteLine(
                    $"[WARN] Invalid alias byte. Expected 0, got {nullByte}");
                
                var inputBankNum = Util.ReadLittleEndian(ref pgalData, 2);
                var inputBankLSB = inputBankNum         & 0x7f;
                var inputBankMSB = (inputBankNum >> 7)  & 0x7f;
                var inputProgram = pgalData[0];
                nullByte = pgalData[1];
                pgalData = pgalData[2..];
                
                if (nullByte != 0) Debug.WriteLine(
                    $"[WARN] Invalid alias header. Expected 0, got {nullByte}");

                DLSInstrument? alias = null;
                foreach (var i in dls.Instruments)
                {
                    if (!(i.BankLSB == inputBankLSB &&
                          i.BankMSB == inputBankMSB &&
                          i.Program == inputProgram &&
                          !i.IsGMGSDrum)) continue;
                    
                    alias = DLSInstrument.Copy(i);
                    break;
                }

                if (alias == null)
                {
                    Debug.WriteLine(
                        $"Invalid alias. Missing instrument: {
                            inputBankLSB}:{inputBankMSB}:{inputProgram}");
                    continue;
                }

                alias.BankMSB = aliasBankMSB;
                alias.BankLSB = aliasBankLSB;
                alias.Program = aliasProgram;
                dls.Instruments.Add(alias);
            }
        }

        if (string.IsNullOrWhiteSpace(dls.Info.Name))
            dls.Info = dls.Info with { Name = "UNNAMED" };
        
        Debug.WriteLine($"Parsing finished! {
            dls.Info.Name} has {
                dls.Instruments.Count} instruments and {
                dls.Samples.Count} samples.");

        return dls;
        
        static ArraySegment<byte> AllocateWVPL(long current, long end) =>
            new byte[Math.Min(
                end - current,
                1/*GB*/ * 1024/*MB*/ * 1024/*KB*/ * 1024/*B*/)];
    }
    
    /// <summary>Performs a full conversion from BasicSoundBank to DownloadableSounds. Includes an optional progress function for transforming the samples.</summary>
    /// <param name="bank"></param>
    /// <param name="progressFunc"></param>
    /// <returns></returns>
    public static DownloadableSounds FromSF(
        SoundBank bank, SoundBank.ProgressFunc? progressFunc)
    {
        Debug.WriteLine("Saving SF2 to DLS level 2 ...");

        var dls = new DownloadableSounds { Info = bank.Info };

        for (var i = 0; i < bank.Samples.Count; i++)
        {
            var s = bank.Samples[i];
            dls.Samples.Add(DownloadableSoundsSample.FromSF(s));
            progressFunc?.Invoke(i / (float)bank.Samples.Count);
        }

        foreach (var p in bank.Presets)
            dls.Instruments.Add(DLSInstrument.FromSF(
                p, CollectionsMarshal.AsSpan(bank.Samples)));
        
        Debug.WriteLine("Conversion Complete!");

        return dls;
    }

    /// <summary>Performs a full conversion from DownloadableSounds to BasicSoundBank.</summary>
    /// <returns></returns>
    public SoundBank ToSF()
    {
        Debug.WriteLine("Converting DLS to SF2 ...");
        var bank = new SoundBank(SoundBank.BankType.DLS)
        { Info = Info with { Version = (2, 4), }, };

        foreach (var sample in Samples)
            sample.ToSFSample(bank);
        foreach (var instrument in Instruments)
            instrument.ToSFPreset(bank);
        
        bank.Flush();

        Debug.WriteLine("Conversion complete!");
        
        return bank;
    }

    /// <summary>Writes a DLS file</summary>
    /// <param name="stream"></param>
    /// <param name="options">The options for writing the file.</param>
    public void Write(Stream stream, DLSWriteOptions? options = null)
    {
        var opts = options ?? new DLSWriteOptions();
        
        // Write colh
        var colhNum = (Span<byte>)stackalloc byte[4];
        var cnSeg = colhNum;
        
        Util.WriteDword(ref cnSeg, Instruments.Count);
        var colh = RIFFChunk.Write(
            new RIFFChunk.FourCC("colh"), colhNum);
        
        Debug.WriteLine("Writing instruments ...");

        var lins = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("lins"),
            Instruments.Select(i => i.Write()).ToArray(),
            true);
        
        Debug.WriteLine("Success!");
        Debug.WriteLine("Writing WAVE samples ...");

        var currentIndex = 0L;
        var ptblOffsets = new List<long>();
        var samples = new List<ArraySegment<byte>>();
        var wvplHeader = new byte[12];
        samples.Add(wvplHeader);
        
        var written = 0;

        foreach (var s in Samples)
        {
            var output = s.Write();
            
            Debug.WriteLine($"Wrote sample {written}. {s.Name} of {Samples.Count}.");
            
            opts.ProgressFunc?.Invoke(written / (float)samples.Count);
            ptblOffsets.Add(currentIndex);
            currentIndex += output.Count;
            samples.Add(output);
            written++;
        }

        var wvplSeg = (ArraySegment<byte>)wvplHeader;
        Util.WriteBinaryString(ref wvplSeg, "LIST");
        Util.WriteDword(ref wvplSeg, unchecked((int)(currentIndex + 4)));
        Util.WriteBinaryString(ref wvplSeg, "wvpl");

        /*var wvpl = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("wvpl"), 
            CollectionsMarshal.AsSpan(samples), 
            true);*/
        
        Debug.WriteLine("Succeeded!");
        
        // Write ptbl
        var ptblData = new byte[8 + 4 * ptblOffsets.Count];
        var ptblSeg = (Span<byte>)ptblData;
        
        Util.WriteDword(ref ptblSeg, 8);
        Util.WriteDword(ref ptblSeg, ptblOffsets.Count);
        foreach (var offset in ptblOffsets)
            Util.WriteDword(ref ptblSeg, unchecked((int)offset));

        var ptbl = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("ptbl"), [ptblData]);

        Info = Info with
            { Software = opts.Software ?? "SpessaSharp", }; // :^)
        var sbInfo = Info;
        
        // Write INFO
        var infos = new List<ArraySegment<byte>>();

        // Name
        WriteDLSInfo(new RIFFChunk.FourCC("INAM"), sbInfo.Name);
        // Comment
        if (sbInfo.Comment is {} comment)
            WriteDLSInfo(new RIFFChunk.FourCC("ICMT"), comment);
        // Copyright
        if (sbInfo.Copyright is {} copyRight)
            WriteDLSInfo(new RIFFChunk.FourCC("ICOP"), copyRight);
        // CreationDate
        WriteDLSInfo(new RIFFChunk.FourCC("ICRD"), Util.ToIsoString(sbInfo.CreationDate));
        // Engineer
        if (sbInfo.Engineer is {} author)
            WriteDLSInfo(new RIFFChunk.FourCC("IENG"), author);
        // Product
        if (sbInfo.Product is {} product)
            WriteDLSInfo(new RIFFChunk.FourCC("IPRD"), product);
        // RomVersion,Version,SoundEngine,RomInfo
        // not writable
        // Software
        if (sbInfo.Software is {} software)
            WriteDLSInfo(new RIFFChunk.FourCC("ISFT"), software);
        // Subject
        if (sbInfo.Subject is {} subject && !string.IsNullOrEmpty(subject))
            WriteDLSInfo(new RIFFChunk.FourCC("ISBJ"), subject);

        var info = RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("INFO"),
            CollectionsMarshal.AsSpan(infos),
            true);
        
        Debug.WriteLine("Combining everything ...");

        RIFFChunk.WriteParts(
            new RIFFChunk.FourCC("RIFF"),
            [(ArraySegment<byte>)"DLS "u8.ToArray(), 
                colh, lins, ptbl, samples, info], stream);
        
        stream.Flush();
        Debug.WriteLine("Saved successfully!");
        return;

        void WriteDLSInfo(RIFFChunk.FourCC type, string data) =>
            infos.Add(RIFFChunk.Write(
                type, Util.GetStringBytes(data, true)));
    }
}