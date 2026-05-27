using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank.SoundFont;

internal static class Read
{
    /// <summary>Reads the modulator read</summary>
    /// <param name="modChunkData"></param>
    /// <returns></returns>
    public static List<Modulator> Modulators(ArraySegment<byte> modChunkData)
    {
        var mods = new List<Modulator>();

        while (modChunkData.Count > 0)
        {
            var sourceEnum = (short)Util.ReadLittleEndian(ref modChunkData, 2);
            var destination = (short)Util.ReadLittleEndian(ref modChunkData, 2);
            var amount = Util.SignedInt16(modChunkData[0], modChunkData[1]);
            modChunkData = modChunkData[2..];
            var secondarySourceEnum = (short)Util.ReadLittleEndian(ref modChunkData, 2);
            var transformType = (short)Util.ReadLittleEndian(ref modChunkData, 2);

            mods.Add(Modulator.Decoded(
                sourceEnum, 
                secondarySourceEnum, 
                destination,
                amount, 
                transformType));
        }
        
        // Remove terminal
        mods.RemoveAt(mods.Count - 1);
        return mods;
    }

    public static List<Generator> Generators(ArraySegment<byte> genChunkData)
    {
        var gens = new List<Generator>();
        while (genChunkData.Count > 0) 
            gens.Add(Read(ref genChunkData));

        // Remove terminal
        gens.RemoveAt(gens.Count - 1);
        
        return gens;

        // Creates a generator
        Generator Read(ref ArraySegment<byte> data)
        {
            // Note: We skip validation here as some sf2 files use invalid values that end up being correct after applying limits at the modulator level.
            // Test case: LiveHQ Natural soundfont GM, "Brass" preset (negative attenuation with quiet samples)
            // 4 bytes:
            // Type, type, type, value
            var genType = (Generator.Type)((data[1] << 8) | data[0]);
            var genVal = Util.SignedInt16(data[2], data[3]);
            data = data[4..];
            return new Generator(genType, genVal, false);
        }
    }

    public record struct SFInstrument(
        BasicInstrument Instrument,
        int ZoneStartIndex,
        int ZonesCount)
    {
        public BasicInstrument.Zone CreateSoundFontZone(
            ReadOnlySpan<Modulator> modulators,
            ReadOnlySpan<Generator> generators,
            List<BasicSample> samples)
        {
            var z = Construct(Instrument, modulators, generators, samples);
            return z;
        }

        public static void ApplyInstrumentZones(
            (List<int> Mod, List<int> Gen) indexes,
            List<Generator> generators,
            List<Modulator> modulators,
            List<BasicSample> samples,
            List<SFInstrument> instruments)
        {
            var insGens = CollectionsMarshal.AsSpan(generators);
            var insMods = CollectionsMarshal.AsSpan(modulators);
            
            var genStartIndexes = indexes.Gen;
            var modStartIndexes = indexes.Mod;

            var modIndex = 0;
            var genIndex = 0;

            foreach (ref var instrument in 
                     CollectionsMarshal.AsSpan(instruments))
            {
                for (var i = 0; i < instrument.ZonesCount; i++)
                {
                    var genStart = genStartIndexes[genIndex++];
                    var genEnd = genStartIndexes[genIndex];
                    var gens = insGens[genStart .. genEnd];
                    
                    var modStart = modStartIndexes[modIndex++];
                    var modEnd = modStartIndexes[modIndex];
                    var mods = insMods[modStart .. modEnd];
                    
                    // Check for global zone
                    if (Util.Any(gens, 
                            g => g.GType == Generator.Type.SampleID))
                    {
                        // Regular zone
                        instrument.CreateSoundFontZone(mods, gens, samples);
                    }
                    else
                    {
                        // Global zone
                        instrument.Instrument.GlobalZone.Add(gens);
                        instrument.Instrument.GlobalZone
                            .Modulators.AddRange(mods);
                    }
                }
            }
        }

        private static BasicInstrument.Zone Construct(
            BasicInstrument inst,
            ReadOnlySpan<Modulator> modulators,
            ReadOnlySpan<Generator> generators,
            List<BasicSample> samples)
        {
            var sampleIdx = -1;
            for (var i = 0; i < generators.Length; i++)
            {
                if (generators[i].GType != Generator.Type.SampleID)
                    continue;
                sampleIdx = i;
                break;
            }

            BasicSample? sample = null;

            if (sampleIdx != -1)
            {
                var index = generators[sampleIdx].Value;
                if (index >= 0 && index < samples.Count)
                    sample = samples[index];
            }
            else
                throw SpessaException.ParsingSoundBank(
                    "No sample ID found in instrument zone");

            if (sample == null)
                throw SpessaException.ParsingSoundBank(
                    $"Invalid sample ID: {
                        generators[sampleIdx].Value}, available samples: {
                            samples.Count}");

            var zone = new BasicInstrument.Zone(inst, sample);
            zone.Basic.Add(generators);
            zone.Basic.Modulators.AddRange(modulators);
            inst.Zones.Add(zone);

            return zone;
        }
    }
    
