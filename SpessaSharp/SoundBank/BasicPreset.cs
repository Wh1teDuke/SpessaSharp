using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank.SoundFont;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

public sealed class BasicPreset
{
    public readonly record struct Zone
    {
        /// <summary>Basic zone information.</summary>
        public readonly BasicZone Basic;
        
        /// <summary>Zone's instrument.</summary>
        public readonly BasicInstrument Instrument;
    
        /// <summary>The preset this zone belongs to.</summary>
        public readonly BasicPreset Parent;

        /// <summary>Creates a new preset zone.</summary>
        /// <param name="basic"></param>
        /// <param name="preset">The preset this zone belongs to.</param>
        /// <param name="instrument">The instrument to use in this zone.</param>
        public Zone(
            BasicZone basic, BasicPreset preset, BasicInstrument instrument)
        {
            Basic = basic;
            Parent = preset;
            Instrument = instrument;
            Instrument.LinkTo(Parent);
        }

        public List<Generator> GetWriteGenerators(SoundBank bank)
        {
            var gens = Basic.GetWriteGenerators();

            var instrumentID = bank.Instruments.IndexOf(Instrument);
            if (instrumentID == -1)
                throw new ArgumentException(
                    $"{Instrument.Name} does not exist in {
                        bank.Info.Name}! Cannot write instrument generator.");

            gens.Add(new Generator(
                Generator.Type.Instrument, instrumentID, false));
            return gens;
        }
    }
    
    public const int PHDR_BYTE_SIZE = 38;

    private static readonly short[] DefaultGeneratorValues = 
        new short[Generator.Amount];

    static BasicPreset()
    {
        for (var i = 0; i < DefaultGeneratorValues.Length; i++)
        {
            if (!Generator.Limits.TryGetValue((Generator.Type)i, out var value))
                continue;
            DefaultGeneratorValues[i] = value.Def;
        }
    }
    
    public MidiPatch.Full Patch;
    /// <summary>The parent soundbank instance. Currently used for determining default modulators and XG status.</summary>
    public readonly SoundBank Parent;
    
    /// <summary>The preset's zones</summary>
    public readonly List<Zone> Zones = [];
    /// <summary>Preset's global zone</summary>
    public readonly BasicZone GlobalZone;
    
    /// <summary>Unused metadata</summary>
    public uint Library;
    /// <summary>Unused metadata</summary>
    public uint Genre;
    /// <summary>Unused metadata</summary>
    public uint Morphology;

    public string Name
    {
        get => Patch.Name;
        set => Patch = Patch with { Name = value };
    }

    public int BankMSB
    {
        get => Patch.BankMSB;
        set => Patch = Patch with { Data = Patch.Data with { BankMSB = value }};
    }

    public int BankLSB
    {
        get => Patch.BankLSB;
        set => Patch = Patch with { Data = Patch.Data with { BankLSB = value }};
    }

    public int Program
    {
        get => Patch.Program;
        set => Patch = Patch with { Data = Patch.Data with { Program = value }};
    }

    public bool IsGMGSDrum
    {
        get => Patch.IsGMGSDrum;
        set => Patch = Patch with { Data = Patch.Data with { IsGMGSDrum = value }};
    }
    
    public bool IsXGDrum => Patch.IsXGDrum;

    /// <summary>Creates a new preset representation.</summary>
    /// <param name="parent">The sound bank this preset belongs to.</param>
    /// <param name="globalZone">Optional, a global zone to use.</param>
    /// <param name="patch"></param>
    public BasicPreset(
        SoundBank parent,
        BasicZone? globalZone = null,
        MidiPatch.Full patch = new ())
    {
        Parent = parent;
        GlobalZone = globalZone ?? new BasicZone();
        Patch = patch;
    }
    
    /// <summary>Checks if this preset is a drum preset</summary>
    public bool IsDrum =>
        IsGMGSDrum || (Parent.IsXGBank && BankSelectHacks.IsXGDrum(BankMSB));
    
    /// <summary>Unlinks everything from this preset.</summary>
    public void Delete() => 
        Zones.ForEach(z => z.Instrument.UnlinkFrom(this));
    
    /// <summary>Deletes an instrument zone from this preset.</summary>
    /// <param name="index">The zone's index to delete.</param>
    public void DeleteZone(int index) 
    {
        Zones[index].Instrument.UnlinkFrom(this);
        Zones.RemoveAt(index);
    }
    
