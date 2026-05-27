using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank.SoundFont;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

namespace SpessaSharp.SoundBank;

/// <summary>
/// Parses soundfont modulators and the source enums, also includes the default modulators list
/// </summary>
/// <param name="PrimarySource">The primary source of this modulator.</param>
/// <param name="SecondarySource"></param>
/// <param name="Destination">The generator destination of this modulator.</param>
/// <param name="TransformAmount">The transform amount for this modulator.</param>
/// <param name="TType">The transform type for this modulator.</param>
public readonly record struct Modulator(
    Modulator.Source PrimarySource,
    Modulator.Source SecondarySource,
    Generator.Type Destination      = Generator.Type.Invalid,
    short TransformAmount           = 0,
    Modulator.TransformType TType   = Modulator.TransformType.Linear)
{
    public const int ByteSize = 10;

    public enum TransformType { Linear, Absolute }

    private static readonly int[] _tTids = [0, 2];
    
    public static int ID(TransformType t) => _tTids[(int)t];

    /// <summary>
    /// </summary>
    /// <param name="IsBipolar">
    /// If this field is set to false, the controller should be mapped with a minimum value of 0 and a maximum value of 1. This is also
    /// called Unipolar. Thus, it behaves similar to the Modulation Wheel controller of the MIDI specification.
    /// If this field is set to true, the controller sound be mapped with a minimum value of -1 and a maximum value of 1. This is also
    /// called Bipolar. Thus, it behaves similar to the Pitch Wheel controller of the MIDI specification.</param>
    /// <param name="IsNegative">
    /// If this field is set true, the direction of the controller should be from the maximum value to the minimum value. So, for
    /// example, if the controller source is Key Number, then a Key Number value of 0 corresponds to the maximum possible
    /// controller output, and the Key Number value of 127 corresponds to the minimum possible controller input.</param>
    /// <param name="SIndex">
    /// The index of the source.
    /// It can point to one of the MIDI controllers or one of the predefined sources, depending on the 'isCC' flag.</param>
    /// <param name="IsCC">
    /// If this field is set to true, the MIDI Controller Palette is selected. The ‘index’ field value corresponds to one of the 128
    /// MIDI Continuous Controller messages as defined in the MIDI specification.</param>
    /// <param name="CurveType">This field specifies how the minimum value approaches the maximum value.</param>
    public readonly record struct Source(
        bool IsBipolar,
        bool IsNegative,
        Source.Index SIndex,
        bool IsCC,
        ModulatorCurve.Type CurveType)
    {
        public enum ControllerSource
        {
            NoController, NoteOnVelocity, NoteOnKeyNum, PolyPressure,
            ChannelPressure, PitchWheel, PitchWheelRange, 
            
            // CT8MGM.SF2
            AuxiliaryEnvelope, LF1, LF2,
            
            Link,
        }

        private static readonly int[] _ids = [0, 2, 3, 10, 13, 14, 16, 105, 106, 112, 127];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ID(ControllerSource e) => _ids[(int)e];

        public static ControllerSource SourceOf(int i)
        {
            var idx = _ids.IndexOf(i);
            ArgumentOutOfRangeException.ThrowIfNegative(
                idx, "Unknown Modulator.Source.Enum value");
            return (ControllerSource)idx;
        }

        public readonly struct Index(byte v) : IEquatable<Index>
        {
            private readonly byte _v = v;

            public static Index Of(bool isCC, int id)
            {
                if (isCC && id is < 0 or > 127)
                    throw new ArgumentException($"Invalid MIDIController: {id}");
                if (!isCC && !_ids.Contains(id))
                    throw new ArgumentException($"Invalid ModulatorEnum: {id}");
                return new Index((byte)id);
            }
            
            public int AsInt => _v;
            public ControllerSource AsControllerSource => SourceOf(_v);
            public Midi.CC AsMidiController => (Midi.CC)_v;
            
            public Index(ControllerSource e) : this((byte)_ids[(int)e]) {}

            public Index(Midi.CC c) : this((byte)c) {}
            
            public string Name(bool isCC) => isCC
                ? AsMidiController.ToString() : AsControllerSource.ToString();

            public bool Equals(Index other) => _v == other._v;
            public override bool Equals(object? obj) => 
                obj is Index other && Equals(other);
            public override int GetHashCode() => _v.GetHashCode();
            public static bool operator ==(Index left, Index right) =>
                left.Equals(right);
            public static bool operator !=(Index left, Index right) => 
                !left.Equals(right);
        }
        
        public string Name => SIndex.Name(IsCC);

        public static Source From(short sourceEnum)
        {
            var isBipolar = (sourceEnum & (1 << 9)) != 0;
            var isNegative = (sourceEnum & (1 << 8)) != 0;
            var isCC = (sourceEnum & (1 << 7)) != 0;
            var index = Index.Of(isCC, sourceEnum & 127);
            var curveType = (ModulatorCurve.Type)((sourceEnum >> 10) & 0x3);
            return new Source(isBipolar, isNegative, index, isCC, curveType);
        }

        public override string ToString() =>
            $"{Name} {CurveType} {
                (IsBipolar ? "Bipolar" : "Unipolar")} {
                    (IsNegative ? "Negative" : "Positive")}";

        public short ToSourceEnum() => (short)(
            ((int)CurveType         << 10)  |
            ((IsBipolar  ? 1 : 0)   << 9)   |
            ((IsNegative ? 1 : 0)   << 8)   |
            ((IsCC       ? 1 : 0)   << 7)   |
            SIndex.AsInt);

        public bool Is(Midi.CC cc) =>
            IsCC && SIndex.AsMidiController == cc;
        
        /// <summary>
        /// Gets the current value from this source.
        /// </summary>
        /// <param name="channel">The MIDI channel to compute for.</param>
        /// <param name="pitchWheel">The pitch wheel value, as channel determines if it's a per-note or a global value.</param>
        /// <param name="voice">The voice to get the data for.</param>
        /// <returns></returns>
        public float GetValue(
            ISf2Channel channel, short pitchWheel, Voice voice) 
        {
            // The raw 14-bit value (0 - 16,383)
            short rawValue;
            if (IsCC)
                rawValue = channel.GetMidiControllers[SIndex.AsInt];
            else
            {
                if (SIndex.AsInt == ID(ControllerSource.NoteOnVelocity))
                    rawValue = (short)(voice.Velocity << 7);
                else if (SIndex.AsInt == ID(ControllerSource.NoteOnKeyNum))
                    rawValue = (short)(voice.TargetKey << 7);
                else if (SIndex.AsInt == ID(ControllerSource.PolyPressure))
                    rawValue = (short)(voice.Pressure << 7);
                else if (SIndex.AsInt == ID(ControllerSource.ChannelPressure))
                    rawValue = (short)(channel.GetMidiParameters.Pressure << 7);
                else if (SIndex.AsInt == ID(ControllerSource.PitchWheel))
                    rawValue = pitchWheel;
                else if (SIndex.AsInt == ID(ControllerSource.PitchWheelRange))
                    // Pitch wheel range may be a floating point number!
                    // Therefore, something like "0.5 << 7" won't work,
                    // So we multiply it by 128 which is essentially the same here
                    // But it allows for fractional pitch wheel range!
                    rawValue = (short)float.Floor(channel.GetMidiParameters.PitchWheelRange * 128);
                // default:
                // if (SIndex.AsInt == ID(Enum.NoController))
                else
                    rawValue = 16_383;  // Equals to 1
            }

            // Transform the value
            // 2-bit number as in 0bPD
            var transformType =
                (IsBipolar  ? 0b10  : 0b00) |
                (IsNegative ? 1     : 0);

            return MODULATOR_TRANSFORMS[
                ModulatorCurve.MODULATOR_RESOLUTION *
                ((int)CurveType * ModulatorCurve.MOD_CURVE_TYPES_AMOUNT + 
                 transformType) +
                rawValue];
        }
    }

    /// <summary>
    /// To get the value, you do MODULATOR_RESOLUTION * (MOD_CURVE_TYPES_AMOUNT * curveType + transformType) + your raw value as 14-bit number (0 - 16,383)
    /// </summary>
    public static readonly float[] MODULATOR_TRANSFORMS = new float[
        ModulatorCurve.MODULATOR_RESOLUTION *
        ModulatorCurve.MOD_SOURCE_TRANSFORM_POSSIBILITIES *
        ModulatorCurve.MOD_CURVE_TYPES_AMOUNT];

    static Modulator()
    {
        for (var curveType = 0; curveType < ModulatorCurve.MOD_CURVE_TYPES_AMOUNT; curveType++) 
        {
            for (
                var transformType = 0;
                transformType < ModulatorCurve.MOD_SOURCE_TRANSFORM_POSSIBILITIES;
                transformType++) 
            {
                var tableIndex =
                    ModulatorCurve.MODULATOR_RESOLUTION *
                    (curveType * ModulatorCurve.MOD_CURVE_TYPES_AMOUNT + transformType);
                for (var value = 0; value < ModulatorCurve.MODULATOR_RESOLUTION; value++)
                {
                    MODULATOR_TRANSFORMS[tableIndex + value] = ModulatorCurve
                        .GetValue(
                        transformType,
                        (ModulatorCurve.Type)curveType,
                        value / (float)ModulatorCurve.MODULATOR_RESOLUTION);
                }
            }
        }
    }
    
    public static short GetModSourceEnum(
        ModulatorCurve.Type curveType,
        bool isBipolar,
        bool isNegative,
        bool isCC,
        Source.Index index) =>
            new Source(isBipolar, isNegative, index, isCC, curveType)
                .ToSourceEnum();

    public static readonly short DefaultResonantModSource = GetModSourceEnum(
        ModulatorCurve.Type.Linear,
        true, 
        false,
        true,
        new Source.Index(Midi.CC.FilterResonance)
    ); // Linear forwards bipolar cc 74
    
    /// <summary>Creates a new SF2 Modulator</summary> 
    public Modulator(
        Source? primarySource = null,
        Source? secondarySource = null,
        Generator.Type destination = Generator.Type.Invalid,
        short amount = 0,
        TransformType transformType = TransformType.Linear): this(
        primarySource ?? new Source(),
        secondarySource ?? new Source(),
        destination,
        amount,
        transformType)
    { }
    
    /// <summary> Checks if the pair of modulators is identical (in SF2 terms) </summary>
    /// <param name="mod1">Modulator 1</param>
    /// <param name="mod2">Modulator 2</param>
    /// <param name="checkAmount">If the amount should be checked too.</param>
    /// <returns>If they are identical</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentical(
            Modulator mod1, Modulator mod2, bool checkAmount = false) =>
        mod1.PrimarySource == mod2.PrimarySource &&
        mod1.SecondarySource == mod2.SecondarySource &&
        mod1.Destination == mod2.Destination &&
        mod1.TType == mod2.TType &&
        (!checkAmount || mod1.TransformAmount == mod2.TransformAmount);

    public override string ToString() =>
        $"Source: {PrimarySource}\nSecondary source: {
            SecondarySource}\nto: {Destination}\namount: {TransformAmount}\n{
            (TType == TransformType.Absolute ? "absolute value" : "")}";

    public void Write(
        ref Span<byte> modData, SoundFontWriteIndexes? indexes = null)
    {
        Util.WriteWord(ref modData, PrimarySource.ToSourceEnum());
        Util.WriteWord(ref modData, (short)Destination);
        Util.WriteWord(ref modData, TransformAmount);
        Util.WriteWord(ref modData, SecondarySource.ToSourceEnum());
        Util.WriteWord(ref modData, (short)TType);

        if (indexes is not null)
            indexes.Mod++;
    }
    
    /// <summary> Sums transform and create a NEW modulator </summary>
    /// <param name="mod">The modulator to sum with</param>
    /// <returns>The new modulator</returns>
    public Modulator SumTransform(Modulator mod) => 
        this with { TransformAmount = 
            (short)(TransformAmount + mod.TransformAmount) };

    /// <summary> Reads an SF2 modulator </summary>
    /// <param name="sourceEnum">SF2 source enum</param>
    /// <param name="secondarySourceEnum">SF2 secondary source enum</param>
    /// <param name="destination">Destination</param>
    /// <param name="amount">Amount</param>
    /// <param name="transformType">Transform type</param>
    /// <returns></returns>
    private static Modulator Decoded(
        short sourceEnum,
        short secondarySourceEnum,
        Generator.Type destination,
        short amount,
        short transformType) => Decoded(
            sourceEnum, 
            secondarySourceEnum, 
            (short)destination, 
            amount, 
            transformType);
    
    public static Modulator Decoded(
        short sourceEnum,
        short secondarySourceEnum,
        short destination,
        short amount,
        short transformType)
    {
        // Flag as invalid (for linked ones)
        if (destination >= (int)Generator.Type.Invalid)
            destination = (int)Generator.Type.Invalid;

        return new Modulator(
            Source.From(sourceEnum),
            Source.From(secondarySourceEnum),
            (Generator.Type)destination,
            amount,
            (TransformType)transformType);
    }

    public static readonly Modulator[] DefaultSoundFont2Modulators =
    [
        // Vel to attenuation
        Decoded(
            GetModSourceEnum(
                ModulatorCurve.Type.Concave,
                false,
                true,
                false,
                new Source.Index(Source.ControllerSource.NoteOnVelocity)
            ),
            0x0,
            Generator.Type.InitialAttenuation,
            960,
            0),
        
        // Mod wheel to vibrato
        Decoded(
            0x00_81, 
            0x0, 
            Generator.Type.VibLFOToPitch,
            50,
            0),
        
        // Vol to attenuation
        Decoded(
            GetModSourceEnum(
                ModulatorCurve.Type.Concave,
                false,
                true,
                true,
                new Source.Index(Midi.CC.MainVolume)
            ),
            0x0,
            Generator.Type.InitialAttenuation,
            960,
            0),
        
        // Channel pressure to vibrato
        Decoded(
            0x00_0d,
            0x0,
            Generator.Type.VibLFOToPitch,
            50,
            0),

        // Pitch wheel to tuning
        Decoded(
            0x02_0e, 
            0x00_10, 
            Generator.Type.FineTune,
            12_700,
            0),

        // Pan to uhh, pan
        // Amount is 500 instead of 1000, see #59
        Decoded(
            0x02_8a, 
            0x0, 
            Generator.Type.Pan,
            500,
            0),
        
        // Expression to attenuation
        Decoded(
            GetModSourceEnum(
                ModulatorCurve.Type.Concave,
                false,
                true,
                true,
                new Source.Index(Midi.CC.Expression)
            ),
            0x0,
            Generator.Type.InitialAttenuation,
            960,
            0),

        // Reverb effects to send
        Decoded(
            0x00_db,
            0x0,
            Generator.Type.ReverbEffectsSend,
            200,
            0),

        // Chorus effects to send
        Decoded(
            0x00_dd, 
            0x0,
            Generator.Type.ChorusEffectsSend, 
            200,
            0)
    ];
    
    public static readonly Modulator[] DefaultSpessaSynthModulators = 
    [
    // Custom modulators heck yeah
    // Cc 73 (attack time) to volEnv attack
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Convex,
            true,
            false,
            true,
            new Source.Index(Midi.CC.AttackTime)
        ), // Linear forward bipolar cc 72
        0x0, // No controller
        Generator.Type.AttackVolEnv,
        6_000,
        0),

    // Cc 72 (release time) to volEnv release
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Linear,
            true,
            false,
            true,
            new Source.Index(Midi.CC.ReleaseTime)
        ), // Linear forward bipolar cc 72
        0x0, // No controller
        Generator.Type.ReleaseVolEnv,
        3_600,
        0),

    // Cc 75 (decay time) to vol env decay
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Linear,
            true,
            false,
            true,
            new Source.Index(Midi.CC.DecayTime)
        ), // Linear forward bipolar cc 75
        0x0, // No controller
        Generator.Type.DecayVolEnv,
        3_600,
        0),

    // Cc 74 (brightness) to filterFc
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Linear,
            true,
            false,
            true,
            new Source.Index(Midi.CC.Brightness)
        ), // Linear forwards bipolar cc 74
        0x0, // No controller
        Generator.Type.InitialFilterFc,
        9_600,
        0),

    // Cc 71 (filter Q) to filter Q (default resonant modulator)
    Decoded(
        DefaultResonantModSource,
        0x0, // No controller
        Generator.Type.InitialFilterQ,
        250,
        0),

    // Cc 67 (soft pedal) to attenuation
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Switch,
            false,
            false,
            true,
            new Source.Index(Midi.CC.SoftPedal)
        ), // Switch unipolar positive 67
        0x0, // No controller
        Generator.Type.InitialAttenuation,
        50,
        0),
    // Cc 67 (soft pedal) to filter fc
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Switch,
            false,
            false,
            true,
            new Source.Index(Midi.CC.SoftPedal)
        ), // Switch unipolar positive 67
        0x0, // No controller
        Generator.Type.InitialFilterFc,
        -2_400,
        0),

    // Cc 8 (balance) to pan
    Decoded(
        GetModSourceEnum(
            ModulatorCurve.Type.Linear,
            true,
            false,
            true,
            new Source.Index(Midi.CC.Balance)
        ), // Linear bipolar positive 8
        0x0, // No controller
        Generator.Type.Pan,
        500,
        0)
    ];

    private static readonly Modulator[] _spessaSynthDefaultMods =
        DefaultSoundFont2Modulators
            .Concat(DefaultSpessaSynthModulators).ToArray();

    public static ReadOnlySpan<Modulator> SPESSASYNTH_DEFAULT_MODULATORS =>
        _spessaSynthDefaultMods;
}