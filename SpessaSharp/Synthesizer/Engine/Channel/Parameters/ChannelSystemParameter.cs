using System.Runtime.CompilerServices;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel.Parameters;

public static class ChannelSystemParameters
{
    extension(ReadOnlySpan<ChannelSystemParameter> parameters)
    {
        public bool PresetLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.PresetLock].AsBool;
        }

        public bool IsMuted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.IsMuted].AsBool;
        }
        
        public float Gain
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.Gain].AsFloat;
        }
        
        public float Pan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.Pan].AsFloat;
        }
        
        public int KeyShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.KeyShift].AsInt;
        }
        
        public float FineTune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.FineTune].AsFloat;
        }

        public Synthesizer.InterpolationType? InterpolationType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.InterpolationType]
                    .AsOptInterpolationType;
        }
        
        public bool? NprnParamLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.NPRNParamLock].AsOptBool;
        }
        
        public bool? MonophonicRetrigger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)ChannelSystemParameter.Type.MonophonicRetrigger].AsOptBool;
        }
     
        public ChannelSystemParameter Get(
                ChannelSystemParameter.Type type) =>
            parameters[(int)type];
    }
    
    /// <summary> Sets a system parameter of the channel</summary>
    /// <param name="param">The type and value of the system parameter to set.</param>
    public static void Set(
        MidiChannel chan, ChannelSystemParameter param)
    {
        var prev = chan.SystemParameters.Get(param.PType);
        if (prev == param) return;
        
        chan.SystemParamArray[(int)param.PType] = param;
        chan.UpdateInternalParams();
        
        // Additional handling for specific parameters
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (param.PType)
        {
            case ChannelSystemParameter.Type.PresetLock:
                if (param.AsBool)
                    chan.LockedSystem = chan.SynthCore.MidiParameters.System;
                break;
            case ChannelSystemParameter.Type.IsMuted:
                if (param.AsBool)
                    chan.StopAllNotes(true);
                break;
            case ChannelSystemParameter.Type.KeyShift:
                if (!chan.DrumChannel && prev.AsInt != param.AsInt)
                    chan.StopAllNotes(true);
                break;
            case ChannelSystemParameter.Type.Gain:
            case ChannelSystemParameter.Type.Pan:
            default: break;
        }
    }

    private static readonly ChannelSystemParameter[] DefaultParameters;
    public static ReadOnlySpan<ChannelSystemParameter> Default => DefaultParameters;

    static ChannelSystemParameters()
    {
        // Avoid setting the param in the wrong position
        var list = (ReadOnlySpan<ChannelSystemParameter>)[
            // Channel Exclusive
            (ChannelSystemParameter.Type.PresetLock, false),
            (ChannelSystemParameter.Type.IsMuted, false),
            
            // Shared with synth
            (ChannelSystemParameter.Type.Gain, 1f),
            (ChannelSystemParameter.Type.Pan, 0f),
            (ChannelSystemParameter.Type.KeyShift, 0),
            (ChannelSystemParameter.Type.FineTune, 0f),
            
            ChannelSystemParameter.OfNull(ChannelSystemParameter.Type.InterpolationType),
            ChannelSystemParameter.OfNull(ChannelSystemParameter.Type.NPRNParamLock),
            ChannelSystemParameter.OfNull(ChannelSystemParameter.Type.MonophonicRetrigger),
        ];
        
        DefaultParameters = new ChannelSystemParameter[list.Length];
        foreach (var param in list)
            DefaultParameters[(int)param.PType] = param;
    }
}

