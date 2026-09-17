using System.Runtime.InteropServices;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.MIDI.Utils;




/// <summary>
/// The analysis result of an RPN (Registered Parameter Number) or NRPN (Non-Registered Parameter Number) MIDI message.
/// <remarks>
/// Channel number may be above 15 for multi-port MIDI setups.
/// </remarks>
/// </summary>
public readonly struct AnalyzedParameter
{
    public enum Type : byte
    {
        /// <summary> An unhandled or unrecognized parameter message. </summary>
        Other,
        /// <summary> A standard MIDI controller change mapped from an NRPN or parameter. </summary>
        ControllerChange, 
        /// <summary>
        /// Represents an analyzed channel MIDI parameter change message.
        /// <remarks>
        /// Channel number may be above 15 for multi-port MIDI setups.
        /// </remarks>
        /// </summary>
        ChannelMidiParameter, 
        /// <summary> A drum setup parameter message. </summary>
        DrumSetup,
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InternalData
    {
        [FieldOffset(0)] public (Midi.CC Controller, int Value, int Channel) _controllerChange;
        
        /// <summary>Channel number may be above 15</summary>
        [FieldOffset(0)] public (ChannelMidiParameter Param, int Channel) _channelMidiParam;

        [FieldOffset(0)] public (int Key, DrumParameter.Entry Parameter) _drumSetup;
    }
    
    public Type MType { get; private init; }
    private InternalData Data { get; init; }

    public (Midi.CC Controller, int Value, int Channel)? AsControllerChange =>
        MType == Type.ControllerChange ? Data._controllerChange : null;
    /// <summary> Represents an analyzed channel MIDI parameter change message. </summary>
    public (ChannelMidiParameter Param, int Channel)? AsChannelMidiParameter =>
        MType == Type.ChannelMidiParameter ? Data._channelMidiParam : null;
    
    public (int Key, DrumParameter.Entry Parameter)? AsDrumSetup =>
        MType == Type.DrumSetup ? Data._drumSetup : null;
    
    public static AnalyzedParameter Of(Type type)
    {
        ReadOnlySpan<Type> notAllowed = [
            Type.ControllerChange, Type.ChannelMidiParameter,
            Type.DrumSetup,];
        return notAllowed.Contains(type) 
            ? throw new ArgumentException("Invalid argument: " + type) 
            : new AnalyzedParameter { MType = type };
    }

    public static AnalyzedParameter OfControllerChange(
        Midi.CC controller, int value, int channel) =>
        new()
        {
            MType = Type.ControllerChange, 
            Data = new InternalData
                { _controllerChange = (controller, value, channel) },
        };

    public static AnalyzedParameter Of(
        ChannelMidiParameter parameter, int channel) =>
        new()
        {
            MType = Type.ChannelMidiParameter, 
            Data = new InternalData
                { _channelMidiParam = (parameter, channel) },
        };
    
    public static AnalyzedParameter Of(
        int key, DrumParameter.Entry param) =>
        new()
        {
            MType = Type.DrumSetup, 
            Data = new InternalData
                { _drumSetup = (key, param) },
        };
    
    public static implicit operator AnalyzedParameter(Type type) =>
        Of(type);
}

/// <summary>
/// The analysis result of a System Exclusive (SysEx) or (N)RPN MIDI message.
/// Represents various parsed MIDI events, including effect parameters, channel setups,
/// program changes, display data, global parameters, user drum setups, and other analyzed parameters.
/// </summary>
public readonly struct AnalyzedMessage
{
    public enum Type : byte
    {
        /// <summary> The analysis result of an RPN (Registered Parameter Number) or NRPN (Non-Registered Parameter Number) MIDI message. </summary>
        AnalyzedParameter,
        /// <summary> A message configuring whether a channel is set as a drum channel or melodic channel. </summary>
        DrumsOn,
        /// <summary> A MIDI program change message configured via System Exclusive. </summary>
        ProgramChange,
        /// <summary> A System Exclusive display data message (e.g., Roland GS or Yamaha XG LCD text or graphic display data). </summary>
        DisplayData,
        /// <summary> Represents an analyzed global MIDI parameter change message. </summary>
        GlobalMidiParameter, 
        /// <summary> A user drum set parameter setup message. </summary>
        UserDrumSetup,
                
        /// <summary> Represents an analyzed channel GS Reverb Processor change. </summary>
        GSReverbParameter,
        /// <summary> Represents an analyzed channel GS Chorus Processor change. </summary>
        GSChorusParameter,
        /// <summary> Represents an analyzed channel GS Delay Processor change. </summary>
        GSDelayParameter,
        /// <summary> Represents an analyzed channel GS Insertion Processor change. </summary>
        GSInsertionParameter,
        
        /// <summary> A reverb effect processor parameter message (Yamaha XG). </summary>
        XGReverbParam,
        /// <summary> A chorus effect processor parameter message (Yamaha XG). </summary>
        XGChorusParam,
        /// <summary> A variation effect processor parameter message (Yamaha XG). </summary>
        XGVariationParam,
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InternalData
    {
        [FieldOffset(0)] public AnalyzedParameter _analyzedParameter;
        [FieldOffset(0)] public (int Channel, bool IsDrum) _drumsOn;
        [FieldOffset(0)] public (int Channel, int Value) _programChange;
        [FieldOffset(0)] public GlobalMidiParameter _globalMidiParam;
        [FieldOffset(0)] public (int MidiNote,
            // 0-based
            int DrumSet, UserDrumSetParameter.Entry Parameter) _userDrumSetup;
        [FieldOffset(0)] public (Effect.GSReverbType Type, int Value) _gsReverb;
        [FieldOffset(0)] public (Effect.GSChorusType Type, int Value) _gsChorus;
        [FieldOffset(0)] public (Effect.GSDelayType Type, int Value) _gsDelay;
        [FieldOffset(0)] public (int Type, int Value, bool intType) _insertion;
    }

    /// <summary> The message type identifier. </summary>
    public Type MType { get; private init; }
    private InternalData Data { get; init; }

    /// <summary> A message configuring whether a channel is set as a drum channel or melodic channel. </summary>
    public (int Channel, bool IsDrum)? AsDrumsOn =>
        MType == Type.DrumsOn ? Data._drumsOn : null;
    public AnalyzedParameter? AsAnalyzedParameter =>
        MType == Type.AnalyzedParameter ? Data._analyzedParameter : null;
    /// <summary> A MIDI program change message configured via System Exclusive. </summary>
    public (int Channel, int Value)? AsProgramChange =>
        MType == Type.ProgramChange ? Data._programChange : null;
    /// <summary> Represents an analyzed global MIDI parameter change message. </summary>
    public GlobalMidiParameter? AsGlobalMidiParameter =>
        MType == Type.GlobalMidiParameter ? Data._globalMidiParam : null;
    /// <summary> A user drum set parameter setup message. </summary>
    public (int MidiNote, int DrumSet, UserDrumSetParameter.Entry Parameter)? AsUserDrumSetup =>
        MType == Type.UserDrumSetup ? Data._userDrumSetup : null;

    public static AnalyzedMessage Of(Type type)
    {
        ReadOnlySpan<Type> notAllowed = [
            Type.DrumsOn, Type.ProgramChange, Type.AnalyzedParameter,
            Type.GlobalMidiParameter, Type.UserDrumSetup,
            Type.GSReverbParameter, Type.GSChorusParameter, Type.GSDelayParameter, 
            Type.GSInsertionParameter, ];
        return notAllowed.Contains(type) 
            ? throw new ArgumentException("Invalid argument: " + type) 
            : new AnalyzedMessage { MType = type };
    }

    public static AnalyzedMessage OfDrumsOn(
        int channel, bool isDrum) =>
        new()
        {
            MType = Type.DrumsOn, 
            Data = new InternalData { _drumsOn = (channel, isDrum) },
        };
    
    public static AnalyzedMessage Of(
        AnalyzedParameter analyzedParameter) =>
        new()
        {
            MType = Type.AnalyzedParameter, 
            Data = new InternalData { _analyzedParameter = analyzedParameter },
        };
    
    public static AnalyzedMessage OfProgramChange(
        int channel, int value) =>
        new()
        {
            MType = Type.ProgramChange, 
            Data = new InternalData { _programChange = (channel, value) },
        };
    
    public static AnalyzedMessage Of(GlobalMidiParameter parameter) =>
        new()
        {
            MType = Type.GlobalMidiParameter, 
            Data = new InternalData { _globalMidiParam = parameter },
        };
            
    public static AnalyzedMessage Of(
        int midiNote, int drumSet, UserDrumSetParameter.Entry param) =>
        new()
        {
            MType = Type.UserDrumSetup, 
            Data = new InternalData
                { _userDrumSetup = (midiNote, drumSet, param) },
        };

    public static AnalyzedMessage Of(
        int midiNote, int drumSet, DrumParameter.Entry param) =>
        new()
        {
            MType = Type.UserDrumSetup, 
            Data = new InternalData
                { _userDrumSetup = (midiNote, drumSet, param) },
        };
    
        
    public static AnalyzedMessage Of(
        Effect.GSReverbType reverbType, int value) =>
        new()
        {
            MType = Type.GSReverbParameter, 
            Data = new InternalData { _gsReverb = (reverbType, value) },
        };
    
    public static AnalyzedMessage Of(
        Effect.GSChorusType chorusType, int value) =>
        new()
        {
            MType = Type.GSChorusParameter, 
            Data = new InternalData { _gsChorus = (chorusType, value) },
        };
    
    public static AnalyzedMessage Of(
        Effect.GSDelayType delayType, int value) =>
        new()
        {
            MType = Type.GSDelayParameter, 
            Data = new InternalData { _gsDelay = (delayType, value) },
        };
    
    public static AnalyzedMessage Of(
        Effect.InsertionType insertionType, int value) =>
        new()
        {
            MType = Type.GSInsertionParameter, 
            Data = new InternalData 
                { _insertion = ((int)insertionType, value, false) },
        };
    
    public static AnalyzedMessage OfInsertionParameter(
        int parameter, int value) =>
        new()
        {
            MType = Type.GSInsertionParameter, 
            Data = new InternalData 
                { _insertion = (parameter, value, true) },
        };
        
    /// <summary> Represents an analyzed channel GS Reverb Processor change. </summary>
    public (Effect.GSReverbType Type, int Value)? AsGSReverbParameter =>
        MType == Type.GSReverbParameter ? Data._gsReverb : null;
    /// <summary> Represents an analyzed channel GS Chorus Processor change. </summary>
    public (Effect.GSChorusType Type, int Value)? AsGSChorusParameter =>
        MType == Type.GSChorusParameter ? Data._gsChorus : null;
    /// <summary> Represents an analyzed channel GS Delay Processor change. </summary>
    public (Effect.GSDelayType Type, int Value)? AsGSDelayParameter =>
        MType == Type.GSDelayParameter ? Data._gsDelay : null;

    /// <summary> Represents an analyzed channel GS Insertion Processor change. </summary>
    public (Effect.InsertionType? Type, int? Parameter, int Value)? AsGSInsertionParameter
    {
        get
        {
            if (MType != Type.GSDelayParameter) return null;
            var i = Data._insertion;
            return (
                i.intType ? null : (Effect.InsertionType)i.Type,
                !i.intType ? null : i.Type,
                i.Value);
        }
    }

    public static implicit operator AnalyzedMessage(
        Type type) => Of(type);

    public static implicit operator AnalyzedMessage(
        AnalyzedParameter param) => Of(param);
    
    public static implicit operator AnalyzedMessage(
        AnalyzedParameter.Type type) => Of(AnalyzedParameter.Of(type));
}