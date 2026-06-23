using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.SoundBank.SoundFont;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

/// <summary>Represents a single instrument</summary>
public sealed class BasicInstrument
{
    public const int INST_BYTE_SIZE = 22;

    private static readonly FrozenSet<Generator.Type> NotGlobalizedTypes = [
        Generator.Type.VelRange,
        Generator.Type.KeyRange,
        Generator.Type.Instrument,
        Generator.Type.SampleID,
        Generator.Type.ExclusiveClass,
        Generator.Type.EndOper,
        Generator.Type.SampleModes,
        Generator.Type.StartLoopAddrsOffset,
        Generator.Type.StartLoopAddrsCoarseOffset,
        Generator.Type.EndLoopAddrsOffset,
        Generator.Type.EndLoopAddrsCoarseOffset,
        Generator.Type.StartAddrsOffset,
        Generator.Type.StartAddrsCoarseOffset,
        Generator.Type.EndAddrsOffset,
        Generator.Type.EndAddrsCoarseOffset,
        Generator.Type.InitialAttenuation,  // Written into wsmp, there's no global wsmp
        Generator.Type.FineTune,            // Written into wsmp, there's no global wsmp
        Generator.Type.CoarseTune,          // Written into wsmp, there's no global wsmp
        Generator.Type.KeyNumToVolEnvHold,  // KEY TO SOMETHING:
        Generator.Type.KeyNumToVolEnvDecay, // Cannot be globalized as they modify their respective generators
        Generator.Type.KeyNumToModEnvHold,  // (for example, keyNumToVolEnvDecay modifies VolEnvDecay)
        Generator.Type.KeyNumToModEnvDecay,
        // SpessaSharp (0..58)
        Generator.Type.OverridingRootKey,
        Generator.Type.Invalid,
        Generator.Type.Unused5,
        Generator.Type.Amplitude,
        Generator.Type.VibLFORate,          
        Generator.Type.VibLFOAmplitudeDepth,
        Generator.Type.VibLFOToFilterFc,    
        Generator.Type.ModLFORate,          
        Generator.Type.ModLFOAmplitudeDepth,
    ];

    // Not these though
    private static readonly Generator.Type[] NotGlobalizedList =
        Util.Filter(Generator.List, NotGlobalizedTypes);

    public readonly record struct Zone
    {
        /// <summary>Basic zone information.</summary>
        public readonly BasicZone Basic;
        
        /// <summary>Zone's sample.</summary>
        public readonly BasicSample Sample;

        /// <summary>The instrument this zone belongs to.</summary>
        public readonly BasicInstrument ParentInstrument;

        /// <summary>For tracking on the individual zone level, since multiple presets can refer to the same instrument.</summary>
        public int UseCount { get; init; }

        /// <summary>Creates a new instrument zone.</summary>
        /// <param name="instrument">The parent instrument.</param>
        /// <param name="sample">The sample to use in this zone.</param>
        public Zone(BasicInstrument instrument, BasicSample sample)
        {
            Basic = new BasicZone();
            ParentInstrument = instrument;
            Sample = sample;
            sample.LinkTo(ParentInstrument);
            UseCount = instrument.UseCount;
        }

        public List<Generator> GetWriteGenerators(SoundBank bank)
        {
            var gens = Basic.GetWriteGenerators();
            var sampleID = bank.Samples.IndexOf(Sample);
            if (sampleID == -1)
                throw new ArgumentException(
                    $"{Sample.Name} does not exist in {
                        bank.Info.Name}! Cannot write sampleID generator.");

            gens.Add(new Generator(Generator.Type.SampleID, sampleID, false));
            return gens;
        }
    }

    /// <summary> The instrument's name </summary>
    public string Name { get; set; } = "";

    /// <summary> The instrument's zones </summary>
    public readonly List<Zone> Zones = [];

    /// <summary> Instrument's global zone </summary>
    public readonly BasicZone GlobalZone = new ();

    /// <summary>Instrument's linked presets (the presets that use it). Note that duplicates are allowed since one preset can use the same instrument multiple times.</summary>
    public readonly List<BasicPreset> LinkedTo = [];
    
