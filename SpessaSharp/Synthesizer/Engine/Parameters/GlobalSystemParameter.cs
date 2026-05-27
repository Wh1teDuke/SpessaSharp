using System.Diagnostics;
using System.Runtime.CompilerServices;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Parameters;


public static class GlobalSystemParameters
{
    extension(ReadOnlySpan<GlobalSystemParameter> parameters)
    {
        public Synthesizer.InterpolationType InterpolationType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.InterpolationType].AsInterpolationType;
        }

        public int VoiceCap
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => parameters[(int)GlobalSystemParameter.Type.VoiceCap].AsInt;
        }

        public bool AutoAllocateVoices
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.AutoAllocateVoices].AsBool;
        }

        public int DeviceID
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.DeviceID].AsInt;
        }
        
        public int KeyShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => parameters[(int)GlobalSystemParameter.Type.KeyShift].AsInt;
        }
        
        public float FineTune
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.FineTune].AsFloat;
        }

        public float Gain
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.Gain].AsFloat;
        }
        
        public float Pan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.Pan].AsFloat;
        }

        public float ReverbGain
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.ReverbGain].AsFloat;
        }

        public float ChorusGain
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.ChorusGain].AsFloat;
        }

        public float DelayGain
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.DelayGain].AsFloat;
        }

        public bool DrumLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.DrumLock].AsBool;
        }

        public bool DelayLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.DelayLock].AsBool;
        }

        public bool ReverbLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.ReverbLock].AsBool;
        }

        public bool ChorusLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.ChorusLock].AsBool;
        }

        public bool InsertionEffectLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.InsertionEffectLock].AsBool;
        }
        
        public bool EffectsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.EffectsEnabled].AsBool;
        }

        public bool EventsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.EventsEnabled].AsBool;
        }
        

        public bool BlackMIDIMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.BlackMIDIMode].AsBool;
        }
        
        public bool NprnParamLock
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get =>
                parameters[(int)GlobalSystemParameter.Type.NprnParamLock].AsBool;
        }

        public bool MonophonicRetrigger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 
                parameters[(int)GlobalSystemParameter.Type.MonophonicRetrigger].AsBool;
        }
        
        public GlobalSystemParameter Get(GlobalSystemParameter.Type type) =>
            parameters[(int)type];
    }
    
    public static void Set(
        Synthesizer synth, 
        GlobalSystemParameter param)
    {
        var prev = synth.SystemParameters.Get(param.PType);
        if (prev == param) return;
        
        synth.SystemParameters[(int)param.PType] = param;
        
        foreach (var ch in synth.MidiChannels)
            ch.UpdateInternalParams();
        
        // Additional handling for specific parameters
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (param.PType)
        {
            case GlobalSystemParameter.Type.VoiceCap:
                // Infinity is not allowed
                var cap = Math.Min(param.AsInt, 1_000_000);
                synth.SystemParameters[(int)param.PType] =
                    GlobalSystemParameter.Of(
                        GlobalSystemParameter.Type.VoiceCap, cap);
                
                // Disable all voices after cap
                for (var i = cap; i < synth.Voices.Count; i++)
                    synth.Voices[i].IsActive = false;

                if (cap > synth.Voices.Count) 
                {
                    Debug.WriteLine($"[WARN] Allocating {cap - synth.Voices.Count} new voices!");
                    synth.AllocateNewVoices(cap - synth.Voices.Count);
                }
                break;
            
            case GlobalSystemParameter.Type.KeyShift:
                if (param.AsInt != prev.AsInt)
                    synth.StopAllChannels(true);
                break;
            
            default: break;
        }
    }

    private static readonly GlobalSystemParameter[] DefaultParameters;
    public static ReadOnlySpan<GlobalSystemParameter> Default => DefaultParameters;

    static GlobalSystemParameters()
    {
        // Avoid setting the param in the wrong position
        var list = (ReadOnlySpan<GlobalSystemParameter>)[
            // Synth Exclusive
            (GlobalSystemParameter.Type.EffectsEnabled, true),
            (GlobalSystemParameter.Type.EventsEnabled, true),
            (GlobalSystemParameter.Type.VoiceCap, Synthesizer.VOICE_CAP),
            (GlobalSystemParameter.Type.AutoAllocateVoices, false),
            (GlobalSystemParameter.Type.ReverbGain, 1f),
            (GlobalSystemParameter.Type.ReverbLock, false),
            (GlobalSystemParameter.Type.ChorusGain, 1f),
            (GlobalSystemParameter.Type.ChorusLock, false),
            (GlobalSystemParameter.Type.DelayGain, 1f),
            (GlobalSystemParameter.Type.DelayLock, false),
            (GlobalSystemParameter.Type.InsertionEffectLock, false),
            (GlobalSystemParameter.Type.DrumLock, false),
            (GlobalSystemParameter.Type.BlackMIDIMode, false),
            (GlobalSystemParameter.Type.DeviceID, -1),
            
            // Shared with channel
            (GlobalSystemParameter.Type.Gain, 1f),
            (GlobalSystemParameter.Type.Pan, 0f),
            (GlobalSystemParameter.Type.KeyShift, 0),
            (GlobalSystemParameter.Type.FineTune, 0f),
            (Synthesizer.InterpolationType.Hermite),
            (GlobalSystemParameter.Type.NprnParamLock, false),
            (GlobalSystemParameter.Type.MonophonicRetrigger, false),
        ];
        
        DefaultParameters = new GlobalSystemParameter[list.Length];
        foreach (var param in list) 
            DefaultParameters[(int)param.PType] = param;
    }
}

