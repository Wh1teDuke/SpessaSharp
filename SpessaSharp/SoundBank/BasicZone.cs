using System.Runtime.InteropServices;

namespace SpessaSharp.SoundBank;

public sealed class BasicZone
{
    public const int BagByteSize = 4;
    
    /// <summary>The zone's velocity range. <b>Min -1</b> means that it is a default value</summary>
    public (int Min, int Max) VelRange = (Min: -1, Max: 127);
    
    /// <summary>The zone's key range. <b>Min -1</b> means that it is a default value</summary>
    public (int Min, int Max) KeyRange = (Min: -1, Max: 127);
    
    /// <summary>The zone's generators.</summary>
    public readonly List<Generator> Generators = [];
    /// <summary>The zone's modulators.</summary>
    public readonly List<Modulator> Modulators = [];

    public bool HasKeyRange => KeyRange.Min != -1;
    public bool HasVelRange => VelRange.Min != -1;
    
    /// <summary>
    /// The current tuning in cents, taking in both coarse and fine generators.
    /// </summary>
    public int FineTuning
    {
        get =>
            GetGenerator(Generator.Type.CoarseTune, 0) * 100 +
            GetGenerator(Generator.Type.FineTune, 0);
        set
        {
            var coarse = value / 100;
            var fine = value % 100;
            SetGenerator(Generator.Type.CoarseTune, coarse);
            SetGenerator(Generator.Type.FineTune, fine);
        }
    }

    /// <summary>
    /// The current SamplesMode. No Loop (0) is the default.
    /// </summary>
    public Generator.LoopMode LoopMode
    {
        get => (Generator.LoopMode)(GetGenerator(
            Generator.Type.SampleModes) ?? 0);
        set => SetGenerator(
            Generator.Type.SampleModes,
            value == Generator.LoopMode.NoLoop ? null : (int)value);
    }
    
    /// <summary>Sets a generator to a given value if preset, otherwise adds a new one.</summary>
    /// <param name="type">The generator type.</param>
    /// <param name="value">The value to set. Set to null to remove this generator (set as "unset").</param>
    /// <param name="validate">If the value should be clamped to allowed limits.</param>
    /// <exception cref="ArgumentException">Type must be set using a different method</exception>
    public void SetGenerator(
        Generator.Type type, int? value = null, bool validate = true)
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (type) 
        {
            case Generator.Type.SampleID:
                throw new ArgumentException("Use SetSample()");
            case Generator.Type.Instrument:
                throw new ArgumentException("Use SetInstrument()");
            case Generator.Type.VelRange:
            case Generator.Type.KeyRange:
                throw new ArgumentException("Set the range manually");
            default: break;
        }

        if (value is not {} val) 
        {
            for (var i = Generators.Count - 1; i >= 0; i--)
            {
                var g = Generators[i];
                if (g.GType == type) Generators.RemoveAt(i);
            }

            return;
        }

        foreach (ref var g in CollectionsMarshal.AsSpan(Generators))
        {
            if (g.GType != type) continue;
            g = new Generator(type, val, validate);
            return;
        }

        Add(new Generator(type, val, validate));
    }
    
    /// <summary> Gets a generator value. </summary>
    /// <param name="type">The generator type.</param>
    /// <param name="def">If the generator is not found, this value is returned. A default value can be passed here, or null for example, to check if the generator is set.</param>
    /// <returns></returns>
    public short GetGenerator(Generator.Type type, short def) =>
        GetGenerator(type) ?? def;

    public short? GetGenerator(Generator.Type type)
    {
        foreach (ref readonly var g in 
                 CollectionsMarshal.AsSpan(Generators))
            if (g.GType == type) return g.Value;
        return null;
    }
    
    /// <summary> Adds generators to the zone. </summary>
    /// <param name="generators">Generators</param>
    public void Add(params ReadOnlySpan<Generator> generators) 
    {
        foreach (var g in generators) 
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (g.GType) 
            {
                default:
                    Generators.Add(g);
                    break;
                
                case Generator.Type.SampleID:
                case Generator.Type.Instrument:
                    // Don't add these, they already have their own properties
                    break;

                case Generator.Type.VelRange:
                    VelRange.Min = g.Value & 0x7f;
                    VelRange.Max = (g.Value >> 8) & 0x7f;
                    break;

                case Generator.Type.KeyRange:
                    KeyRange.Min = g.Value & 0x7f;
                    KeyRange.Max = (g.Value >> 8) & 0x7f;
                    break;
            }
        }
    }

    public void CopyFrom(BasicZone zone)
    {
        Generators.Clear();
        Modulators.Clear();

        Generators.AddRange(zone.Generators);
        Modulators.AddRange(zone.Modulators);
        
        VelRange = zone.VelRange;
        KeyRange = zone.KeyRange;
    }
    
    /// <summary>Filters the generators and prepends the range generators.</summary>
    /// <returns></returns>
    public List<Generator> GetWriteGenerators() 
    {
        var generators = Generators.FindAll(
            g =>
                g.GType != Generator.Type.SampleID      &&
                g.GType != Generator.Type.Instrument    &&
                g.GType != Generator.Type.KeyRange      &&
                g.GType != Generator.Type.VelRange);

        // Unshift vel then key (to make key first)
        if (HasVelRange) 
        {
            generators.Insert(
                0, new Generator(
                    Generator.Type.VelRange,
                    (VelRange.Max << 8) | Math.Max(VelRange.Min, 0),
                    false));
        }
        if (HasKeyRange) 
        {
            generators.Insert(
                0, new Generator(
                    Generator.Type.KeyRange,
                    (KeyRange.Max << 8) | Math.Max(KeyRange.Min, 0),
                    false));
        }

        return generators;
    }

    /// <summary>Adds to a given generator, or its default value.</summary>
    /// <param name="type">The generator type.</param>
    /// <param name="value">The value to add.</param>
    /// <param name="validate">If the value should be clamped to allowed limits.</param>
    public void AddToGenerator(
        Generator.Type type, int value, bool validate = true)
    {
        var genValue = GetGenerator(type) ?? Generator.Limits[type].Def;
        SetGenerator(type, value + genValue, validate);
    }
}