using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Parameters;

public static class GlobalMidiParameters
{
    extension(ReadOnlySpan<GlobalMidiParameter> parameters)
    {
        public float Volume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalMidiParameter.Type.Volume].AsFloat;
        }
        
        public float Pan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalMidiParameter.Type.Pan].AsFloat;
        }
        
        public int KeyShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalMidiParameter.Type.KeyShift].AsInt;
        }
        
        public float FineTune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalMidiParameter.Type.FineTune].AsFloat;
        }
        
        public Midi.System System
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalMidiParameter.Type.System].AsMidiSystem;
        }
        
        public GlobalMidiParameter Get(
            GlobalMidiParameter.Type type) => parameters[(int)type];
    }
    
    public static void Set(Synthesizer synth, GlobalMidiParameter param)
    {
        if (synth.LockedParameters[(int)param.PType]) return;
        
        synth.MidiParameters[(int)param.PType] = param;
            
        foreach (var ch in synth.MidiChannels)
            ch.UpdateInternalParams();
            
        synth.CallEvent(new Event.CbGlobalMidiParameterChange(param));
    }

    private static readonly GlobalMidiParameter[] DefaultParameters;
    public static ReadOnlySpan<GlobalMidiParameter> Default => DefaultParameters;

    static GlobalMidiParameters()
    {
        // Avoid setting the param in the wrong position
        var list = (ReadOnlySpan<GlobalMidiParameter>)[
            (GlobalMidiParameter.Type.Volume, 1f),
            (GlobalMidiParameter.Type.Pan, 0f),
            (GlobalMidiParameter.Type.KeyShift, 0),
            (GlobalMidiParameter.Type.FineTune, 0f),
            Synthesizer.DefaultMode,
        ];
        
        DefaultParameters = new GlobalMidiParameter[list.Length];
        foreach (var param in list)
            DefaultParameters[(int)param.PType] = param;
    }
}

public readonly record struct GlobalMidiParameter
{
    public readonly Type PType;
    private readonly Params.Data _data;

    public Midi.System AsMidiSystem
    {
        get
        {
            Assert(PType, Params.Type.MidiSystem);
            return _data.AsEnum<Midi.System>();
        }
    }

    public bool AsBool
    {
        get
        {
            Assert(PType, Params.Type.Bool);
            return _data.AsBool();
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

    public float AsFloat
    {
        get
        {
            Assert(PType, Params.Type.Float);
            return _data.AsFloat();
        }
    }
    
    public static GlobalMidiParameter Of(Type type, int intVal)
    {
        Assert(type, Params.Type.Int);
        return new GlobalMidiParameter(type, Params.Of(intVal));
    }

    public static GlobalMidiParameter Of(Type type, float floatVal)
    {
        Assert(type, Params.Type.Float);
        return new GlobalMidiParameter(type, Params.Of(floatVal));
    }
    
    public static GlobalMidiParameter Of(Type type, bool boolVal)
    {
        Assert(type, Params.Type.Bool);
        return new GlobalMidiParameter(type, Params.Of(boolVal));
    }

    public static GlobalMidiParameter Of(Midi.System system)
    {
        Assert(Type.System, Params.Type.MidiSystem);
        return new GlobalMidiParameter(Type.System, Params.Of(system));
    }
    
    private GlobalMidiParameter(Type type, Params.Data data)
    {
        PType = type;
        _data = data;
    }

    private static void Assert(Type type, Params.Type value) =>
        Params.Assert(TypeOf(type), value);

    public static readonly int Len = Enum.GetValues<Type>().Length;
    
    public enum Type
    { 
        /// <summary>The currently enabled MIDI system used by the synthesizer for bank selects and system exclusives. (GM, GM2, GS, XG)</summary>
        System, 
        /// <summary>The global key shift in semitones. Drum channels ignore this value. Set by MIDI SysEx.</summary>
        KeyShift, 
        /// <summary>The global tuning in cents. Drum channels ignore this value. Set by MIDI SysEx.</summary>
        FineTune,
        /// <summary>
        /// The master volume.
        /// From 0 (silent) to 1 (full volume).
        ///
        /// This differs from the <b>gain</b> system parameter in that it is squared internally.
        /// </summary>
        Volume, 
        /// <summary>The master pan. From -1 (left) to 1 (right). 0 is center.</summary>
        Pan,
    }
    
    public static Params.Type TypeOf(Type type) => type switch
    {
        Type.System => Params.Type.MidiSystem,
        Type.Volume => Params.Type.Float,
        Type.Pan => Params.Type.Float,
        Type.KeyShift => Params.Type.Int,
        Type.FineTune => Params.Type.Float,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    
    public static implicit operator GlobalMidiParameter(
        (Type Type, float Value) param) => Of(param.Type, param.Value);
    public static implicit operator GlobalMidiParameter(
        (Type Type, int Value) param) => Of(param.Type, param.Value);
    public static implicit operator GlobalMidiParameter(
        Midi.System param) => Of(param);

    internal void Deconstruct<T>(out Type pType, out Params.Data data)
    {
        pType = PType;
        data = _data;
    }

}