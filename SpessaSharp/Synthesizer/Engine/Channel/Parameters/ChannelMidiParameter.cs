using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel.Parameters;

public static class ChannelMidiParameters
{
    extension(ChannelMidiParameter[] parameters)
    {
        public int RxChannel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => parameters.Set(
                (ChannelMidiParameter.Type.RxChannel, value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(
            ChannelMidiParameter param) => parameters[(int)param.PType] = param;
    }

    extension(ReadOnlySpan<ChannelMidiParameter> parameters)
    {
        public int PitchWheel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.PitchWheel).AsInt;
        }
        
        public float PitchWheelRange
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.PitchWheelRange).AsFloat;
        }
        
        public int Pressure
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.Pressure).AsInt;
        }
        
        public float ModulationDepth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.ModulationDepth).AsFloat;
        }
        
        public int RxChannel
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => parameters.Get(ChannelMidiParameter.Type.RxChannel).AsInt;
        }

        public bool PolyMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.PolyMode).AsBool;
        }
        
        public int KeyShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.KeyShift).AsInt;
        }
                
        public float FineTune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.FineTune).AsFloat;
        }
        
        public MidiChannel.Assign AssignMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.AssignMode).AsAssignMode;
        }
        
        public bool EfxAssign
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.EfxAssign).AsBool;
        }
        
        public bool RandomPan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.RandomPan).AsBool;
        }
        
        public Midi.CC CC1
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.CC1).AsCC;
        }
        
        public Midi.CC CC2
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.CC2).AsCC;
        }
        
        public int DrumMap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.DrumMap).AsInt;
        }
        
        public int VelocitySenseDepth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.VelocitySenseDepth).AsInt;
        }
        
        public int VelocitySenseOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                parameters.Get(ChannelMidiParameter.Type.VelocitySenseOffset).AsInt;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChannelMidiParameter Get(
            ChannelMidiParameter.Type type) => parameters[(int)type];
    }
    
    private static readonly ChannelMidiParameter[] DefaultParams;
    public static ReadOnlySpan<ChannelMidiParameter> Default => DefaultParams;

    static ChannelMidiParameters()
    {
        // Avoid setting the param in the wrong position
        var list = (ReadOnlySpan<ChannelMidiParameter>)[
            (ChannelMidiParameter.Type.PitchWheel, 8_192),
            (ChannelMidiParameter.Type.PitchWheelRange, 2f),
            (ChannelMidiParameter.Type.Pressure, 0),
            (ChannelMidiParameter.Type.ModulationDepth, 1f),
            (ChannelMidiParameter.Type.RxChannel, 0),
            (ChannelMidiParameter.Type.PolyMode, true),
            (ChannelMidiParameter.Type.KeyShift, 0),
            (ChannelMidiParameter.Type.FineTune, 0f),
            (ChannelMidiParameter.Type.RandomPan, false),
            MidiChannel.Assign.FullMulti,
            (ChannelMidiParameter.Type.EfxAssign, false),
            new (ChannelMidiParameter.Type.CC1, (Midi.CC)0x10),
            new (ChannelMidiParameter.Type.CC2, (Midi.CC)0x11),
            (ChannelMidiParameter.Type.DrumMap, 0),
            (ChannelMidiParameter.Type.VelocitySenseDepth, 64),
            (ChannelMidiParameter.Type.VelocitySenseOffset, 64),
        ];
        
        DefaultParams = new ChannelMidiParameter[list.Length];
        foreach (var param in list)
            DefaultParams[(int)param.PType] = param;
    }
}