    /// <summary> Parses soundfont instrument and stores them as a class </summary>
    /// <param name="instrumentChunkData"></param>
    /// <returns></returns>
    public static List<SFInstrument> Instruments(
        ArraySegment<byte> instrumentChunkData)
    {
        var instruments = new List<SFInstrument>();
        while (instrumentChunkData.Count > 0)
        {
            var instrument = Read(ref instrumentChunkData);
            if (instruments.Count > 0)
            {
                ref var previous = ref CollectionsMarshal.AsSpan(
                    instruments)[^1];
                previous.ZonesCount =
                    instrument.ZoneStartIndex - previous.ZoneStartIndex;
            }
            
            instruments.Add(instrument);
        }
        
        // Remove EOI
        instruments.RemoveAt(instruments.Count - 1);
        return instruments;

        SFInstrument Read(ref ArraySegment<byte> data)
        {
            var inst = new BasicInstrument();
            inst.Name = Util.ToString(
                Util.ReadBinaryString(ref data, 20));
            var zoneStartIndex = Util.ReadLittleEndian(ref data, 2);

            return new SFInstrument(inst, zoneStartIndex, 0);
        }
    }

    /// <summary></summary>
    /// <param name="zoneChunk">Both pbag and ibag work</param>
    /// <returns></returns>
    public static (List<int> Mod, List<int> Gen) ZoneIndexes(
        ArraySegment<byte> zoneChunk)
    {
        var genStartIndexes = new List<int>();
        var modStartIndexes = new List<int>();

        while (zoneChunk.Count > 0)
        {
            genStartIndexes.Add(Util.ReadLittleEndian(ref zoneChunk, 2));
            modStartIndexes.Add(Util.ReadLittleEndian(ref zoneChunk, 2));
        }
        
        return (modStartIndexes, genStartIndexes);
    }
    
