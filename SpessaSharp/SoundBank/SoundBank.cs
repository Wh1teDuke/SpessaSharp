using System.Diagnostics;
using System.Text;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank.DLS;
using SpessaSharp.SoundBank.SoundFont;
using SpessaSharp.SoundBank.Utils;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

/// <summary>Represents a single sound bank, be it DLS or SF2.</summary>
public sealed class SoundBank(
    SoundBank.BankType type = SoundBank.BankType.SF2): BasePreset.IGetter<BasicPreset>
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Name">Name.</param>
    /// <param name="Version">The sound bank's version.</param>
    /// <param name="CreationDate">Creation date.</param>
    /// <param name="SoundEngine">Sound engine.</param>
    /// <param name="Engineer">Author.</param>
    /// <param name="Product">Product.</param>
    /// <param name="Copyright">Copyright.</param>
    /// <param name="Comment">Comment.</param>
    /// <param name="Software">Software used to edit the file.</param>
    /// <param name="Subject">Subject.</param>
    /// <param name="RomInfo">ROM information.</param>
    /// <param name="RomVersion">A tag that only applies to SF2 and will usually be undefined.</param>
    public readonly record struct InfoData(
        string Name,
        (int Major, int Minor) Version,
        DateTime CreationDate,
        string? SoundEngine = null,
        string? Engineer = null,
        string? Product = null,
        string? Copyright = null,
        string? Comment = null,
        string? Software = null,
        string? Subject = null,
        string? RomInfo = null,
        (int Major, int Minor)? RomVersion = null);
    
    private bool _isXGBank;

    public InfoData Info { get; set; } = new(
        Name:           "Unnamed",
        Version:        (Major: 2, Minor: 4),
        CreationDate:   DateTime.Now,
        SoundEngine:    "E-mu 10K2",
        Software:       "SpessaSharp");


    public enum BankType { SF2, SFE, DLS }

    /// <summary>The sound bank's presets.</summary>
    public readonly List<BasicPreset> Presets = [];
    /// <summary>The sound bank's samples.</summary>
    public readonly List<BasicSample> Samples = [];
    /// <summary>The sound bank's instruments.</summary>
    public readonly List<BasicInstrument> Instruments = [];

    /// <summary>
    /// The type of the sound bank that was loaded.
    /// Either <b>sf2</b> for SoundFont2/SoundFont3 or <b>dls</b> for DownLoadable Sounds or <b>sfe</b> for SF-Enhanced..
    /// <para>
    /// Please note that SF3 or SFOGG files are parsed as <b>sf2</b> files, but with compressed samples.
    /// The type is still <b>sf2</b>.
    /// </para>
    /// </summary>
    public readonly BankType Type = type;

    /// <summary>Sound bank's default modulators.</summary>
    public readonly List<Modulator> DefaultModulators =
        [..Modulator.SPESSASYNTH_DEFAULT_MODULATORS];

    /// <summary>If the sound bank has custom default modulators (DMOD).</summary>
    public bool CustomDefaultModulators;

    /// <summary>Checks for XG drum sets and considers if this sound bank is XG.</summary> 
    public bool IsXGBank => _isXGBank;

    public delegate ArraySegment<float> Decode(
        ArraySegment<byte> compressed);
    public delegate ArraySegment<byte> Encode(
        ArraySegment<float> audioData, int sampleRate);

    public static class Vorbis
    {
        public static Decode? Decoder { get; set; }
        public static Encode? Encoder { get; set; }   
    }

    /// <summary>Loads a sound bank from a file buffer.</summary>
    /// <param name="buffer">The binary file buffer to load.</param>
    /// <remarks>If your soundbank is large (>2GB), use <see cref="From(FileInfo)"/> or <see cref="From(FileStream)"/></remarks>
    /// <returns>The loaded sound bank, a BasicSoundBank instance.</returns>
    public static SoundBank From(ArraySegment<byte> buffer)
    {
        var riffCheck = buffer[..4];
        if (!Util.Equals(riffCheck, "RIFF", "RIFS"))
            throw SpessaException.ParsingSoundBank(
                $"Expected 'RIFF' or 'RIFS' header, got '{
                    Util.ToString(riffCheck)}'");

        var rf64 = Util.Equals(riffCheck, "RIFS");
        var check = buffer[rf64 ? 12 .. 16 : 8 .. 12];
        var id= Util.ReadBinaryString(check);
        return Ascii.EqualsIgnoreCase(id, "dls ") 
            ? DownloadableSounds.Load(new MemoryStream(
                buffer.Array!, buffer.Offset, buffer.Count)).ToSF()
            : SoundFont2.Load(
                new RootChunk.Segment(buffer),
                Util.Equals(id, "sfen"));
    }

    /// <summary>Loads a sound bank from a file buffer.</summary>
    /// <param name="file">The file to load. Use this for large (>2GB) soundbanks</param>
    /// <returns>The loaded sound bank, a BasicSoundBank instance.</returns>
    public static SoundBank From(FileInfo file)
    {
        using var stream = file.OpenRead();
        return From(stream);
    }

    /// <summary>Loads a sound bank from a file buffer.</summary>
    /// <param name="stream">The file stream to load. Use this for large (>2GB) soundbanks</param>
    /// <returns>The loaded sound bank, a BasicSoundBank instance.</returns>
    public static SoundBank From(FileStream stream)
    {
        if (stream.Length < int.MaxValue)
            using (var reader = new BinaryReader(stream))
                return From(reader.ReadBytes((int)stream.Length));

        var chunk = new RootChunk.Stream(stream);
        var riffCheck = chunk.Slice(0, 4).AsSegment();
        if (!Util.Equals(riffCheck, "RIFF", "RIFS"))
            throw SpessaException.ParsingSoundBank(
                $"Expected 'RIFF' or 'RIFS' header, got '{
                    Util.ToString(riffCheck)}'");
        
        var rf64 = Util.Equals(riffCheck, "RIFS");
        var check =
            (rf64 ? chunk.Slice(12, 16) : chunk.Slice(8, 12)).AsSegment();
        var id= Util.ReadBinaryString(check);
        return Ascii.EqualsIgnoreCase(id, "dls ") 
            ? DownloadableSounds.Load(stream).ToSF()
            : SoundFont2.Load(chunk, Util.Equals(id, "sfen"));
    }

    /// <summary>
    /// Merges sound banks with the given order. Keep in mind that the info read is copied from the first one
    /// </summary>
    /// <param name="soundBanks">The sound banks to merge, the first overwrites the last</param>
    /// <returns>The new merged soundbank</returns>
    /// <exception cref="ArgumentOutOfRangeException">Empty Soundbank list</exception>
    public static SoundBank Merge(List<SoundBank> soundBanks)
    {
        ArgumentOutOfRangeException.ThrowIfZero(
            soundBanks.Count, "No sound banks provided!");

        var mainSf = soundBanks[0];
        soundBanks.RemoveAt(0);

        var presets = new List<BasicPreset>(mainSf.Presets);
        while (soundBanks.Count > 0) 
        {
            var newPresets = soundBanks[0].Presets;
            soundBanks.RemoveAt(0);

            foreach (var newPreset in newPresets)
            {
                if (!presets.Any(oldPreset =>
                        newPreset.Matches(oldPreset.Patch.Data)))
                    presets.Add(newPreset);
            }
        }

        var b = new SoundBank();
        b.AddCompletePresets(presets);
        b.Info = mainSf.Info;
        return b;
    }

    /// <summary>Creates a simple sound bank with one saw wave preset.</summary>
    /// <returns></returns>
    public static ArraySegment<byte> GetSampleSoundBankFile()
    {
        const int samples = 128;
        var font = new SoundBank();
        var sampleData = new float[samples];
        
        for (var i = 0; i < samples; i++)
            sampleData[i] = (i / (float)samples) * 2 - 1;
        
        var sample = BasicSample.NewEmpty();
        sample.Name             = "Saw";
        sample.OriginalKey      = 65;
        sample.PitchCorrection  = 20;
        sample.LoopEnd          = samples;
        sample.SetAudioData(sampleData, 44_100);
        font.Samples.Add(sample);

        var inst = new BasicInstrument();
        inst.Name = "Saw Wave";
        inst.GlobalZone.Add(
            new Generator(Generator.Type.InitialAttenuation, 375),
            new Generator(Generator.Type.ReleaseVolEnv, -1_000),
            new Generator(Generator.Type.SampleModes, 1));

        /*discard*/inst.CreateZone(sample);
        var zone2 = inst.CreateZone(sample);
        zone2.Basic.SetGenerator(Generator.Type.FineTune, -9);

        font.Instruments.Add(inst);

        var preset = new BasicPreset(font);
        preset.Name = "Saw Wave";
        /*discard*/preset.CreateZone(inst);

        font.Presets.Add(preset);

        font.Info = font.Info with { Name = "SpessaSynth Sample Sound Bank" };
        font.Flush();
        return font.WriteSF2();
    }

    /// <summary> Copies a given sound bank. </summary>
    /// <param name="bank">The sound bank to copy.</param>
    /// <returns>The new copy</returns>
    public static SoundBank From(SoundBank bank)
    {
        var copied = new SoundBank();
        foreach (var p in bank.Presets) copied.Clone(p);
        copied.Info = bank.Info;
        return copied;
    }

    /// <summary>Adds complete presets along with their instruments and samples.</summary>
    /// <param name="presets">The presets to add.</param>
    public void AddCompletePresets(List<BasicPreset> presets) 
    {
        Presets.AddRange(Presets);

        // Ordered set
        var instrumentList = new OrderedDictionary<BasicInstrument, bool>();
        foreach (var preset in presets)
            foreach (var zone in preset.Zones)
                instrumentList.TryAdd(zone.Instrument, true);

        Instruments.AddRange(instrumentList.Keys);

        // Ordered set
        var sampleList = new OrderedDictionary<BasicSample, bool>();
        foreach (var instrument in instrumentList.Keys)
            foreach (var zone in instrument.Zones) 
                sampleList.TryAdd(zone.Sample, true);

        Samples.AddRange(sampleList.Keys);
    }

    /// <summary>A function to track progress during writing.</summary>
    public delegate void ProgressFunc(
        // Estimated progress, from 0 to 1.
        float progress);
    
    /// <summary>Write the sound bank as a .dls file. This may not be 100% accurate. Note that samples are always written in the s16le PCM encoding.</summary>
    /// <param name="options">Options for writing the file.</param>
    /// <remarks>If your soundbank is large (>2GB), use <see cref="WriteDLS(FileInfo, DLSWriteOptions?)"/>
    /// or <see cref="WriteDLS(Stream, DLSWriteOptions?)"/></remarks>
    /// <returns>The DLS in binary form.</returns>
    public ArraySegment<byte> WriteDLS(DLSWriteOptions? options = null)
    {
        var stream = new MemoryStream();
        WriteDLS(stream, options);
        stream.Seek(0, SeekOrigin.Begin);
        return stream.ToArray();
    }
    
    /// <summary>Write the sound bank as a .dls file. This may not be 100% accurate. Note that samples are always written in the s16le PCM encoding.</summary>
    /// <param name="file">The path to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">Options for writing the file.</param>
    public void WriteDLS(
        FileInfo file, DLSWriteOptions? options = null)
    {
        using var stream = file.OpenWrite();
        WriteDLS(stream, options);
    }

    /// <summary>Write the sound bank as a .dls file. This may not be 100% accurate. Note that samples are always written in the s16le PCM encoding.</summary>
    /// <param name="stream">The stream to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">Options for writing the file.</param>
    public void WriteDLS(Stream stream, DLSWriteOptions? options = null)
    {
        // First half (progress 0-0.5)
        var fProgress = options?.ProgressFunc;
        ProgressFunc? firstHalf = fProgress == null 
            ? null : p => fProgress(p * .5f);
        var dls = DownloadableSounds.FromSF(this, firstHalf);

        // Second half (progress 0.5-1)
        if (fProgress != null)
        {
            void SecondHalf(float p) => fProgress(.5f + p * .5f);
            options = options!.Value with { ProgressFunc = SecondHalf };
        }
        
        dls.Write(stream, options);
    }

    /// <summary> Writes the sound bank as an SF2 file. </summary>
    /// <param name="options">The options for writing.</param>
    /// <remarks>If your soundbank is large (>2GB), use <see cref="WriteSF2(FileInfo, SF2WriteOptions?)"/>
    /// or <see cref="WriteSF2(Stream, SF2WriteOptions?)"/></remarks>
    /// <returns>The binary file data.</returns>
    public ArraySegment<byte> WriteSF2(SF2WriteOptions? options = null)
    {
        var stream = new MemoryStream();
        Write.SF2(this, options ?? SF2WriteOptions.Default, stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream.ToArray();
    }

    /// <summary> Writes the sound bank as an SF2 file. </summary>
    /// <param name="file">The path to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">The options for writing.</param>
    public void WriteSF2(FileInfo file, SF2WriteOptions? options = null)
    {
        using var stream = file.OpenWrite();
        WriteSF2(stream, options);
    }
    
    /// <summary> Writes the sound bank as an SF2 file. </summary>
    /// <param name="stream">The stream to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">The options for writing.</param>
    public void WriteSF2(Stream stream, SF2WriteOptions? options = null) => 
        Write.SF2(this, options ?? SF2WriteOptions.Default, stream);
    
    /// <summary>
    /// Writes the sound bank as an [SFE 4](https://sfe-team-was-taken.github.io/SFE/) file.
    /// This enables features such as bank LSB and RIFF64.
    /// Note that spessasynth is currently the only software that can read these files.</summary>
    /// <param name="options">The options for writing.</param>
    /// <remarks>If your soundbank is large (>2GB), use <see cref="WriteSFE(FileInfo, SFEWriteOptions?)"/>
    /// or <see cref="WriteSFE(Stream, SFEWriteOptions?)"/></remarks>
    /// <returns>The binary file data.</returns>
    public ArraySegment<byte> WriteSFE(SFEWriteOptions? options = null)
    {
        var stream = new MemoryStream();
        Write.SFE(this, options ?? SFEWriteOptions.Default, stream);
        stream.Seek(0, SeekOrigin.Begin);
        return stream.ToArray();
    }

    /// <summary>Writes the sound bank as an SFE file.</summary>
    /// <param name="file">The path to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">The options for writing.</param>
    public void WriteSFE(FileInfo file, SFEWriteOptions? options = null)
    {
        using var stream = file.OpenWrite();
        WriteSFE(stream, options);
    }
    
    /// <summary>Writes the sound bank as an SFE file.</summary>
    /// <param name="stream">The stream to save the data. Use this for large (>2GB) soundbanks</param>
    /// <param name="options">The options for writing.</param>
    public void WriteSFE(Stream stream, SFEWriteOptions? options = null) => 
        Write.SFE(this, options ?? SFEWriteOptions.Default, stream);
    
    /// <summary> Clones a sample into this bank. </summary>
    /// <param name="sample">The sample to copy.</param>
    /// <returns>The copied sample, if a sample exists with that name, it is returned instead</returns>
    public BasicSample Clone(BasicSample sample)  
    {
        var duplicate = Samples.Find(s => 
            s.Name.Equals(sample.Name));

        if (duplicate != null) return duplicate;

        var newSample = new BasicSample(
            sample.Name,
            sample.Rate,
            sample.OriginalKey,
            sample.PitchCorrection,
            sample.SType,
            sample.LoopStart,
            sample.LoopEnd);

        if (sample.IsCompressed)
            newSample.SetCompressedData(sample.GetRawData(true));
        else
            newSample.SetAudioData(sample.GetAudioData(), sample.Rate);

        Samples.Add(newSample);
        
        if (sample.LinkedSample is {} linkedSample) 
        {
            var clonedLinked = Clone(linkedSample);
            // Sanity check
            if (clonedLinked.LinkedSample == null)
                newSample.SetLinkedSample(clonedLinked, newSample.SType);
        }

        return newSample;
    }
    
    /// <summary> Recursively clones an instrument into this sound bank, as well as its samples. </summary>
    /// <param name="instrument">The copied instrument, if an instrument exists with that name, it is returned instead.</param>
    /// <returns>The cloned instrument</returns>
    public BasicInstrument Clone(BasicInstrument instrument)
    {
        var duplicate = Instruments.Find(
            i => i.Name.Equals(instrument.Name));

        if (duplicate != null) return duplicate;

        var newInstrument = new BasicInstrument
        { Name = instrument.Name };
        newInstrument.GlobalZone.CopyFrom(instrument.GlobalZone);

        foreach (var zone in instrument.Zones) 
        {
            var copiedZone = newInstrument.CreateZone(
                Clone(zone.Sample));
            copiedZone.Basic.CopyFrom(zone.Basic);
        }
        
        Instruments.Add(newInstrument);
        return newInstrument;
    }
    
    /// <summary>Recursively clones a preset into this sound bank, as well as its instruments and samples.</summary>
    /// <param name="preset">The copied preset, if a preset exists with that name, it is returned instead.</param>
    /// <returns>The cloned preset</returns>
    public BasicPreset Clone(BasicPreset preset)
    {
        var duplicate = Presets.Find(
            p => p.Patch.Data.Equals(preset.Patch.Data));
        if (duplicate != null) return duplicate;
        
        var newPreset = new BasicPreset(this)
        {
            Patch   = preset.Patch,
            Library     = preset.Library,
            Genre       = preset.Genre,
            Morphology  = preset.Morphology,
        };
        
        newPreset.GlobalZone.CopyFrom(preset.GlobalZone);

        foreach (var zone in preset.Zones) 
        {
            var copiedZone = newPreset.CreateZone(Clone(zone.Instrument));
            copiedZone.Basic.CopyFrom(zone.Basic);
        }

        Presets.Add(newPreset);
        return newPreset;
    }
    
    // Updates internal values.
    public void Flush()
    {
        Util.Sort(Presets, (a, b) =>
            MidiPatch.Compare(a.Patch.Data, b.Patch.Data));

        ParseInternal();
    }

    /// <summary>
    /// Trims the sound bank _in-place_ to only contain samples in a given MIDI file.
    /// Absent presets will be removed from the sound bank, and samples that don't
    /// get activated in the remaining presets will be removed as well.
    /// </summary>
    /// <param name="mid">The MIDI file</param>
    public void Trim(Midi mid) =>
        Trim(mid.GetUsedProgramsAndKeys(this));

    /// <summary>
    /// Trims the sound bank _in-place_ to only contain samples in a given MIDI file.
    /// Absent presets will be removed from the sound bank, and samples that don't
    /// get activated in the remaining presets will be removed as well.
    /// </summary>
    /// <param name="presetData">A Map: BasicPreset -> (key-velocity).</param>
    public void Trim(PresetsWithKeyCombinations presetData)
    {
        SpessaLog.Info("Trimming sound bank ...");
        SpessaLog.Info($"Combinations to trim for: {presetData}");
        
        // Modify the sound bank to only include programs and samples that are used
        for (var i = 0; i < Presets.Count; i++) 
        {
            var p = Presets[i];
            if (!presetData.TryGetValue(p, out var keyCombos))
            {
                SpessaLog.Info($"Deleting preset {p.Name} and its zones");
                Delete(p);
                i--;
                continue;
            }

            SpessaLog.Info($"Trimming {p.Name}");
            SpessaLog.Info($"Keys for {p.Name}: {string.Join(',', keyCombos)}");
            var trimmedZones = 0;

            // Clean the preset to only use zones that are used
            for (
                var zoneIndex = 0;
                zoneIndex < p.Zones.Count;
                zoneIndex++)
            {
                var zone = p.Zones[zoneIndex];
                var keyRange = zone.Basic.KeyRange;
                var velRange = zone.Basic.VelRange;
                // Check if any of the combos matches the zone
                var isZoneUsed = false;
                foreach (var (key, velocity) in keyCombos)
                {
                    // Check if the key/velocity range matches
                    if (key       < keyRange.Min ||
                        key       > keyRange.Max ||
                        velocity  < velRange.Min ||
                        velocity  > velRange.Max) continue;

                    // Zone is used, trim the instrument zones
                    isZoneUsed = true;
                    var trimmedIZones = TrimInstrumentZones(
                        zone.Instrument, keyCombos);

                    SpessaLog.Info(
                        $"Trimmed off {trimmedIZones} instrument zones from {
                            zone.Instrument.Name}");
                    break;
                }

                if (isZoneUsed) continue;

                trimmedZones++;
                p.DeleteZone(zoneIndex);
                if (zone.Instrument.UseCount < 1)
                    Delete(zone.Instrument);
                zoneIndex--;
            }
                
            SpessaLog.Info($"Trimmed off {
                trimmedZones} preset zones from {p.Name}");
        }
        
        RemoveUnusedElements();

        SpessaLog.Info("Sound bank modified!");
        return;

        int TrimInstrumentZones(
            BasicInstrument instrument,
            HashSet<(int Key, int Velocity)> keyCombos)
        {
            var trimmedIZones = 0;
            for (
                var iZoneIndex = 0;
                iZoneIndex < instrument.Zones.Count;
                iZoneIndex++) 
            {
                var iZone = instrument.Zones[iZoneIndex];
                var iKeyRange = iZone.Basic.KeyRange;
                var iVelRange = iZone.Basic.VelRange;
                var isIZoneUsed = false;

                foreach (var (key, velocity) in keyCombos)
                {
                    // Check if the key/velicity range matches
                    if (key      < iKeyRange.Min ||
                        key      > iKeyRange.Max ||
                        velocity < iVelRange.Min ||
                        velocity > iVelRange.Max) continue;

                    isIZoneUsed = true;
                    break;
                }

                if (isIZoneUsed) continue;

                Debug.WriteLine(
                    $"{iZone.Sample.Name} removed from {instrument.Name}");

                if (instrument.DeleteZone(iZoneIndex)) 
                {
                    trimmedIZones++;
                    iZoneIndex--;
                    Debug.WriteLine($"{iZone.Sample.Name} deleted");
                }

                if (iZone.Sample.UseCount < 1)
                    Delete(iZone.Sample);
            }

            return trimmedIZones;
        }
    }

    public void RemoveUnusedElements()
    {
        Instruments.RemoveAll(i =>
        {
            i.DeleteUnusedZones();
            var deletable = i.UseCount <= 0;
            if (deletable) i.Delete();
            return deletable;
        });
        
        Samples.RemoveAll(s =>
        {
            var deletable = s.UseCount <= 0;
            if (deletable) s.UnlinkSample();
            return deletable;
        });
    }

    public void Delete(BasicInstrument instrument)
    {
        instrument.Delete();
        Instruments.Remove(instrument);
    }

    public void Delete(BasicPreset preset)
    {
        preset.Delete();
        Presets.Remove(preset);
    }

    public void Delete(BasicSample sample)
    {
        sample.UnlinkSample();
        Samples.Remove(sample);
    }

    /// <summary>Get the appropriate preset.</summary>
    /// <param name="patch"></param>
    /// <param name="system"></param>
    /// <returns></returns>
    public BasicPreset GetPreset(
        MidiPatch patch, Midi.System system) =>
            PresetSelector.Of(Presets, patch, system);

    public void Destroy()
    {
        Presets.Clear();
        Instruments.Clear();
        Samples.Clear();
    }
    
    internal static SpessaException.ParserSoundBank ParsingError(string msg) =>
        SpessaException.ParsingSoundBank(
            $"SF parsing error: {msg}. The file may be corrupted.");

    private void ParseInternal()
    {
        _isXGBank = false;
        // Definitions for XG:
        // At least one preset with bank 127, 126 or 120
        // MUST be a valid XG bank.
        // Allowed banks: (see XG specification)
        // Note: XG spec numbers the programs from 1...
        var allowedPrograms = new HashSet<int>([
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9/**/, 16, 17/**/,
            24, 25, 26, 27, 28, 29, 30,
            31, 32, 33/**/, 40, 41/**/, 48/**/, 56, 57, 58/**/,
            64, 65, 66/**/, 126, 127,
        ]);

        foreach (var preset in Presets)
        {
            if (!BankSelectHacks.IsXGDrum(preset.BankMSB)) continue;

            _isXGBank = true;
            if (allowedPrograms.Contains(preset.Program)) continue;
            
            // Not valid!
            Debug.WriteLine($"This bank is not valid XG. Preset {
                preset} is not a valid XG drum. XG mode will use presets on bank 128.");
            
            _isXGBank = false;
            break;
        }
    }
}