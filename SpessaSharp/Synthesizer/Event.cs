using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer;

public readonly struct Event
{
    public enum Type
    {
        Null,

        NoteOn, NoteOff,
        ControllerChange, 
        ProgramChange, ChannelPressure, PolyPressure, StopAll, ChannelAdded, 
        MuteChannel, PresetListChange, Reset,
        DisplayMessage, SystemParameterChange,
        EffectChange,
        ChannelMidiParameterChange,
        GlobalMidiParameterChange,
    }
    
    /// <summary>This event fires when a note is played.</summary>
    /// <param name="MidiNote">The MIDI note number.</param>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="Velocity">The velocity of the note.</param>
    public readonly record struct CbNoteOn(
        int MidiNote, int Channel, int Velocity);

    /// <summary>This event fires when a note is released.</summary>
    /// <param name="MidiNote">The MIDI note number.</param>
    /// <param name="Channel">The MIDI channel number.</param>
    public readonly record struct CbNoteOff(int MidiNote, int Channel);
    
    public readonly record struct CbProgramChange(
        MidiPatch.Full Patch, int Channel);

    /// <summary>This event fires when a controller is changed.</summary>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="Controller">The controller number.</param>
    /// <param name="Value">The value of the controller.</param>
    public readonly record struct CbControllerChange(
        int Channel, Midi.CC Controller, int Value);

    /// <summary>This event fires when a channel is muted or unmuted.</summary>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="IsMuted">Indicates if the channel is muted.</param>
    public readonly record struct CbMuteChannel(int Channel, bool IsMuted);

    /// <summary> This event fires when the preset list is changed. </summary>
    /// <param name="Data"></param>
    public readonly record struct CbPresetListChange(
        IReadOnlyList<MidiPatch.Full> Data);

    /// <summary>This event fires when the synthesizer is reset</summary>
    /// <param name="System">The new System</param>
    public readonly record struct CbReset(Midi.System System);
    
    /// <summary> This event fires when the synthesizer receives a display message. </summary>
    /// <param name="Data"></param>
    public readonly record struct CbDisplayMessage(
        ArraySegment<byte> Data);

    /// <summary>This event fires when a channel pressure is changed.</summary>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="Pressure">The pressure value.</param>
    public readonly record struct CbChannelPressure(int Channel, int Pressure);

    /// <summary>This event fires when a polyphonic pressure is changed.</summary>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="MidiNote">The MIDI note number.</param>
    /// <param name="Pressure">The pressure value.</param>
    public readonly record struct CbPolyPressure(
        int Channel, int MidiNote, int Pressure);

    /// <summary> </summary>
    /// <param name="Channel">The MIDI channel number.</param>
    /// <param name="Force">If the channel was force stopped. (no release time)</param>
    public readonly record struct CbStopAll(int Channel, bool Force);

    /// <summary>This event fires when a system parameter changes.</summary>
    /// <param name="Parameter">The parameter that was changed.</param>
    public readonly record struct CbSystemParameterChange(
        GlobalSystemParameter Parameter);

    /// <summary>The effect that was changed, "reverb", "chorus", "delay" or "insertion"</summary>
    /// <param name="EffectType"></param>
    /// <param name="Parameter"></param>
    /// <param name="Value"></param>
    public readonly record struct CbEffectChange(
        CbEffectChange.Type EffectType, int Parameter, int Value)
    {
        public enum Type { Reverb, Chorus, Delay, Insertion, }
                
        /// <summary> </summary>
        /// <param name="Type">The parameter type or "macro".</param>
        /// <param name="Value">The new 7-bit value.</param>
        public readonly record struct Reverb(
            Effect.FxReverbType Type, int Value);
        
        public static CbEffectChange OfReverb(
            Effect.FxReverbType type, int value) =>
            new (Type.Reverb, (int)type, value);

        public Reverb AsReverb =>
            EffectType != Type.Chorus
                ? throw SpessaException.Invalid(
                    $"Expected type Reverb, got {EffectType}")
                : new Reverb((Effect.FxReverbType)Parameter, Value);

        /// <summary> </summary>
        /// <param name="Type">The parameter type or "macro".</param>
        /// <param name="Value">The new 7-bit value.</param>
        public readonly record struct Chorus(
            Effect.FxChorusType Type, int Value);

        public static CbEffectChange OfChorus(
            Effect.FxChorusType type, int value) =>
            new (Type.Chorus, (int)type, value);

        public Chorus AsChorus =>
            EffectType != Type.Chorus
                ? throw SpessaException.Invalid(
                    $"Expected type Chorus, got {EffectType}")
                : new Chorus((Effect.FxChorusType)Parameter, Value);
        
        /// <summary> </summary>
        /// <param name="Type">The parameter type or "macro".</param>
        /// <param name="Value">The new 7-bit value.</param>
        public readonly record struct Delay(
            Effect.FxDelayType Type, int Value);
        
        public static CbEffectChange OfDelay(
            Effect.FxDelayType type, int value) =>
            new (Type.Delay, (int)type, value);

        public Delay AsDelay =>
            EffectType != Type.Delay
                ? throw SpessaException.Invalid(
                    $"Expected type Delay, got {EffectType}")
                : new Delay((Effect.FxDelayType)Parameter, Value);
        
        /// <summary> </summary>
        /// <param name="Parameter">
        /// The parameter that was changed. This maps to GS address map at addr2 = 0x03.<br/>
        /// See SC-8850 Manual p.237,<br/>
        /// for example:<br/>
        /// - 0x0 - EFX type, the value is 16 bit in this special case. Note that this resets the parameters!<br/>
        /// - 0x3 - EFX param 1<br/>
        /// - 0x16 - EFX param 20 (usually level)<br/>
        /// - 0x17 - EFX send to reverb<br/>
        ///<br/>
        /// There are two exceptions:<br/>
        /// - -1 - the channel has ENABLED the effect.<br/>
        /// - -2 - the channel has DISABLED the effect.<br/>
        /// For both of these cases, `value` is the channel number.<br/>
        /// </param>
        /// <param name="Value">The new value for the parameter.</param>
        public readonly record struct Insertion(int Parameter, int Value);
        
        public static CbEffectChange OfInsertion(int parameter, int value) =>
            new (Type.Insertion, parameter, value);

        public Insertion AsInsertion =>
            EffectType != Type.Insertion
                ? throw SpessaException.Invalid(
                    $"Expected type Insertion, got {EffectType}")
                : new Insertion(Parameter, Value);
    }
    
    /// <summary>This event fires when a global MIDI parameter changes.</summary>
    /// <param name="Parameter">The type parameter and value that was changed.</param>
    public readonly record struct CbGlobalMidiParameterChange(
        GlobalMidiParameter Parameter);

    /// <summary>This event fires when a channel MIDI parameter changes.</summary>
    /// <param name="Channel">The channel that was affected.</param>
    /// <param name="Parameter">The type parameter and value that was changed.</param>
    public readonly record struct CbChannelMidiParameterChange(
        int Channel, ChannelMidiParameter Parameter);

    [StructLayout(LayoutKind.Explicit)]
    private struct EventsWithoutPointers
    {
        // This event fires when a note is played.
        [FieldOffset(0)] public CbNoteOn NoteOn;
        // This event fires when a note is released.
        [FieldOffset(0)] public CbNoteOff NoteOff;
        // This event fires when a controller is changed.
        [FieldOffset(0)] public CbControllerChange ControllerChange;
        // This event fires when a channel pressure is changed.
        [FieldOffset(0)] public CbChannelPressure ChannelPressure;
        // This event fires when a polyphonic pressure is changed.
        [FieldOffset(0)] public CbPolyPressure PolyPressure;
        // This event fires when all notes on a channel are stopped.
        [FieldOffset(0)] public CbStopAll StopAll;
        // This event fires when a channel is muted or unmuted.
        [FieldOffset(0)] public CbMuteChannel MuteChannel;
        // This event fires when a system parameter changes.
        [FieldOffset(0)] public CbSystemParameterChange SystemParameterChange;
        // This event fires when an effect processor is modified.
        [FieldOffset(0)] public CbEffectChange EffectChange;
        // This event fires when the synthesizer is reset
        [FieldOffset(0)] public CbReset Reset;

        [FieldOffset(0)] public CbChannelMidiParameterChange ChannelParamChange;
        [FieldOffset(0)] public CbGlobalMidiParameterChange GlobalParamChange;
    }

    private struct EventsWithPointers
    {
        // This event fires when the preset list is changed.
        public CbPresetListChange PresetListChange;
        // This event fires when the synthesizer receives a display message.
        public CbDisplayMessage DisplayMessage;
        // This event fires when a program is changed.
        public CbProgramChange ProgramChange;
    }

    public Type EventType { get; init; }

    private EventsWithoutPointers _e1 { get; init; }
    private EventsWithPointers _e2 { get; init; }

    public static Event Of(CbNoteOn noteOn) => new()
    {
        EventType = Type.NoteOn,
        _e1 = new EventsWithoutPointers { NoteOn = noteOn },
    };
    
    public static Event Of(CbNoteOff noteOff) => new()
    {
        EventType = Type.NoteOff,
        _e1 = new EventsWithoutPointers { NoteOff = noteOff },
    };
    
    public static Event Of(CbControllerChange controllerChange) => new()
    {
        EventType = Type.ControllerChange,
        _e1 = new EventsWithoutPointers { ControllerChange = controllerChange },
    };

    public static Event Of(CbProgramChange programChange) => new()
    {
        EventType = Type.ProgramChange,
        _e2 = new EventsWithPointers { ProgramChange = programChange },
    };
    
    public static Event Of(CbChannelPressure channelPressure) => new()
    {
        EventType = Type.ChannelPressure,
        _e1 = new EventsWithoutPointers { ChannelPressure = channelPressure },
    };
    
    public static Event Of(CbPolyPressure polyPressure) => new()
    {
        EventType = Type.PolyPressure,
        _e1 = new EventsWithoutPointers { PolyPressure = polyPressure },
    };
    
    public static Event Of(CbStopAll stopAll) => new() 
    {
        EventType = Type.StopAll,
        _e1 = new EventsWithoutPointers { StopAll = stopAll },
    };
    
    public static Event OfChannelAdded() => new() 
        { EventType = Type.ChannelAdded, };
    
    public static Event Of(CbMuteChannel muteChannel) => new()
    {
        EventType = Type.MuteChannel,
        _e1 = new EventsWithoutPointers { MuteChannel = muteChannel },
    };
    
    public static Event Of(CbReset reset) => new()
    {
        EventType = Type.Reset,
        _e1 = new EventsWithoutPointers { Reset = reset, },
    };
    
    public static Event Of(CbSystemParameterChange systemParamChange) => new()
    {
        EventType = Type.SystemParameterChange,
        _e1 = new EventsWithoutPointers { SystemParameterChange = systemParamChange },
    };
    
    public static Event Of(CbEffectChange effectChange) => new()
    {
        EventType = Type.EffectChange,
        _e1 = new EventsWithoutPointers { EffectChange = effectChange },
    };
    
    public static Event Of(CbGlobalMidiParameterChange globalParamChange) => new()
    {
        EventType = Type.GlobalMidiParameterChange,
        _e1 = new EventsWithoutPointers { GlobalParamChange = globalParamChange },
    };
    
    public static Event Of(CbChannelMidiParameterChange channelParamChange) => new()
    {
        EventType = Type.ChannelMidiParameterChange,
        _e1 = new EventsWithoutPointers { ChannelParamChange = channelParamChange },
    };

    public static Event Of(CbPresetListChange presetListChange) => new()
    {
        EventType = Type.PresetListChange,
        _e2 = new EventsWithPointers { PresetListChange = presetListChange },
    };
    
    public static Event Of(CbDisplayMessage displayMessage) => new()
    {
        EventType = Type.DisplayMessage,
        _e2 = new EventsWithPointers { DisplayMessage = displayMessage },
    };

    public CbNoteOn? AsNoteOn => 
        EventType == Type.NoteOn ? _e1.NoteOn : null;
    public CbNoteOff? AsNoteOff => 
        EventType == Type.NoteOff ? _e1.NoteOff : null;
    public CbControllerChange? AsControllerChange => 
        EventType == Type.ControllerChange ? _e1.ControllerChange : null;
    public CbProgramChange? AsProgramChange => 
        EventType == Type.ProgramChange ? _e2.ProgramChange : null;
    public CbChannelPressure? AsChannelPressure => 
        EventType == Type.ChannelPressure ? _e1.ChannelPressure : null;
    public CbPolyPressure? AsPolyPressure => 
        EventType == Type.PolyPressure ? _e1.PolyPressure : null;
    public CbStopAll? AsStopAll => 
        EventType == Type.StopAll ? _e1.StopAll : null;
    public bool IsChannelAdded => EventType == Type.ChannelAdded;
    public CbMuteChannel? AsMuteChannel => 
        EventType == Type.MuteChannel ? _e1.MuteChannel : null;
    public CbReset? AsReset =>
        EventType == Type.Reset ? _e1.Reset : null;
    public CbSystemParameterChange? AsSystemParameterChange => 
        EventType == Type.SystemParameterChange ? _e1.SystemParameterChange : null;
    public CbEffectChange? AsEffectChange => 
        EventType == Type.EffectChange ? _e1.EffectChange : null;
    public CbGlobalMidiParameterChange? AsGlobalParameterChange => 
        EventType == Type.GlobalMidiParameterChange ? _e1.GlobalParamChange : null;
    public CbChannelMidiParameterChange? AsChannelParameterChange => 
        EventType == Type.ChannelMidiParameterChange ? _e1.ChannelParamChange : null;
    public CbPresetListChange? AsPresetListChange => 
        EventType == Type.PresetListChange ? _e2.PresetListChange : null;
    public CbDisplayMessage? AsDisplayMessage => 
        EventType == Type.DisplayMessage ? _e2.DisplayMessage : null;

    public static implicit operator Event(CbNoteOn ev) => Of(ev);
    public static implicit operator Event(CbNoteOff ev) => Of(ev);
    public static implicit operator Event(CbControllerChange ev) => Of(ev);
    public static implicit operator Event(CbProgramChange ev) => Of(ev);
    public static implicit operator Event(CbChannelPressure ev) => Of(ev);
    public static implicit operator Event(CbPolyPressure ev) => Of(ev);
    public static implicit operator Event(CbStopAll ev) => Of(ev);
    public static implicit operator Event(CbMuteChannel ev) => Of(ev);
    public static implicit operator Event(CbReset ev) => Of(ev);
    public static implicit operator Event(CbSystemParameterChange ev) => Of(ev);
    public static implicit operator Event(CbEffectChange ev) => Of(ev);
    public static implicit operator Event(CbGlobalMidiParameterChange ev) => Of(ev);
    public static implicit operator Event(CbChannelMidiParameterChange ev) => Of(ev);
    public static implicit operator Event(CbPresetListChange ev) => Of(ev);
    public static implicit operator Event(CbDisplayMessage ev) => Of(ev);
}