    /// <summary>Parses soundfont presets, also includes function for getting the generators and samples from midi note and velocity</summary>
    /// <param name="BasicPreset"></param>
    /// <param name="ZoneStartIndex"></param>
    /// <param name="ZonesCount"></param>
    public record struct SFPreset(
        BasicPreset BasicPreset,
        int ZoneStartIndex,
        int ZonesCount = 0)
    {
        public SFPreset(ref ArraySegment<byte> data, SoundBank sf2)
            : this(null!, 0, 0)
        {
            BasicPreset = new BasicPreset(sf2);
            
            var name = Util.ReadBinaryString(ref data, 20);
            var nameStr = Util.ToString(name);

            BasicPreset.Name        = nameStr;
            BasicPreset.Program     = Util.ReadLittleEndian(ref data, 2);
            var wBank            = Util.ReadLittleEndian(ref data, 2);
            BasicPreset.BankMSB     = wBank     & 0x7f;
            BasicPreset.IsGMGSDrum  = (wBank    & 0x80) > 0;
            BasicPreset.BankLSB     = wBank >> 8;
            
            ZoneStartIndex          = Util.ReadLittleEndian(ref data, 2);
            
            // Read the dword
            BasicPreset.Library     = ReadLittleEndianUint(ref data, 4);
            BasicPreset.Genre       = ReadLittleEndianUint(ref data, 4);
            BasicPreset.Morphology  = ReadLittleEndianUint(ref data, 4);
            return;

            uint ReadLittleEndianUint(ref ArraySegment<byte> data, int bytes) =>
                unchecked((uint)Util.ReadLittleEndian(ref data, bytes));
        }
        
        /// <summary>Creates a zone (preset)</summary>
        /// <param name="mods"></param>
        /// <param name="gens"></param>
        /// <param name="instruments"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public BasicPreset.Zone CreateSoundFontZone(
            ReadOnlySpan<Modulator> mods,
            ReadOnlySpan<Generator> gens,
            List<BasicInstrument> instruments)
        {
            BasicInstrument? instrument = null;
            if (Util.TryFind(
                    gens,
                    g => g.GType == Generator.Type.Instrument,
                    out var id))
            {
                if (id.Value >= 0 && id.Value < instruments.Count)
                    instrument = instruments[id.Value];
            }
            else
                throw SpessaException.ParsingSoundBank(
                    "No instrument ID found in preset zone.");
            
            if (instrument == null)
                throw SpessaException.ParsingSoundBank(
                    $"Invalid instrument ID: {
                        id.Value}, available instruments: {
                            instruments.Count}");
            
            var zone = new BasicPreset.Zone(
                new BasicZone(), BasicPreset, instrument);
            zone.Basic.Add(gens);
            zone.Basic.Modulators.AddRange(mods);
            BasicPreset.Zones.Add(zone);
            
            return zone;
        }
    }

    public static List<SFPreset> Presets(
        ArraySegment<byte> presetChunk, SoundBank parent)
    {
        var presets = new List<SFPreset>();

        while (presetChunk.Count > 0)
        {
            var preset = new SFPreset(ref presetChunk, parent);

            if (presets.Count > 0)
            {
                ref var prev = ref CollectionsMarshal
                    .AsSpan(presets)[^1];
                prev.ZonesCount = preset.ZoneStartIndex - prev.ZoneStartIndex;
            }
            
            presets.Add(preset);
        }
        
        // Remove EOP
        presets.RemoveAt(presets.Count - 1);
        return presets;
    }
    
    /// <summary>Reads the given preset zone</summary>
    /// <param name="indexes"></param>
    /// <param name="presetGens"></param>
    /// <param name="presetMods"></param>
    /// <param name="instruments"></param>
    /// <param name="presets"></param>
    public static void ApplyPresetZones(
        (List<int> Mod, List<int> Gen) indexes,
        ReadOnlySpan<Generator> presetGens,
        ReadOnlySpan<Modulator> presetMods,
        List<BasicInstrument> instruments,
        List<SFPreset> presets)
    {
        var genStartIndexes = indexes.Gen;
        var modStartIndexes = indexes.Mod;
        
        var modIndex = 0;
        var genIndex = 0;

        foreach (var preset in presets)
        {
            for (var i = 0; i < preset.ZonesCount; i++)
            {
                var gensStart = genStartIndexes[genIndex++];
                var gensEnd = genStartIndexes[genIndex];
                var gens = presetGens[gensStart .. gensEnd];
                
                var modsStart = modStartIndexes[modIndex++];
                var modsEnd = modStartIndexes[modIndex];
                var mods = presetMods[modsStart .. modsEnd];
                
                // Check for global zone
                if (Util.Any(gens, g => g.GType == Generator.Type.Instrument))
                {
                    // Regular zone
                    preset.CreateSoundFontZone(mods, gens, instruments);
                }
                else
                {
                    // Global zone
                    preset.BasicPreset.GlobalZone.Add(gens);
                    preset.BasicPreset.GlobalZone.Modulators
                        .AddRange(mods);
                }
            }
        }
    }
}