    /// <summary>How many presets is this instrument used by</summary>
    public int UseCount => LinkedTo.Count;
    
    /// <summary>Creates a new instrument zone and returns it.</summary>
    /// <param name="sample">The sample to use in the zone.</param>
    /// <returns></returns>
    public Zone CreateZone(BasicSample sample)
    {
        var zone = new Zone(this, sample);
        Zones.Add(zone);
        return zone;
    }
    
    /// <summary>Links the instrument to a given preset</summary>
    /// <param name="preset">The preset to link to</param>
    public void LinkTo(BasicPreset preset) 
    {
        LinkedTo.Add(preset);
        foreach (ref var zone in CollectionsMarshal.AsSpan(Zones))
            zone = zone with { UseCount = zone.UseCount + 1 };
    }
    
    /// <summary>Unlinks the instrument from a given preset</summary>
    /// <param name="preset">The preset to unlink from</param>
    /// <exception cref="ArgumentException"></exception>
    public void UnlinkFrom(BasicPreset preset) 
    {
        var index = LinkedTo.IndexOf(preset);
        if (index == -1)
            // SpessaSharp: originally a warning
            throw new ArgumentException(
                $"Cannot unlink {preset.Name} from {Name}: not linked.");

        LinkedTo.RemoveAt(index);
        foreach (ref var zone in CollectionsMarshal.AsSpan(Zones))
            zone = zone with { UseCount = zone.UseCount - 1 };
    }
    
    /// <summary>Deletes unused zones of the instrument</summary>
    public void DeleteUnusedZones()
    {
        Zones.RemoveAll(z =>
        {
            var leaves = z.UseCount <= 0;
            if (leaves) z.Sample.UnlinkFrom(this);
            
            return leaves;
        });
    }
    
    /// <summary>Unlinks everything from this instrument</summary>
    /// <exception cref="InvalidOperationException"></exception>
    public void Delete() 
    {
        if (UseCount > 0)
            throw new InvalidOperationException(
                "Cannot delete an instrument that is used by: " +
                $"{string.Join(',', LinkedTo.Select(p => p.Name))}");

        foreach (var z in Zones) 
            z.Sample.UnlinkFrom(this);
    }
    
    /// <summary>Deletes a given instrument zone if it has no uses</summary>
    /// <param name="index">The index of the zone to delete</param>
    /// <param name="force">Tgnores the use count and deletes forcibly</param>
    /// <returns>If the zone has been deleted</returns>
    public bool DeleteZone(int index, bool force = false)
    {
        ref var zone = ref CollectionsMarshal.AsSpan(Zones)[index];
        zone = zone with { UseCount = zone.UseCount - 1 };

        if (zone.UseCount >= 1 && !force) return false;

        zone.Sample.UnlinkFrom(this);
        Zones.RemoveAt(index);
        return true;
    }
    