public readonly record struct ChannelMidiParameter
{
    private readonly Params.Data _data;
    public readonly Type PType;
    
    public static ChannelMidiParameter Of(Type type, float value)
    {
        Assert(type, Params.Type.Float);
        return new ChannelMidiParameter(type, Params.Of(value));
    }
    
    public static ChannelMidiParameter Of(Type type, int value)
    {
        Assert(type, Params.Type.Int);
        return new ChannelMidiParameter(type, Params.Of(value));
    }
    
    public static ChannelMidiParameter Of(Type type, bool value)
    {
        Assert(type, Params.Type.Bool);
        return new ChannelMidiParameter(type, Params.Of(value));
    }
    
    public ChannelMidiParameter(Type type, Midi.CC value)
    {
        Assert(type, Params.Type.CC);
        PType = type;
        _data = Params.Of(value);
    }
    
    public static ChannelMidiParameter Of(MidiChannel.Assign value)
    {
        Assert(Type.AssignMode, Params.Type.AssignMode); //Just in case
        return new ChannelMidiParameter(Type.AssignMode, Params.Of(value));
    }
    
    private ChannelMidiParameter(Type type, Params.Data data)
    {
        PType = type;
        _data = data;
    }
    
    public bool AsBool
    {
        get
        {
            Assert(PType, Params.Type.Bool);
            return _data.AsBool();
        }
    }
    
    public float AsFloat
    {
        get
        {
            Assert(PType, Params.Type.Float);
            return _data.AsFloat();
        }
    }
    
    public int AsInt
    {
        get
        {
            Assert(PType, Params.Type.Int);
            return _data.AsInt();
        }
    }

    public Midi.CC AsCC
    {
        get
        {
            Assert(PType, Params.Type.CC);
            return _data.AsEnum<Midi.CC>();
        }
    }

    public MidiChannel.Assign AsAssignMode
    {
        get
        {
            Assert(PType, Params.Type.AssignMode);
            return _data.AsEnum<MidiChannel.Assign>();
        }
    }
    
    public enum Type
    {
        /// <summary>The current pitch wheel value (0-16,383) of this channel.</summary>
        PitchWheel,
        /// <summary>The current pitch wheel range, in semitones.</summary>
        PitchWheelRange,
        /// <summary>The current pressure (aftertouch) of this channel.</summary>
        Pressure,
        /// <summary>
        /// The multiplier of the modulation wheel modulator.
        /// 
        /// The MIDI specification assumes the default modulation depth is 50 cents,
        /// but it may vary for different sound banks.
        /// For example, if a MIDI requests a modulation depth of 100 cents,
        /// the multiplier will be 2,
        /// which, for a preset with a depth of 50,
        /// will create a total modulation depth of 100 cents.
        /// </summary>
        ModulationDepth,
        /// <summary>
        /// The channel's receiving number (0-based index).
        /// This allows triggering multiple parts (channels) with a single note message.
        /// </summary>
        /// <remarks>Only used when customChannelNumbers is enabled.</remarks>
        RxChannel,
        /// <summary>
        /// If the channel is in the poly mode.<br/>
        /// <b>true</b> - POLY ON - regular playback.<br/>
        /// <b>false</b> - MONO ON - one note per channel, others are killed on NoteOn
        /// </summary>
        PolyMode,
        /// <summary>The key shift of the channel (in semitones). Drum channels ignore this value.</summary>
        KeyShift,
        /// <summary>Cents, RPN/SysEx for fine-tuning. Drum channels ignore this value.</summary>
        FineTune,
        /// <summary>Enables random panning for every note played on this channel.</summary>
        RandomPan,
        /// <summary> Assign mode for the channel. </summary>
        AssignMode,
        /// <summary> Indicates whether this channel uses the insertion EFX processor. </summary>
        EfxAssign,
        /// <summary>
        /// CC1 for GS controller matrix. An arbitrary MIDI controller, which can be bound to any synthesis parameter. Default is 16.
        /// </summary>
        CC1,
        /// <summary>
        /// CC2 for GS controller matrix. An arbitrary MIDI controller, which can be bound to any synthesis parameter. Default is 17.
        /// </summary>
        CC2,
        /// <summary>
        /// Drum map for GS system exclusive tracking.
        /// Only used for selecting the correct channel when setting drum parameters through sysEx,
        /// as those don't specify the channel, but the drum number.
        /// The only values that are allowed are 0 (melodic) 1 or 2.
        /// </summary>
        DrumMap,
        /// <summary>
        /// The relation between the input and the actual velocity.
        /// <para>
        /// If Velo Depth is increased, small differences in your playing dynamics will make a large difference in the loudness of the sound.
        /// If Velo Depth is decreased, even large differences in your playing dynamics will make only a small difference in the loudness of the sound.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Examples (with offset being set to normal):
        /// <list type="bullet">
        /// <item><description>64 is normal.</description></item>
        /// <item><description>32 is half velocity at max volume.</description></item>
        /// <item><description>127 is max velocity at half volume.</description></item>
        /// </list>
        /// See SC-8850 Manual page 56.
        /// </remarks>
        VelocitySenseDepth,
        /// <summary>
        /// Adjusts the velocity offset. 
        /// If set higher than 64, softly played notes will sound loudly. 
        /// If set lower than 64, strongly played notes will sound softly.
        /// </summary>
        /// <remarks>
        /// Examples (with depth set to normal):
        /// <list type="bullet">
        /// <item><description>64: Normal response.</description></item>
        /// <item><description>32: Silent until half velocity; max velocity produces half volume.</description></item>
        /// <item><description>96: Starts at half volume and reaches max volume at half velocity.</description></item>
        /// <item><description>127: Always forces velocity to maximum.</description></item>
        /// </list>
        /// See SC-8850 Manual page 56.
        /// </remarks>
        VelocitySenseOffset,
    }

    public static Params.Type TypeOf(Type type) => type switch
    {
        Type.PitchWheel => Params.Type.Int,
        Type.PitchWheelRange => Params.Type.Float,
        Type.Pressure => Params.Type.Int,
        Type.ModulationDepth => Params.Type.Float,
        Type.RxChannel => Params.Type.Int,
        Type.PolyMode => Params.Type.Bool,
        Type.KeyShift => Params.Type.Int,
        Type.RandomPan => Params.Type.Bool,
        Type.AssignMode => Params.Type.AssignMode,
        Type.EfxAssign => Params.Type.Bool,
        Type.CC1 => Params.Type.CC,
        Type.CC2 => Params.Type.CC,
        Type.DrumMap => Params.Type.Int,
        Type.FineTune => Params.Type.Float,
        Type.VelocitySenseDepth => Params.Type.Int,
        Type.VelocitySenseOffset => Params.Type.Int,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    
    private static void Assert(Type type, Params.Type value) =>
        Params.Assert(TypeOf(type), value);

    public static implicit operator ChannelMidiParameter(
        MidiChannel.Assign param) => Of(param);
    public static implicit operator ChannelMidiParameter(
        (Type Type, bool Value) param) => Of(param.Type, param.Value);
    public static implicit operator ChannelMidiParameter(
        (Type Type, int Value) param) => Of(param.Type, param.Value);
    public static implicit operator ChannelMidiParameter(
        (Type Type, float Value) param) => Of(param.Type, param.Value);
}