    /// <summary>Creates a new preset zone and returns it.</summary>
    /// <param name="instrument">The instrument to use in the zone.</param>
    /// <returns></returns>
    public Zone CreateZone(BasicInstrument instrument)
    {
        var z = new Zone(new BasicZone(), this, instrument);
        Zones.Add(z);
        return z;
    }

    /// <summary>Preloads (loads and caches synthesis data) for a given key range.</summary>
    /// <param name="keyMin"></param>
    /// <param name="keyMax"></param>
    public void Preload(int keyMin, int keyMax) 
    {
        var cache = new CachedVoice.Base.Cache(null);
        for (var key = keyMin; key < keyMax + 1; key++) 
        {
            for (var velocity = 0; velocity < 128; velocity++)
            {
                var list = GetVoiceParameters(cache, key, velocity);
                foreach (var synthesisData in list)
                    synthesisData.Item2.Sample.GetAudioData();
                Util.Return(list);
            }
        }
    }

    /// <summary>Checks if the bank and program numbers are the same for the given preset as this one.</summary>
    /// <param name="preset">The preset to check.</param>
    /// <returns></returns>
    public bool Matches(MidiPatch preset) => Patch.Data.Matches(preset);

    public readonly ref struct InstrumentZoneEnumerable(
        BasicPreset preset, int note, int velocity)
    {
        public InstrumentZoneEnumerator GetEnumerator() =>
            new (preset, note, velocity);
    }