    /// <summary>
    /// Globalizes the instrument <b>in-place</b>. This means trying to move as many generators and modulators to the global zone as possible to reduce clutter and the count of parameters.
    /// </summary>
    public void Globalize() 
    {
        var globalZone = GlobalZone;

        // Create a global zone and add repeating generators to it
        // Also modulators
        // Iterate over every type of generator
        var occurrencesForValues =
            new Dictionary<int, (int Count, int Index)>();
        
        foreach (var type in NotGlobalizedList)
        {
            var defaultForType =
                Generator.Limits.TryGetValue(type, out var v)
                    ? v.Def : 0;

            var index = 0;
            occurrencesForValues.Clear();
            occurrencesForValues[defaultForType] = (0, index++);

            foreach (var zone in Zones)
            {
                var key = zone.Basic.GetGenerator(type) ?? defaultForType;
                ref var val = ref CollectionsMarshal
                    .GetValueRefOrAddDefault(
                        occurrencesForValues, key, out var exists);
                if (exists) val.Count++;
                else val = (1, index++);

                // If the checked type has the keyNumTo something generator set, it cannot be globalized.
                var relativeCounterpart = Generator.Type.Invalid;
                
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (type) 
                {
                    case Generator.Type.DecayVolEnv:
                        relativeCounterpart = Generator.Type.KeyNumToVolEnvDecay;
                        break;
                    case Generator.Type.HoldVolEnv:
                        relativeCounterpart = Generator.Type.KeyNumToVolEnvHold;
                        break;
                    case Generator.Type.DecayModEnv:
                        relativeCounterpart = Generator.Type.KeyNumToModEnvDecay;
                        break;
                    case Generator.Type.HoldModEnv:
                        relativeCounterpart = Generator.Type.KeyNumToModEnvHold;
                        break;
                    default: continue;
                }

                if (zone.Basic.GetGenerator(relativeCounterpart) != null) 
                {
                    occurrencesForValues.Clear();
                    break;
                }
            }

            // If at least one occurrence, find the most used one and add it to global
            if (occurrencesForValues.Count <= 0) continue;
            
            // [value, occurrences]
            var jsOrder = occurrencesForValues
                .Select(e => (
                    Value: e.Key, e.Value.Count, e.Value.Index))
                .OrderBy(x => x.Value >= 0 ? 0 : 1)
                .ThenBy(x => x.Value >= 0 ? x.Value : x.Index);
            
            var valueToGlobalize = (Value: 0, Ocurrences: 0);
            foreach (var (value, count, _) in jsOrder) 
            {
                if (count > valueToGlobalize.Ocurrences)
                    valueToGlobalize = (Value: value, Ocurrences: count);
            }

            var targetValue = valueToGlobalize.Value;

            // If the global value is the default value just remove it, no need to add it
            if (targetValue != defaultForType) 
                globalZone.SetGenerator(type, targetValue, false);

            // Remove from the zones
            foreach (var z in Zones)
            {
                if (z.Basic.GetGenerator(type) is not {} genValue) 
                {
                    // That type does not exist at all here.
                    // Since we're globalizing, we need to add the default here.
                    if (targetValue != defaultForType)
                        z.Basic.SetGenerator(type, defaultForType);
                } 
                else if (genValue == targetValue) 
                {
                    // That exact value exists. Since it's global now, remove it
                    z.Basic.SetGenerator(type, null);
                }
            }
        }

        // Globalize only modulators that exist in all zones
        var firstZoneMods = Zones.Count > 0 
            ? Zones[0].Basic.Modulators.ToArray() // Copy
            : [];

        foreach (var checkedModulator in firstZoneMods)
        {
            var existsForAllZones = true;
            foreach (var zone in Zones) 
            {
                // Check if that zone has an existing modulator
                foreach (var m in zone.Basic.Modulators)
                    if (Modulator.IsIdentical(m, checkedModulator))
                        goto Exists;

                // Does not exist for this zone, so it's not global.
                existsForAllZones = false;
                break;

                // Exists.
                Exists:;
            }

            if (!existsForAllZones) continue;

            globalZone.Modulators.Add(checkedModulator);

            // Delete it from local zones.
            foreach (var zone in Zones) 
            {
                for (var i = 0; i < zone.Basic.Modulators.Count; i++)
                {
                    var m = zone.Basic.Modulators[i];
                    if (!Modulator.IsIdentical(m, checkedModulator))
                        continue;

                    // Check if the amount is correct.
                    // If so, delete it since it's global.
                    // If not, then it will simply override global as it's identical.
                    if (m.TransformAmount == checkedModulator.TransformAmount)
                        zone.Basic.Modulators.RemoveAt(i);
                    break;
                }
            }
        }
    }
    
    internal void Write(ExtendedSF2Chunks instData, int index) 
    {
        Debug.WriteLine($"Writing Instrument '{Name}' ...");

        // Split up the name
        Util.WriteBinaryString(ref instData.pdta, 
            Util.SafeSlice(Name, end: 20), 20);
        Util.WriteBinaryString(ref instData.xdta,
            Util.SafeSlice(Name, start: 20), 20);
        
        // Inst start index
        Util.WriteWord(ref instData.pdta, (short)(index & 0xff_ff));
        Util.WriteWord(ref instData.xdta, (short)(index >>> 16));
    }
}