public readonly record struct GlobalSystemParameter
{
    private readonly Params.Data _data;
    public readonly Type PType;
    

    
    public Synthesizer.InterpolationType AsInterpolationType
    {
        get
        {
            Assert(PType, Params.Type.InterpolationType);
            return _data.AsEnum<Synthesizer.InterpolationType>();
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
    
    public static GlobalSystemParameter Of(Type type, float val)
    {
        Assert(type, Params.Type.Float);
        return new GlobalSystemParameter(type, Params.Of(val));
    }
        
    public static GlobalSystemParameter Of(Type type, int val)
    {
        Assert(type, Params.Type.Int);
        return new GlobalSystemParameter(type, Params.Of(val));
    }
    
    public static GlobalSystemParameter Of(
        Synthesizer.InterpolationType type)
    {
        Assert(Type.InterpolationType, Params.Type.InterpolationType);
        return new GlobalSystemParameter(
            Type.InterpolationType, Params.Of(type));
    }

    public static GlobalSystemParameter Of(Type type, bool val)
    {
        Assert(type, Params.Type.Bool);
        return new GlobalSystemParameter(type, Params.Of(val));
    }

    private GlobalSystemParameter(Type type, Params.Data data)
    {
        PType = type; 
        _data = data;
    }

    public enum Type
    {
        // Synth Exclusive
        /// <summary> If the synthesizer processes the audio effects. </summary>
        EffectsEnabled,//bool
        /// <summary>If the event system is enabled.</summary>
        EventsEnabled,//bool
        /// <summary> The maximum number of voices that can be played at once.
        ///
        /// Increasing this value causes memory allocation for more voices.
        /// It is recommended to set it at the beginning, before rendering audio to avoid GC.
        /// Decreasing it does not cause memory usage change, so it's fine to use. </summary>
        VoiceCap,//int

        /// <summary>
        /// Enabling this parameter will cause a new voice allocation when the voice cap is hit, rather than stealing existing voices.
        /// This is not recommended in real-time environments. </summary>
        AutoAllocateVoices,//bool
        /// <summary> The reverb gain. From 0 to any number. 1 is 100% reverb. </summary>
        ReverbGain,//float
        /// <summary>
        /// If the synthesizer should prevent editing of the reverb parameters.
        /// This effect is modified using MIDI system exclusive messages, so
        /// the recommended use case would be setting
        /// the reverb parameters then locking it to prevent changes by MIDI files.
        /// </summary>
        ReverbLock,//bool
        /// <summary> The chorus gain. From 0 to any number. 1 is 100% chorus. </summary>
        ChorusGain,//float
        /// <summary>
        /// If the synthesizer should prevent editing of the chorus parameters.
        /// This effect is modified using MIDI system exclusive messages, so
        /// the recommended use case would be setting
        /// the chorus parameters then locking it to prevent changes by MIDI files.
        /// </summary>
        ChorusLock,//bool
        /// <summary>
        /// The delay gain. From 0 to any number. 1 is 100% delay.
        /// </summary>
        DelayGain,//float
        /// <summary>
        /// If the synthesizer should prevent editing of the delay parameters.
        /// This effect is modified using MIDI system exclusive messages, so
        /// the recommended use case would be setting
        /// the delay parameters then locking it to prevent changes by MIDI files.
        /// </summary>
        DelayLock,//bool
        /// <summary>
        /// If the synthesizer should prevent changing the insertion effect type and parameters (including enabling/disabling it on channels).
        /// This effect is modified using MIDI system exclusive messages, so
        /// the recommended use case would be setting
        /// the insertion effect type and parameters then locking it to prevent changes by MIDI files.
        /// </summary>
        InsertionEffectLock,//bool
        /// <summary>
        /// If the synthesizer should prevent editing of the drum parameters.
        /// These params are modified using MIDI system exclusive messages or NRPN, so
        /// the recommended use case would be setting
        /// the drum parameters then locking it to prevent changes by MIDI files.
        /// </summary>
        DrumLock,//bool
        /// <summary> Forces note killing instead of releasing. Improves performance in black MIDIs. </summary>
        BlackMIDIMode,//bool
        /// <summary> Synthesizer's device ID for system exclusive messages. Set to -1 to accept all. </summary>
        DeviceID,//int
        /// <summary>The master gain. From 0 to any number. 1 is 100% volume. </summary>
        Gain,//float
        /// <summary>The master pan. From -1 (left) to 1 (right). 0 is center. </summary>
        Pan,//float
        /// <summary>The global key shift in semitones. Drum channels ignore this value.</summary>
        KeyShift,//int
        /// <summary> The global tuning in cents. Drum channels ignore this value. </summary>
        FineTune,//float
        /// <summary> The interpolation type used for sample playback. </summary>
        InterpolationType,//InterpolationType
        /// <summary>
        /// If the synthesizer should prevent changing any parameters via NRPN.
        /// This includes the custom vibrato parameters.
        /// </summary>
        NprnParamLock,//bool
        /// <summary>
        /// Indicates whether the synthesizer is in monophonic retrigger mode.
        /// This emulates the behavior of Microsoft GS Wavetable Synth,
        /// Where a new note will kill the previous one if it is still playing.
        /// </summary>
        MonophonicRetrigger,//bool
    }

    public static Params.Type TypeOf(Type type) => type switch
    {
        Type.EffectsEnabled => Params.Type.Bool,
        Type.EventsEnabled => Params.Type.Bool,
        Type.Gain => Params.Type.Float,
        Type.Pan => Params.Type.Float,
        Type.VoiceCap => Params.Type.Int,
        Type.AutoAllocateVoices => Params.Type.Bool,
        Type.InterpolationType => Params.Type.InterpolationType,
        Type.MonophonicRetrigger => Params.Type.Bool,
        Type.ReverbGain => Params.Type.Float,
        Type.ReverbLock => Params.Type.Bool,
        Type.ChorusGain => Params.Type.Float,
        Type.ChorusLock => Params.Type.Bool,
        Type.DelayGain => Params.Type.Float,
        Type.DelayLock => Params.Type.Bool,
        Type.InsertionEffectLock => Params.Type.Bool,
        Type.DrumLock => Params.Type.Bool,
        Type.NprnParamLock => Params.Type.Bool,
        Type.BlackMIDIMode => Params.Type.Bool,
        Type.KeyShift => Params.Type.Int,
        Type.FineTune => Params.Type.Float,
        Type.DeviceID => Params.Type.Int,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static readonly Type[] List = Enum.GetValues<Type>();
    public static readonly string[] Names = Enum.GetNames<Type>();

    public static readonly string[] Descriptions =
    [
        "If the synthesizer processes the audio effects.",
        "If the event system is enabled.",
        "The maximum number of voices that can be played at once. Increasing this value causes memory allocation for more voices. It is recommended to set it at the beginning, before rendering audio to avoid GC. Decreasing it does not cause memory usage change, so it's fine to use.",
        "Enabling this parameter will cause a new voice allocation when the voice cap is hit, rather than stealing existing voices. This is not recommended in real-time environments.",
        "The reverb gain. From 0 to any number. 1 is 100% reverb",
        "If the synthesizer should prevent editing of the reverb parameters. This effect is modified using MIDI system exclusive messages, so the recommended use case would be setting the reverb parameters then locking it to prevent changes by MIDI files.",
        "The chorus gain. From 0 to any number. 1 is 100% chorus",
        "If the synthesizer should prevent editing of the chorus parameters. This effect is modified using MIDI system exclusive messages, so the recommended use case would be setting the chorus parameters then locking it to prevent changes by MIDI files.",
        "The delay gain. From 0 to any number. 1 is 100% delay",
        "If the synthesizer should prevent editing of the delay parameters. This effect is modified using MIDI system exclusive messages, so the recommended use case would be setting the delay parameters then locking it to prevent changes by MIDI files.",
        "If the synthesizer should prevent changing the insertion effect type and parameters (including enabling/disabling it on channels). This effect is modified using MIDI system exclusive messages, so the recommended use case would be setting the insertion effect type and parameters then locking it to prevent changes by MIDI files.",
        "If the synthesizer should prevent editing of the drum parameters. These params are modified using MIDI system exclusive messages or NRPN, so the recommended use case would be setting the drum parameters then locking it to prevent changes by MIDI files.",
        "Forces note killing instead of releasing. Improves performance in black MIDIs",
        "Synthesizer's device ID for system exclusive messages. Set to -1 to accept all",
        "The master gain. From 0 to any number. 1 is 100% volume",
        "The master pan. From -1 (left) to 1 (right). 0 is center",
        "The global key shift in semitones. Drum channels ignore this value.",
        "The global tuning in cents. Drum channels ignore this value.",
        "The interpolation type used for sample playback",
        "If the synthesizer should prevent applying the custom vibrato. This effect is modified using NRPN, so the recommended use case would be setting the custom vibrato then locking it to prevent changes by MIDI files.",
        "If the synthesizer should prevent changing any parameters via NRPN. This includes the custom vibrato parameters.",
        "Indicates whether the synthesizer is in monophonic retrigger mode. This emulates the behavior of Microsoft GS Wavetable Synth, where a new note will kill the previous one if it is still playing.",
    ];
    
    private static void Assert(Type type, Params.Type value) =>
        Params.Assert(TypeOf(type), value);
    
    public static implicit operator GlobalSystemParameter(
        (Type Type, int Value) param) => Of(param.Type,  param.Value);
    public static implicit operator GlobalSystemParameter(
        (Type Type, float Value) param) => Of(param.Type,  param.Value);
    public static implicit operator GlobalSystemParameter(
        (Type Type, bool Value) param) => Of(param.Type,  param.Value);
    public static implicit operator GlobalSystemParameter(
        Synthesizer.InterpolationType param) => Of(param);
}