    public ref struct InstrumentZoneEnumerator(
        BasicPreset preset, int key, int vel)
    {
        public readonly record struct Entry(
            Zone PresetZone, BasicInstrument.Zone InstrumentZone);
        
        private int _zoneIndex = 0;
        private int _instZoneIndex = int.MaxValue;

        private Entry? _current;
        public Entry Current => _current!.Value;

        public bool MoveNext()
        {
            _current = null;
            
            while (true)
            {
                if (_zoneIndex >= preset.Zones.Count)
                    return false;

                var pZone = preset.Zones[_zoneIndex];
                var instrument = pZone.Instrument;

                if (_instZoneIndex >= instrument.Zones.Count)
                {
                    _instZoneIndex = int.MaxValue;
                    var pKeyRange = pZone.Basic.HasKeyRange
                        ? pZone.Basic.KeyRange
                        : preset.GlobalZone.KeyRange;

                    var pVelRange = pZone.Basic.HasVelRange
                        ? pZone.Basic.VelRange
                        : preset.GlobalZone.VelRange;

                    // Local range overrides over global
                    if (instrument.Zones.Count == 0 ||
                        !(InRange(pKeyRange, key) && InRange(pVelRange, vel)))
                    {
                        _zoneIndex++;
                        continue;
                    }
                    
                    _instZoneIndex = 0;
                }

                var iZone = instrument.Zones[_instZoneIndex++];
                
                if (_instZoneIndex >= instrument.Zones.Count)
                {
                    _instZoneIndex = int.MaxValue;
                    _zoneIndex++;
                }

                var iKeyRange = iZone.Basic.HasKeyRange
                    ? iZone.Basic.KeyRange
                    : instrument.GlobalZone.KeyRange;
                var iVelRange = iZone.Basic.HasVelRange
                    ? iZone.Basic.VelRange
                    : instrument.GlobalZone.VelRange;

                if (!(InRange(iKeyRange, key) && InRange(iVelRange, vel)))
                    continue;

                _current = new Entry(pZone, iZone);
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool InRange((int Min, int Max) range, int number) =>
                number >= range.Min && number <= range.Max;
        }
    }

    public InstrumentZoneEnumerable ZonesInRange(int key, int vel) =>
        new (this, key, vel);

    /// <summary>Returns the voice synthesis data for this preset.</summary>
    /// <param name="note">The MIDI note number.</param>
    /// <param name="velocity">The MIDI velocity.</param>
    /// <returns>The returned sound data.</returns>
    internal ArraySegment<((BasicZone, BasicZone), Voice.Parameters)> 
        GetVoiceParameters(
            CachedVoice.Base.Cache cCache, int note, int velocity)
    {
        var voiceParameters =
            Util.Rent<((BasicZone, BasicZone), Voice.Parameters)>(16);
        var paramCount = 0;
        var presetGenerators = Util.Rent<short>(Generator.Amount);
        List<Modulator>? presetModulators = null;
        List<Modulator>? modulators = null;
        Zone? prevZone = null;

        // Filter zones out of range
        foreach (var (pZone, iZone) in ZonesInRange(note, velocity))
        {
            var key = (pZone.Basic, iZone.Basic);
            if (cCache.TryGetParams(key) is {} vParams)
            {
                AddParams(key, vParams);
                continue;
            }

            if (prevZone != pZone)
            {
                prevZone = pZone;
            
                // Preset generator list (offsets)
                presetGenerators.AsSpan().Clear();
                presetModulators?.Clear();

                if (GlobalZone.Generators.Count + pZone.Basic.Generators.Count > 0)
                {
                    // Firstly set global generators
                    foreach (var generator in GlobalZone.Generators)
                        if ((int)generator.GType < presetGenerators.Count)
                            presetGenerators[(int)generator.GType] = generator.Value;

                    // Then local, which will override them!
                    foreach (var generator in pZone.Basic.Generators)
                        if ((int)generator.GType < presetGenerators.Count)
                            presetGenerators[(int)generator.GType] = generator.Value;                    
                }

                if (GlobalZone.Modulators.Count + pZone.Basic.Modulators.Count > 0)
                {
                    presetModulators ??= [];
                    
                    // Preset modulators (add global to local)
                    presetModulators.AddRange(pZone.Basic.Modulators);
                
                    AddUniqueModulators(
                        presetModulators, GlobalZone.Modulators);
                }
            }
            
            var instrument = iZone.ParentInstrument;
            // Modulators
            modulators ??= [];
            modulators.Clear();
            modulators.AddRange(iZone.Basic.Modulators);
            // Add unique from global zone
            AddUniqueModulators(modulators, instrument.GlobalZone.Modulators);

            // Add unique default modulators
            AddUniqueModulators(modulators, Parent.DefaultModulators);

            // Sum preset and instrument modulators (sum their amounts) sf spec page 54, section 9.5
            if (presetModulators != null) foreach (
                var presetMod in presetModulators)
            {
                // Find a matching modulator to sum
                var found = false;
                foreach (ref var m in CollectionsMarshal.AsSpan(modulators))
                {
                    if (!Modulator.IsIdentical(presetMod, m))
                        continue;

                    // An identical instrument modulator, add the amounts
                    // This makes a new modulator
                    // Because otherwise it would overwrite the one in the sound bank!
                    // Replaces the original instrument modulator
                    m = m.SumTransform(presetMod);
                    found = true;
                    break;
                }

                if (!found)
                    // No match, add directly
                    modulators.Add(presetMod);
            }

            // Default generator values
            var generators = DefaultGeneratorValues.ToArray();
            // Overridden by global generators
            foreach (var generator in instrument.GlobalZone.Generators)
                generators[(int)generator.GType] = generator.Value;

            // Overridden by local generators!
            foreach (var generator in iZone.Basic.Generators)
                generators[(int)generator.GType] = generator.Value;

            // Sum the generators
            for (var i = 0; i < generators.Length; i++) 
            {
                // Limits are applied in the compute_modulator function
                // Clamp to prevent short from overflowing
                // Testcase: Sega Genesis soundfont (spessasynth/#169) adds 20,999 and the default 13,500 to initialFilterFc
                // Which is more than 32k
                generators[i] = (short)Math.Clamp(
                    generators[i] + presetGenerators[i],
                    short.MinValue, short.MaxValue);
            }

            // EMU initial attenuation correction, multiply initial attenuation by 0.4!
            // All EMU sound cards have this quirk, and all sf2 editors and players emulate it too
            generators[(int)Generator.Type.InitialAttenuation] =
                (short)Math.Floor(generators[
                    (int)Generator.Type.InitialAttenuation] * .4);

            var newVParams = new Voice.Parameters
            {
                Zone        = iZone,
                Generators  = generators,
                Modulators  = modulators.ToArray(),
            };

            AddParams(key, newVParams);
            cCache.Add(key, newVParams);
        }
        
        Util.Return(presetGenerators);
        return voiceParameters[..paramCount];
        
        void AddParams((BasicZone, BasicZone) key, Voice.Parameters newVParams)
        {
            if (paramCount >= voiceParameters.Count)
                Util.Grow(ref voiceParameters, paramCount * 2);
            voiceParameters[paramCount++] = (key, newVParams);
        }

        static void AddUniqueModulators(
            List<Modulator> main, List<Modulator> adder)
        {
            foreach (var addedMod in adder)
            {
                var found = false;
                foreach (var newMod in main)
                {
                    if (!Modulator.IsIdentical(addedMod, newMod)) continue;
                    found = true;
                    break;
                }
            
                if (!found) main.Add(addedMod);
            }
        }
    }

    /// <summary>BankMSB:BankLSB:Program:IsGMGSDrum</summary>
    /// <returns></returns>
    public string ToMidiString() => Patch.Data.ToMidiString();

    public override string ToString() => Patch.ToFullMidiString();
    
    /// <summary>
    /// Combines preset into an instrument, flattening the preset zones into instrument zones. This is a really complex function that attempts to work around the DLS limitations of only having the instrument layer.
    /// </summary>
    /// <returns>The instrument containing the flattened zones. In theory, it should exactly the same as this preset.</returns>
    public BasicInstrument ToFlattenedInstrument()
    {
        var outputInstrument = new BasicInstrument();
        outputInstrument.Name = Name;

        // Find the global zone and apply ranges, generators, and modulators
        var globalPresetZone = GlobalZone;
        var globalPresetGenerators =
            CollectionsMarshal.AsSpan(globalPresetZone.Generators);
        var globalPresetModulators =
            CollectionsMarshal.AsSpan(globalPresetZone.Modulators);
        
        var globalPresetKeyRange = globalPresetZone.KeyRange;
        var globalPresetVelRange = globalPresetZone.VelRange;
        
        // For each non-global preset zone
        foreach (var presetZone in Zones) 
        {
            // Use global ranges if not provided
            var presetZoneKeyRange = presetZone.Basic.KeyRange;
            if (!presetZone.Basic.HasKeyRange)
                presetZoneKeyRange = globalPresetKeyRange;

            var presetZoneVelRange = presetZone.Basic.VelRange;
            if (!presetZone.Basic.HasVelRange)
                presetZoneVelRange = globalPresetVelRange;

            // Add unique generators and modulators from the global zone
            var presetGenerators = Generator.Copy(
                [], presetZone.Basic.Generators);
            AddUniqueGens(presetGenerators, globalPresetGenerators);

            var presetModulators =
                new List<Modulator>(presetZone.Basic.Modulators);
            AddUniqueMods(presetModulators, globalPresetModulators);

            var instrument = presetZone.Instrument;
            IReadOnlyList<BasicInstrument.Zone> iZones = instrument.Zones;

            var globalInstZone = instrument.GlobalZone;
            var globalInstGenerators =
                CollectionsMarshal.AsSpan(globalInstZone.Generators);
            var globalInstModulators =
                CollectionsMarshal.AsSpan(globalInstZone.Modulators);

            var globalInstKeyRange = globalInstZone.KeyRange;
            var globalInstVelRange = globalInstZone.VelRange;
            
            // For each non-global instrument zone
            foreach (var instZone in iZones) 
            {
                // Use global ranges if not provided
                var instZoneKeyRange = instZone.Basic.KeyRange;
                if (!instZone.Basic.HasKeyRange)
                    instZoneKeyRange = globalInstKeyRange;

                var instZoneVelRange = instZone.Basic.VelRange;
                if (!instZone.Basic.HasVelRange)
                    instZoneVelRange = globalInstVelRange;

                instZoneKeyRange = Subtract(
                    instZoneKeyRange, presetZoneKeyRange);
                instZoneVelRange = Subtract(
                    instZoneVelRange, presetZoneVelRange);

                // If either of the zones is out of range (i.e.m min larger than the max),
                // Then we discard that zone
                if (instZoneKeyRange.Max < instZoneKeyRange.Min ||
                    instZoneVelRange.Max < instZoneVelRange.Min)
                    continue;

                // Add unique generators and modulators from the global zone
                var instGenerators =
                    Generator.Copy([],  instZone.Basic.Generators);
                AddUniqueGens(instGenerators, globalInstGenerators);
                var instModulators = new List<Modulator>(instZone.Basic.Modulators);
                AddUniqueMods(instModulators, globalInstModulators);

                // Sum preset modulators to instruments (amount) sf spec page 54
                var finalModList = instModulators;
                foreach (var mod in presetModulators) 
                {
                    var identicalInstMod = finalModList.FindIndex(
                        m => Modulator.IsIdentical(mod, m));
                    if (identicalInstMod == -1)
                        finalModList.Add(mod);
                    else 
                    {
                        // Sum the amounts
                        // (this makes a new modulator)
                        // Because otherwise it would overwrite the one in the soundfont!
                        finalModList[identicalInstMod] =
                            finalModList[identicalInstMod].SumTransform(mod);
                    }
                }

                // Clone the generators as the values are modified during DLS conversion (keyNumToSomething)
                var finalGenList =
                    Generator.Copy([], instGenerators);
                foreach (var gen in presetGenerators) 
                {
                    if (gen.GType is 
                        Generator.Type.VelRange     or
                        Generator.Type.KeyRange     or
                        Generator.Type.Instrument   or
                        Generator.Type.EndOper      or
                        Generator.Type.SampleModes) continue;

                    var identicalInstGen = instGenerators.FindIndex(
                        g => g.GType == gen.GType);

                    if (identicalInstGen == -1) 
                    {
                        // If not, sum to the default generator
                        var newAmount =
                            Generator.Limits[gen.GType].Def + gen.Value;
                        finalGenList.Add(new Generator(gen.GType, newAmount));
                    }
                    else 
                    {
                        // If exists, sum to that generator
                        var newAmount =
                            finalGenList[identicalInstGen].Value + gen.Value;
                        finalGenList[identicalInstGen] = new Generator(
                            gen.GType, newAmount);
                    }
                }

                // Remove unwanted
                finalGenList.RemoveAll(g =>
                    g.GType == Generator.Type.SampleID ||
                    g.GType == Generator.Type.KeyRange ||
                    g.GType == Generator.Type.VelRange ||
                    g.GType == Generator.Type.EndOper ||
                    g.GType == Generator.Type.Instrument ||
                    (Generator.Limits.TryGetValue(g.GType, out var limit) &&
                     limit.Def == g.Value));

                // Create the zone and copy over values
                var zone = outputInstrument.CreateZone(instZone.Sample);
                zone.Basic.KeyRange = instZoneKeyRange;
                zone.Basic.VelRange = instZoneVelRange;
                if (zone.Basic.KeyRange is { Min: 0, Max: 127 })
                    zone.Basic.KeyRange.Min = -1;
                if (zone.Basic.VelRange is { Min: 0, Max: 127 })
                    zone.Basic.VelRange.Min = -1;

                zone.Basic.Add(CollectionsMarshal.AsSpan(finalGenList));
                zone.Basic.Modulators.AddRange(finalModList);
            }
        }

        return outputInstrument;
        
        void AddUniqueGens(List<Generator> main, ReadOnlySpan<Generator> adder)
        {
            foreach (var g1 in adder)
            {
                foreach (var g2 in main)
                    if (g2.GType == g1.GType) goto DontAdd;
                main.Add(g1);
                DontAdd:;
            }
        }

        void AddUniqueMods(List<Modulator> main, ReadOnlySpan<Modulator> adder)
        {
            foreach (var m1 in adder)
            {
                foreach (var m2 in main)
                    if (Modulator.IsIdentical(m2, m1)) goto DontAdd;
                main.Add(m1);
                DontAdd:;
            }
        }

        static (int Min, int Max) Subtract(
            (int Min, int Max) r1, (int Min, int Max) r2) => 
            (Math.Max(r1.Min, r2.Min), Math.Min(r1.Max, r2.Max));
    }
    
    /// <summary>Writes the SF2 header</summary>
    /// <param name="phdrData"></param>
    /// <param name="index"></param>
    public void Write(ExtendedSF2Chunks phdrData, int index) 
    {
        Debug.WriteLine($"Writing Preset '{Name}' ...");
        
        // Split up the name
        Util.WriteBinaryString(ref phdrData.pdta, 
            Util.SafeSlice(Name, end: 20), 20);
        Util.WriteBinaryString(ref phdrData.xdta, 
            Util.SafeSlice(Name, start: 20), 20);

        Util.WriteWord(ref phdrData.pdta, (short)Program);
        var wBank = BankMSB;
        if (IsGMGSDrum) 
        {
            // Drum flag
            wBank = 0x80;
        } 
        else if (BankMSB == 0) 
        {
            // If bank MSB is zero, write bank LSB (XG)
            wBank = BankLSB;
        }
        Util.WriteWord(ref phdrData.pdta, (short)wBank);
        // Skip wBank and wProgram
        phdrData.xdta = phdrData.xdta[4..];

        Util.WriteWord(ref phdrData.pdta, (short)(index & 0xff_ff));
        Util.WriteWord(ref phdrData.xdta, (short)(index >> 16));

        // 3 unused dword, spec says to keep em so we do
        Util.WriteDword(ref phdrData.pdta, unchecked((int)Library));
        Util.WriteDword(ref phdrData.pdta, unchecked((int)Genre));
        Util.WriteDword(ref phdrData.pdta, unchecked((int)Morphology));
        phdrData.xdta = phdrData.xdta[12..];
    }
}