using System.Collections.Frozen;
using System.Diagnostics;
using SpessaSharp.Utils;
using static SpessaSharp.SoundBank.DLS.ConnectionSource.DLSDestination;

namespace SpessaSharp.SoundBank.DLS;

/// <summary>Represents a single DLS articulator (connection block) </summary>
/// <param name="Source">Like SF2 modulator source.</param>
/// <param name="Control">Like SF2 modulator secondary source.</param>
/// <param name="Destination">Like SF2 Destination</param>
/// <param name="Scale">Like SF2 amount, but long (32-bit) instead of short.</param>
/// <param name="Transform">Like SF2 source transforms</param>
internal readonly record struct ConnectionBlock(
    ConnectionSource Source,
    ConnectionSource Control,
    ConnectionSource.DLSDestination Destination,
    int Scale,
    ModulatorCurve.Type Transform)
{
    private static readonly FrozenSet<Generator.Type> InvalidGenTypes = [
        Generator.Type.SampleModes,                 // Set in wave sample
        Generator.Type.InitialAttenuation,          // Set in wave sample
        Generator.Type.KeyRange,                    // Set in region header
        Generator.Type.VelRange,                    // Set in region header

        Generator.Type.SampleID,                    // Set in wave link
        Generator.Type.FineTune,                    // Set in wave sample
        Generator.Type.CoarseTune,                  // Set in wave sample

        Generator.Type.StartAddrsOffset,            // Does not exist in DLS
        Generator.Type.StartAddrsCoarseOffset,      // Does not exist in DLS
        Generator.Type.EndAddrsOffset,              // Does not exist in DLS

        Generator.Type.EndAddrsCoarseOffset,        // Set in wave sample
        Generator.Type.StartLoopAddrsOffset,        // Set in wave sample
        Generator.Type.StartLoopAddrsCoarseOffset,  // Set in wave sample
        Generator.Type.EndLoopAddrsOffset,          // Set in wave sample
        Generator.Type.EndLoopAddrsCoarseOffset,    // Set in wave sample
        Generator.Type.OverridingRootKey,           // Set in wave sample

        Generator.Type.ExclusiveClass,              // Set in region header
    ];

    public ConnectionBlock(
        ConnectionSource.DLSDestination destination,
        ModulatorCurve.Type transform,
        int scale) : this(
            new ConnectionSource(),
            new ConnectionSource(), 
            destination, scale, transform) {}

    public bool IsStaticParameter =>
        Source.Source   == ConnectionSource.DLSSource.None &&
        Control.Source  == ConnectionSource.DLSSource.None;

    public short ShortScale => (short)(Scale >> 16);

    public static ConnectionBlock Read(ref ArraySegment<byte> artData)
    {
        var usSource = Util.ReadLittleEndian(ref artData, 2);
        var usControl = Util.ReadLittleEndian(ref artData, 2);
        var usDestination = new ConnectionSource.DLSDestination(
            Util.ReadLittleEndian(ref artData, 2));
        var usTransform = Util.ReadLittleEndian(ref artData, 2);
        var lScale = Util.ReadLittleEndian(ref artData, 4);
        
        /*
        2.10 <art2-ck>, Level 2 Articulator Chunk
        usTransform
        Bits 0-3 specify one of 16 possible output transforms. Bits 4-7 specify one of 16 possible transforms to apply to
        the usControl input. Bits 8 and 9 specify whether the usControl input should be inverted and/or bipolar. Bits 10-13
        specify one of 16 possible transforms to apply to the usSource input. Bit 14 and 15 specify whether the usSource
        input should be inverted and/or bipolar.
        */
        
        // Decode usTransform
        var transform = (ModulatorCurve.Type)(usTransform & 0x0f);
        var dlsSource = new ConnectionSource.DLSSource(usSource);
        
        // Decode usControl
        var controlTransform = 
            (ModulatorCurve.Type)((usTransform >> 4) & 0x0f);
        var controlBipolar = (usTransform & (1 << 8)) != 0;
        var controlInvert = (usTransform & (1 << 9)) != 0;
        var control = new ConnectionSource(
            new ConnectionSource.DLSSource(usControl),
            controlTransform,
            controlBipolar,
            controlInvert);

        // Decode usSource
        var sourceTransform = 
            (ModulatorCurve.Type)((usTransform >> 10) & 0x0f);
        var sourceBipolar = (usTransform & (1 << 14)) != 0;
        var sourceInvert = (usTransform & (1 << 15)) != 0;

        var source = new ConnectionSource(
            dlsSource,
            sourceTransform,
            sourceBipolar,
            sourceInvert);

        return new ConnectionBlock(
            source, control, usDestination, lScale, transform);
    }

    public static void FromSFModulator(
        Modulator modulator, DLSArticulation articulation)
    {
        if (modulator.TType != Modulator.TransformType.Linear)
        {
            Failed("Absolute transform type is not supported");
            return;
        }
        
        // Do not write the default DLS effect modulators
        if (Modulator.IsIdentical(modulator,
                DownloadableSounds.DefaultModulators
                    .DEFAULT_DLS_CHORUS, true) ||
            Modulator.IsIdentical(modulator,
                DownloadableSounds.DefaultModulators
                    .DEFAULT_DLS_REVERB, true))
            return;

        if (ConnectionSource
            .FromSFSource(modulator.PrimarySource) is not { } source)
        {
            Failed("Invalid primary source");
            return;
        }

        if (ConnectionSource
            .FromSFSource(modulator.SecondarySource) is not { } control)
        {
            Failed("Invalid secondary source");
            return;
        }
        
        if (FromSFDestination(
            modulator.Destination,
            modulator.TransformAmount) is not {} dlsDestination)
        {
            Failed("Invalid destination");
            return;
        }

        var amount = (int)modulator.TransformAmount;
        var destination = None;

        if (dlsDestination is ConnectionSource.DLSDestination dest)
            destination = dest;
        else
        {
            var destObj = (Dest)dlsDestination;
            destination = destObj.Destination;
            amount = destObj.Amount;
            /*
             Check for a special case, for example mod wheel to vibLfoToPitch
             comprises vibLFO source, mod wheel control and pitch destination.
            */

            if (destObj.Source != ConnectionSource.DLSSource.None)
            {
                if (control.Source  != ConnectionSource.DLSSource.None &&
                    source.Source   != ConnectionSource.DLSSource.None)
                {
                    Failed("Articulation generators with secondary source are not supported");
                    return;
                }
                
                // Move the source to control if needed
                if (source.Source != ConnectionSource.DLSSource.None)
                    control = source;

                source = new ConnectionSource(
                    destObj.Source,
                    ModulatorCurve.Type.Linear,
                    destObj.IsBipolar,
                    false);
            }
        }

        var block = new ConnectionBlock(
            source, control, destination, amount << 16, 0);
        
        articulation.Add(block);
        return;

        void Failed(string msg) => Debug.WriteLine(
            $"Failed converting SF modulator into DLS:\n{modulator}\n({msg})");
    }

    public static void FromSFGenerator(
        Generator generator, DLSArticulation articulation)
    {
        if (InvalidGenTypes.Contains(generator.GType))
            return;

        var dlsDestination = FromSFDestination(
            generator.GType, generator.Value);

        if (dlsDestination == null)
        {
            Failed("Invalid type");
            return;
        }

        var source = new ConnectionSource();
        var destination = None;
        var amount = (int)generator.Value;
        
        // Envelope generators are limited to 40 seconds,
        // However the keyToEnv correction makes us use the full SF range.
        
        if (dlsDestination is ConnectionSource.DLSDestination dest)
            destination = dest;
        else
        {
            var destObj = (Dest)dlsDestination;
            destination = destObj.Destination;
            amount = destObj.Amount;

            source = source with
            {
                Source = destObj.Source,
                Bipolar = destObj.IsBipolar,
            };
        }

        articulation.Add(new ConnectionBlock(
            source, 
            new ConnectionSource(), 
            destination,
            amount << 16,
            0));

        return;

        void Failed(string msg) => Debug.WriteLine(
            $"Failed converting SF2 generator into DLS:\n {generator}\n({msg})");
    }

    private readonly record struct Dest(
        ConnectionSource.DLSSource Source,
        ConnectionSource.DLSDestination Destination,
        bool IsBipolar,
        int Amount);

    private static object?/*DLSDestination|Dest*/ FromSFDestination(
        Generator.Type dest, int amount)
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        return dest switch
        {
            Generator.Type.InitialAttenuation =>
                // The amount does not get EMU corrected here, as this only applies to modulator attenuation
                // The generator (affected) attenuation is handled in wsmp.
                new Dest(
                    ConnectionSource.DLSSource.None, 
                    Gain, 
                    false, 
                    -amount),
            Generator.Type.FineTune => Pitch,
            Generator.Type.Pan => Pan,
            Generator.Type.KeyNum => KeyNum,
            Generator.Type.ReverbEffectsSend => ReverbSend,
            Generator.Type.ChorusEffectsSend => ChorusSend,
            Generator.Type.FreqModLFO => ModLFOFreq,
            Generator.Type.DelayModLFO => ModLFODelay,
            Generator.Type.DelayVibLFO => VibLFODelay,
            Generator.Type.FreqVibLFO => VibLFOFreq,
            Generator.Type.DelayVolEnv => VolEnvDelay,
            Generator.Type.AttackVolEnv => VolEnvAttack,
            Generator.Type.HoldVolEnv => VolEnvHold,
            Generator.Type.DecayVolEnv => VolEnvDecay,
            Generator.Type.SustainVolEnv => 
                new Dest(
                    ConnectionSource.DLSSource.None, 
                    VolEnvSustain, 
                    false,
                1_000 - amount),
            Generator.Type.ReleaseVolEnv => VolEnvRelease,
            Generator.Type.DelayModEnv => ModEnvDelay,
            Generator.Type.AttackModEnv => ModEnvAttack,
            Generator.Type.HoldModEnv => ModEnvHold,
            Generator.Type.DecayModEnv => ModEnvDecay,
            Generator.Type.SustainModEnv => 
                new Dest(
                    ConnectionSource.DLSSource.None, 
                    ModEnvSustain, 
                    false,
                1_000 - amount),
            Generator.Type.ReleaseModEnv => ModEnvRelease,
            Generator.Type.InitialFilterFc => FilterCutOff,
            Generator.Type.InitialFilterQ => FilterQ,
            // Mod env
            Generator.Type.ModEnvToFilterFc => 
                new Dest(
                    ConnectionSource.DLSSource.ModEnv, 
                    FilterCutOff, 
                    false, 
                    amount),
            Generator.Type.ModEnvToPitch => 
                new Dest(
                    ConnectionSource.DLSSource.ModEnv, 
                    Pitch, 
                    false, 
                    amount),
            // Mod lfo
            Generator.Type.ModLFOToFilterFc => 
                new Dest(
                    ConnectionSource.DLSSource.ModLFO, 
                    FilterCutOff, 
                    true, 
                    amount),
            Generator.Type.ModLFOToVolume => 
                new Dest(
                    ConnectionSource.DLSSource.ModLFO, 
                    Gain, 
                    true, 
                    amount),
            Generator.Type.ModLFOToPitch => 
                new Dest(
                    ConnectionSource.DLSSource.ModLFO, 
                    Pitch, 
                    true, 
                    amount),
            // Vib lfo
            Generator.Type.VibLFOToPitch => 
                new Dest(
                    ConnectionSource.DLSSource.VibratoLFO, 
                    Pitch, 
                    true, 
                    amount),
            // Key to something
            Generator.Type.KeyNumToVolEnvHold => 
                new Dest(
                    ConnectionSource.DLSSource.KeyNum, 
                    VolEnvHold, 
                    true, 
                    amount),
            Generator.Type.KeyNumToVolEnvDecay => 
                new Dest(
                    ConnectionSource.DLSSource.KeyNum, 
                    VolEnvDecay, 
                    true, 
                    amount),
            Generator.Type.KeyNumToModEnvHold => 
                new Dest(
                    ConnectionSource.DLSSource.KeyNum, 
                    ModEnvHold, 
                    true, 
                    amount),
            Generator.Type.KeyNumToModEnvDecay => 
                new Dest(
                    ConnectionSource.DLSSource.KeyNum, 
                    ModEnvDecay, 
                    true, 
                    amount),
            Generator.Type.ScaleTuning =>
                // Scale tuning is implemented in DLS via an articulator:
                // KeyNum to relative pitch at 12,800 cents.
                // Change that to scale tuning * 128.
                // Therefore, a regular scale is still 12,800, half is 6400, etc.
                new Dest(
                    ConnectionSource.DLSSource.KeyNum, 
                    Pitch, 
                    false, // According to table 4, this should be false.
                    amount * 128),
            _ => null
        };
    }

    public override string ToString() =>
        $"Source: {Source}\n" +
        $"Control: {Control}\n" +
        $"Scale: {Scale} >> 16 = {ShortScale}\n" +
        $"Destination: {Destination}";

    public ArraySegment<byte> Write()
    {
        var output = new byte[12];
        var seg = (ArraySegment<byte>)output;
        
        Util.WriteWord(ref seg, (short)Source.Source.Value);
        Util.WriteWord(ref seg, (short)Control.Source.Value);
        Util.WriteWord(ref seg, (short)Destination.Value);
        var transformEnum =
            (int)Transform |
            (Control.TransformFlag  << 4) |
            (Source.TransformFlag   << 10);
        
        Util.WriteWord(ref seg, (short)transformEnum);
        Util.WriteDword(ref seg, Scale);

        return output;
    }

    public void ToSFGenerator(BasicZone zone)
    {
        var d = Destination;
        // SF2 uses 16-bit amounts, DLS uses 32-bit scale.
        var value = ShortScale;

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (d)
        {
            case var _ when d == Pan:
                zone.SetGenerator(Generator.Type.Pan, value);
                break;
            case var _ when d == Gain:
                // Turn to centibels and apply emu correction
                zone.AddToGenerator(
                    Generator.Type.InitialAttenuation,
                    Util.Round(-value / .4));
                break;
            case var _ when d == FilterCutOff:
                zone.SetGenerator(Generator.Type.InitialFilterFc, value);
                break;
            case var _ when d == FilterQ:
                zone.SetGenerator(Generator.Type.InitialFilterQ, value);
                break;
            
            // Mod LFO raw values it seems
            case var _ when d == ModLFOFreq:
                zone.SetGenerator(Generator.Type.FreqModLFO, value);
                break;
            case var _ when d == ModLFODelay:
                zone.SetGenerator(Generator.Type.DelayModLFO, value);
                break;
            case var _ when d == VibLFOFreq:
                zone.SetGenerator(Generator.Type.FreqVibLFO, value);
                break;
            case var _ when d == VibLFODelay:
                zone.SetGenerator(Generator.Type.DelayVibLFO, value);
                break;
            
            // Vol. env: all times are timecents like sf2
            case var _ when d == VolEnvDelay:
                zone.SetGenerator(Generator.Type.DelayVolEnv, value);
                break;
            case var _ when d == VolEnvAttack:
                zone.SetGenerator(Generator.Type.AttackVolEnv, value);
                break;
            case var _ when d == VolEnvHold:
                zone.SetGenerator(Generator.Type.HoldVolEnv, value);
                break;
            case var _ when d == VolEnvDecay:
                zone.SetGenerator(Generator.Type.DecayVolEnv, value);
                break;
            case var _ when d == VolEnvRelease:
                zone.SetGenerator(Generator.Type.ReleaseVolEnv, value);
                break;
            case var _ when d == VolEnvSustain:
                // Gain seems to be (1000 - value) = sustain cB
                zone.SetGenerator(Generator.Type.SustainVolEnv, 1_000 - value);
                break;
            // Mod env
            case var _ when d == ModEnvDelay:
                zone.SetGenerator(Generator.Type.DelayModEnv, value);
                break;
            case var _ when d == ModEnvAttack:
                zone.SetGenerator(Generator.Type.AttackModEnv, value);
                break;
            case var _ when d == ModEnvHold:
                zone.SetGenerator(Generator.Type.HoldModEnv, value);
                break;
            case var _ when d == ModEnvDecay:
                zone.SetGenerator(Generator.Type.DecayModEnv, value);
                break;
            case var _ when d == ModEnvRelease:
                zone.SetGenerator(Generator.Type.ReleaseModEnv, value);
                break;
            case var _ when d == ModEnvSustain:
                // DLS uses 0.1%, SF uses 0.1%
                zone.SetGenerator(Generator.Type.SustainModEnv, 1_000 - value);
                break;
            
            case var _ when d == ReverbSend:
                zone.SetGenerator(Generator.Type.ReverbEffectsSend, value);
                break;
            case var _ when d == ChorusSend:
                zone.SetGenerator(Generator.Type.ChorusEffectsSend, value);
                break;
            
            case var _ when d == Pitch:
                zone.FineTuning += value;
                break;
            
            default:
                Debug.WriteLine(
                    $"[WARN] Failed converting DLS articulator into SF generator: {ToString()}\n(invalid destination)");
                return;
        }
    }

    public void ToSFModulator(BasicZone zone)
    {
        // Output modulator variables
        var amount = (int)ShortScale;
        var modulatorDestination = Generator.Type.Invalid;
        Modulator.Source primarySource;
        var secondarySource = new Modulator.Source();

        if (ToCombinedSFDestination() is { } specialDest)
        {
            /*
             Special destination detected.
             This means modLfoToPitch for example, as an SF modulator like

             CC#1 -> | x50 | -> modLfoToPitch

             In DLS is:

             Mod LFO -> | x50 | -> pitch
             CC#1    -> |     |
            */
            modulatorDestination = specialDest;
            if (Control.ToSFSource() is { } controlSF)
                primarySource = controlSF;
            else
            {
                FailedConversion("Invalid control");
                return;
            }
        }
        else
        {
            // Convert destination
            if (ToSFDestination() is not { } convertedDest)
            {
                // Cannot be a valid modulator
                FailedConversion("Invalid destination");
                return;
            }
            
            // The conversion may specify an adjusted value
            if (convertedDest.newAmount is { } newAmount)
                amount = newAmount;
            modulatorDestination = convertedDest.Gen;

            if (Source.ToSFSource() is not { } convertedPrim)
            {
                FailedConversion("Invalid source");
                return;
            }
            
            primarySource = convertedPrim;
            
            if (Control.ToSFSource() is not { } convertedSec)
            {
                FailedConversion("Invalid control");
                return;
            }

            secondarySource = convertedSec;
        }

        // Output transform is ignored as it's not a thing in soundfont format
        // Unless the curve type of source is linear, then output is copied.
        // Testcase: Fury.dls (sets concave output transform for the key to attenuation)
        if (Transform != ModulatorCurve.Type.Linear &&
            primarySource.CurveType == ModulatorCurve.Type.Linear)
            primarySource = primarySource with { CurveType = Transform };

        if (modulatorDestination == Generator.Type.InitialAttenuation)
        {
            if (Source.Source == ConnectionSource.DLSSource.Velocity ||
                Source.Source == ConnectionSource.DLSSource.Volume   ||
                Source.Source == ConnectionSource.DLSSource.Expression)
            {
                /*
                Some DLS banks (example: Fury.dls or 1 - House.rmi) only specify the output transform,
                while completely omitting the invert flag for this articulator.
                This results in the modulator rendering the voice inaudible, as the attenuation increases with velocity,
                which also conflicts with the default velToAtt modulator
                Yet most software seems to load them fine, so we invert it here.
                 */
                primarySource = primarySource with { IsNegative = true };
            }
            
            // A corrupted rendition of gm.dls was found under
            // https://sembiance.com/fileFormatSamples/audio/downloadableSoundBank/
            // Name: (GM.dls)
            // Which specifies a whopping 32,768 centibels of attenuation
            amount = Math.Clamp(amount, 0, 960);
        }
        
        // Get the modulator!
        var mod = new Modulator(
            primarySource,
            secondarySource,
            modulatorDestination,
            (short)amount);
        
        zone.Modulators.Add(mod);
    }

    /// <summary>Checks for an SF generator that consists of DLS source and destination (such as mod LFO and pitch)</summary>
    /// <returns>Either a matching SF generator or nothing.</returns>
    public Generator.Type? ToCombinedSFDestination()
    {
        var source = Source.Source;
        var destination = Destination;

        ReadOnlySpan<(
            ConnectionSource.DLSSource, 
            ConnectionSource.DLSDestination, 
            Generator.Type)> tests =
        [
            // Vibrato lfo to pitch
            (ConnectionSource.DLSSource.VibratoLFO, 
                Pitch, 
                Generator.Type.VibLFOToPitch),
            // Mod lfo to pitch
            (ConnectionSource.DLSSource.ModLFO,
                Pitch,
                Generator.Type.ModLFOToPitch),
            // Mod lfo to filter
            (ConnectionSource.DLSSource.ModLFO,
                FilterCutOff,
                Generator.Type.ModLFOToFilterFc),
            // Mod lfo to volume
            (ConnectionSource.DLSSource.ModLFO,
                Gain,
                Generator.Type.ModLFOToVolume),
            // Mod envelope to filter
            (ConnectionSource.DLSSource.ModEnv,
                FilterCutOff,
                Generator.Type.ModEnvToFilterFc),
            // Mod envelope to pitch
            (ConnectionSource.DLSSource.ModEnv,
                Pitch,
                Generator.Type.ModEnvToPitch),
        ];
        
        foreach (var (src, dst, type) in tests)
            if (src == source && dst == destination) return type;

        return null;
    }

    /// <summary> Converts DLS destination of this block to an SF2 one, also with the correct amount.</summary>
    /// <returns></returns>
    private (Generator.Type Gen, int? newAmount)? ToSFDestination()
    {
        var amount = ShortScale;
        var d = Destination;
        return d switch
        {
            _ when d == Pan => Ret1(Generator.Type.Pan),
            _ when d == Gain =>
                // DLS uses gain, SF uses attenuation
                Ret2(Generator.Type.InitialAttenuation, -amount),
            _ when d == Pitch => Ret1(Generator.Type.FineTune),
            _ when d == KeyNum => Ret1(Generator.Type.OverridingRootKey),
            // Vol env
            _ when d == VolEnvDelay => Ret1(Generator.Type.DelayVolEnv),
            _ when d == VolEnvAttack => Ret1(Generator.Type.AttackVolEnv),
            _ when d == VolEnvHold => Ret1(Generator.Type.HoldVolEnv),
            _ when d == VolEnvDecay => Ret1(Generator.Type.DecayVolEnv),
            _ when d == VolEnvSustain => 
                Ret2(Generator.Type.SustainVolEnv, 1_000 - amount),
            _ when d == VolEnvRelease => Ret1(Generator.Type.ReleaseVolEnv),
            
            // Mod env
            _ when d == ModEnvDelay => Ret1(Generator.Type.DelayModEnv),
            _ when d == ModEnvAttack => Ret1(Generator.Type.AttackModEnv),
            _ when d == ModEnvHold => Ret1(Generator.Type.HoldModEnv),
            _ when d == ModEnvDecay =>  Ret1(Generator.Type.DecayModEnv),
            _ when d == ModEnvSustain =>
                Ret2(Generator.Type.SustainModEnv, 1_000 - amount),
            _ when d == ModEnvRelease => Ret1(Generator.Type.ReleaseModEnv),
            _ when d == FilterCutOff => Ret1(Generator.Type.InitialFilterFc),
            _ when d == FilterQ => Ret1(Generator.Type.InitialFilterQ),
            _ when d == ChorusSend => Ret1(Generator.Type.ChorusEffectsSend),
            _ when d == ReverbSend =>  Ret1(Generator.Type.ReverbEffectsSend),
            
            // Lfo
            _ when d == ModLFOFreq => Ret1(Generator.Type.FreqModLFO),
            _ when d == ModLFODelay => Ret1(Generator.Type.DelayModLFO),
            _ when d == VibLFOFreq => Ret1(Generator.Type.FreqVibLFO),
            _ when d == VibLFODelay => Ret1(Generator.Type.DelayVibLFO),
            
            _ when d == None => null,
            _ => null,
        };
        
        (Generator.Type Gen, int? newAmount) Ret1(Generator.Type gen) =>
            (gen, null);
        (Generator.Type Gen, int? newAmount) Ret2(Generator.Type gen, int a) =>
            (gen, a);
    }

    private void FailedConversion(string msg) =>
        Debug.WriteLine(
            $"[WARN]Failed converting DLS articulator into SF2:\n {
                this}\n({msg})");
}