/// <summary>The system parameters of the channel.</summary>
public readonly record struct ChannelSystemParameter
{
    private readonly Params.Data _data;
    public readonly Type PType;
    
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

    public Synthesizer.InterpolationType? AsOptInterpolationType
    {
        get
        {
            Assert(PType, Params.Type.OptInterpolationType);
            return _data.AsOptEnum<Synthesizer.InterpolationType>();
        }
    }
    
    public bool? AsOptBool
    {
        get
        {
            Assert(PType, Params.Type.OptBool);
            return _data.AsOptBool();
        }
    }
    
    public enum Type
    {
        // Channel exclusive
        /// <summary>If the preset is locked, preventing any program changes from being sent.</summary>
        PresetLock,//bool
        /// <summary>If the channel should not produce any sound and ignore incoming Note On messages.</summary>
        IsMuted,//bool
        
        // Shared with synth
        /// <summary> The master gain, from 0 to any number. 1 is 100% volume. </summary>
        Gain,
        /// <summary> The master pan, from -1 (left) to 1 (right). 0 is center. </summary>
        Pan,
        /// <summary>The channel key shift in semitones. Drum channels DO NOT ignore this value.</summary>
        KeyShift,
        /// <summary>The channel tuning in cents. Drum channels DO NOT ignore this value.</summary>
        FineTune,
        /// <summary> The interpolation type used for sample playback. Overrides the global parameter if set. </summary>
        InterpolationType,
        /// <summary>
        /// If the channel should prevent changing any parameters via NRPN.
        /// Overrides the global parameter if set.
        /// </summary>
        NPRNParamLock,
        /// <summary>
        /// Indicates whether the channel is in monophonic retrigger mode.
        /// This emulates the behavior of Microsoft GS Wavetable Synth,
        /// Where a new note will kill the previous one if it is still playing.
        /// Overrides the global parameter if set.
        /// </summary>
        MonophonicRetrigger,
    }
    
    public static ChannelSystemParameter Of(Type type, float value)
    {
        Assert(type, Params.Type.Float);
        return new ChannelSystemParameter(type, Params.Of(value));
    }
    
    public static ChannelSystemParameter Of(Type type, int value)
    {
        Assert(type, Params.Type.Int);
        return new ChannelSystemParameter(type, Params.Of(value));
    }
    
    public static ChannelSystemParameter Of(Type type, bool value)
    {
        Assert(type, Params.Type.Bool);
        return new ChannelSystemParameter(type, Params.Of(value));
    }
    
    public static ChannelSystemParameter OfOpt(Type type, bool value)
    {
        Assert(type, Params.Type.OptBool);
        return new ChannelSystemParameter(type, Params.Of(value));
    }
    
    public static ChannelSystemParameter Of(
        Synthesizer.InterpolationType type)
    {
        Assert(Type.InterpolationType, Params.Type.OptInterpolationType);
        return new ChannelSystemParameter(
            Type.InterpolationType, Params.Of(type));
    }

    public static ChannelSystemParameter OfNull(Type type)
    {
        if (TypeOf(type) is not (Params.Type.OptBool or 
            Params.Type.OptInterpolationType))
            throw SpessaException.Invalid($"Invalid nullable type: {type}");
        return new ChannelSystemParameter(type, Params.OfNull());
    }

    private ChannelSystemParameter(Type type, Params.Data data)
    {
        PType = type; 
        _data = data;
    }

    private static Params.Type TypeOf(Type type) => type switch
    {
        Type.PresetLock => Params.Type.Bool,
        Type.IsMuted => Params.Type.Bool,
        
        Type.KeyShift => Params.Type.Int,
        
        Type.Gain => Params.Type.Float,
        Type.Pan => Params.Type.Float,
        Type.FineTune => Params.Type.Float,
        
        Type.InterpolationType => Params.Type.OptInterpolationType,
        Type.NPRNParamLock => Params.Type.OptBool,
        Type.MonophonicRetrigger => Params.Type.OptBool,
        
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    
    private static void Assert(Type type, Params.Type value) =>
        Params.Assert(TypeOf(type), value);
    
    public static implicit operator ChannelSystemParameter(
        (Type type, bool value) param) => Of(param.type, param.value);
    public static implicit operator ChannelSystemParameter(
        (Type type, float value) param) => Of(param.type, param.value);
    public static implicit operator ChannelSystemParameter(
        (Type type, int value) param) => Of(param.type, param.value);
    public static implicit operator ChannelSystemParameter(
        Synthesizer.InterpolationType param) => Of(param);
}