using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary>A single-use class for editing a MIDI file</summary>
public sealed class MidiEditor
{
    /// <summary>
    /// Allows easy editing of the file by removing channels, changing programs,
    /// changing controllers and transposing channels. Note that this modifies the MIDI in-place.
    /// </summary>
    /// <param name="Channels">
    /// The channel changes.<br/>
    /// - Key: the MIDI channel number.<br/>
    /// - value:<br/>
    ///   - <b>Clear</b> - all MIDI messages for this channel, such as Note On are removed.<br/>
    ///   - <b>ChannelModification</b> - modifies the channel.
    /// </param>
    /// <param name="UserDrumSetParams">
    /// The User Drum Set changes. (MIDI program 64).
    /// <list type="bullet">
    /// <item><description>
    /// <b>Key:</b> the User Drum Set number, 0 based.
    /// 0 is the User Drum Set 1 located at MIDI program 64, and 1 is User Drum Set 2 located at MIDI program 65.
    /// </description></item>
    /// <item><description><b>Value:</b>
    /// <list type="bullet">
    /// <item><description><b>Clear:</b> all existing User Drum Set 1 changes are removed.</description></item>
    /// <item><description><b>UserDrumModification:</b> modifies the drum set.</description></item>
    /// </list>
    /// </description></item>
    /// </list>
    /// </param>
    /// <param name="DrumSetupParams">
    /// The drum parameter changes.<br/>
    /// - <b>Clear</b> - all existing drum change MIDI messages are removed.<br/>
    /// - <b>Null</b> - not yet implemented.
    /// </param>
    /// <param name="MidiParams">
    /// The global MIDI parameter changes.
    /// <list type="bullet">
    /// <item><description>Key: the MIDI parameter name.</description></item>
    /// <item><description>value:
    /// <list type="bullet">
    /// <item><description>Clear - all changes for this parameter are removed.</description></item>
    /// <item><description>Specific value - clear + sets the new parameter at the start of the song, effectively locking them to the set value.</description></item>
    /// </list></description></item>
    /// </list>
    /// 
    /// Please note that <b>clear</b> is not supported for the <b>system</b> parameter,
    /// as it may cause issues with the MIDI system detection and reset insertion.
    /// </param>
    /// <param name="ReverbParams">
    /// The desired GS reverb parameters.<br/>
    /// - <b>Clear</b> - all existing parameter change MIDI messages are removed.<br/>
    /// - <b>ReverbProcessorSnapshot</b> - clear + the new parameters are set via System Exclusive messages.
    /// </param>
    /// <param name="ChorusParams">
    /// The GS chorus parameters.<br/>
    /// - <b>Clear</b> - all existing parameter change MIDI messages are cleared.<br/>
    /// - <b>ChorusProcessorSnapshot</b> - clear + the new parameters are set via System Exclusive messages.
    /// </param>
    /// <param name="DelayParams">
    /// The GS delay parameters.<br/>
    /// - <b>Clear</b> - all existing parameter change MIDI messages are cleared.<br/>
    /// - <b>DelayProcessorSnapshot</b> - clear + the new parameters are set via System Exclusive messages.
    /// </param>
    /// <param name="InsertionParams">
    /// The GS Insertion Effect parameters.<br/>
    /// - <b>Clear</b> - all existing parameter change MIDI messages are cleared.<br/>
    /// - <b>InsertionProcessorSnapshot</b> - clear + the new parameters are set via System Exclusive messages.
    /// </param>
    public readonly record struct Options(
        Dictionary<int, Parameter<ChannelModification>>? Channels = null,
        Dictionary<int, Parameter<UserDrumModification>>? UserDrumSetParams = null,
        Parameter<object>? DrumSetupParams = null,
        Dictionary<
            GlobalMidiParameter.Type,
            Parameter<GlobalMidiParameter>>? MidiParams = null,
        Parameter<Effect.ReverbProcessorSnapshot>? ReverbParams = null,
        Parameter<Effect.ChorusProcessorSnapshot>? ChorusParams = null,
        Parameter<Effect.DelayProcessorSnapshot>? DelayParams = null,
        Parameter<Effect.InsertionProcessorSnapshot>? InsertionParams = null);

    /// <summary>
    /// Represents a value that means "clear this parameter" instead of "replace this parameter with".
    /// Essentially:
    /// <list type="bullet">
    /// <item><description><b>Clear</b> - clear all changes of this parameter from the MIDI file.</description></item>
    /// <item><description><b>Replace</b> - clear all changes of this parameter from the MIDI file and add T.</description></item>
    /// </list>
    /// </summary>
    public abstract record Parameter<T>
    {
        internal sealed record Clear: Parameter<T>;
        internal sealed record Replace(T Value) : Parameter<T>;

        internal bool IsClear() => this is Clear;
        internal Replace? AsReplace() => this as Replace;

        public static Parameter<T> OfClear() => new Clear();
        public static Parameter<T> OfReplace(T value) => new Replace(value);
        
        public static implicit operator Parameter<T>(T t) =>
            OfReplace(t);
    }

    /// <summary>
    /// </summary>
    public sealed class ChannelModification
    {
        /// <summary>
        /// All controllers that should be modified for this channel.
        /// <list type="bullet">
        /// <item><description><b>Key</b>: the MIDI controller number.</description></item>
        /// <item><description><b>Value</b>:
        /// <list type="bullet">
        /// <item><description><b>Clear</b> - all controller changes for this controller are removed.</description></item>
        /// <item><description><b>Int</b> - clear + sets the new controller at the start of the song, effectively locking them to the set value.</description></item>
        /// </list></description></item>
        /// </list>
        /// </summary>
        public Dictionary<Midi.CC, Parameter<int>>? Controllers;
        
        /// <summary>
        /// The new program of this channel.
        /// <list type="bullet">
        /// <item><description><b>Clear</b> - all program changes for this channel are removed.</description></item>
        /// <item><description><b>MidiPatch</b> - clear + sets the new patch according to the MIDI system at the start of the sequence.</description></item>
        /// </list>
        /// </summary>
        public Parameter<MidiPatch>? Patch;

        /// <summary>
        /// The new MIDI parameters of this channel.
        /// <list type="bullet">
        /// <item><description><b>Key</b>: the MIDI parameter name.</description></item>
        /// <item><description><b>Value</b>:
        /// <list type="bullet">
        /// <item><description><b>Clear</b> - all changes for this parameter are removed.</description></item>
        /// <item><description><b>Specific Value</b> - clear + sets the new parameter at the start of the song, effectively locking them to the set value.</description></item>
        /// </list>
        /// </description></item>
        /// </list>
        /// </summary>
        public Dictionary<
            ChannelMidiParameter.Type,
            Parameter<ChannelMidiParameter>>? MidiParameters;

        /// <summary>
        /// The channel key shift in semitones. Note on/off numbers are shifted.
        /// This differs from the `keyShift` MIDI Parameter in that it shifts the actual note numbers,
        /// and doesn't delete or overwrite existing shifts.
        /// </summary>
        public int? KeyShift;
        
        /// <summary>
        /// The channel tuning in cents. Tuned using RPN Fine Tune. Range is <b>[-100; 99.986]</b> cents.
        /// This differs from the `fineTune` MIDI Parameter
        /// in that it is relative to the tuning applied in the MIDI file,
        /// and it does not overwrite it.
        /// </summary>
        public float? FineTune;

        public void Set(ChannelMidiParameter param) =>
            MidiParameters?[param.PType] = param;
    }

    /// <summary>
    /// All modifications for this User Drum Set.
    /// </summary>
    /// <param name="Mods">
    /// <list type="bullet">
    /// <item><description><b>Key</b>: the MIDI note number for the note to modify.</description></item>
    /// <item><description><b>Value</b>:
    /// <list type="bullet">
    /// <item><description><b>Clear</b> - all modifications for this note are removed.</description></item>
    /// <item><description><b>Object</b> - partial parameter changes for this note:
    /// <list type="bullet">
    /// <item><description><b>Key</b>: User Drum Set parameter name.</description></item>
    /// <item><description><b>Value</b>:
    /// <list type="bullet">
    /// <item><description><b>Clear</b> - all modifications for this note are removed.</description></item>
    /// <item><description><b>specific value</b> - clear + insert a message setting this after a reset.</description></item>
    /// </list>
    /// </description></item>
    /// </list>
    /// </description></item>
    /// </list>
    /// </description></item>
    /// </list>
    /// </param>
    public readonly record struct UserDrumModification(
        // TODO: Parameter stuff needs refactoring because it is driving me nuts
        Dictionary<
            int, 
            Parameter<Dictionary<
                UserDrumSetParameter.Type,
                Parameter<UserDrumSetParameter.Entry>>>> Mods);

    public static Dictionary<
        int, Parameter<ChannelModification>> ChannelModifications(
            params ReadOnlySpan<(int Channel, Parameter<ChannelModification> Modification)> values)
    {
        var result = new Dictionary<int, Parameter<ChannelModification>>();
        foreach (var value in values)
            result.Add(value.Channel, value.Modification);
        return result;
    }

    public static Parameter<T> Replace<T>(T value) =>
        Parameter<T>.OfReplace(value);
    
    public static Parameter<T> Clear<T>() => Parameter<T>.OfClear();
    
    public static void Replace<T>(out Parameter<T> param, T value)
        => param = Parameter<T>.OfReplace(value);

    private static readonly Effect.ReverbProcessorSnapshot ReverbAddressMap = new()
    {
        Character = 0x31,
        PreLowPass = 0x32,
        Level = 0x33,
        Time = 0x34,
        DelayFeedback = 0x35,
        PreDelayTime = 0x37,
    };

    private static readonly Effect.ChorusProcessorSnapshot ChorusAddressMap = new()
    {
        PreLowPass = 0x39,
        Level = 0x3a,
        Feedback = 0x3b,
        Delay = 0x3c,
        Rate = 0x3d,
        Depth = 0x3e,
        SendLevelToReverb = 0x3f,
        SendLevelToDelay = 0x40,
    };

    private static readonly Effect.DelayProcessorSnapshot DelayAddressMap = new()
    {
        PreLowPass = 0x51,
        TimeCenter = 0x52,
        TimeRatioLeft = 0x53,
        TimeRatioRight = 0x54,
        LevelCenter = 0x55,
        LevelLeft = 0x56,
        LevelRight = 0x57,
        Level = 0x58,
        Feedback = 0x59,
        SendLevelToReverb = 0x5a,
    };

    /// <summary>Internal tracking interface</summary>
    private sealed class ChannelStatus
    {
        /// <summary>Tracks if the channel already had its first note on</summary>
        public bool IsFirstNoteOn;
        /// <summary>RPN/NRPN tracking</summary>
        public ParamTracker Param;
        /// <summary>
        /// If the parameters (MSB, LSB and the first data) were cleared.
        /// Some MIDIs send param MSB once and then set via LSB only, like:<br/>
        /// MSB, LSB, Data, LSB, Data,<br/>
        /// And even though it violates MIDI 1.0, it works ...
        /// </summary>
        public (bool LSB, bool MSB, bool Data) ClearedParams;

        /// <summary> Channel number for logging </summary>
        public required int Channel;

        /// <summary>Semitones, for easier access rather than having to do "?? 0"</summary>
        public required int KeyShift;

        /// <summary>Cents, for easier access rather than having to do "?? 0"</summary>
        public float FineTune;
        /// <summary>
        /// Since total tune has to be applied relatively,
        /// We need to track the currently applied key shift
        /// </summary>
        public int CurrentKeyShift;
        /// <summary>
        /// Same case as with above, since tuning may exceed the RPN range.
        /// </summary>
        public float CurrentFineTune;
    }

    private readonly Midi _midi;
    private readonly bool _clearDrumParams;
    private readonly Dictionary<int, ChannelModification> _channelChanges;
    // Track only channels to clear
    private readonly HashSet<int> _clearedChannels;
    private Midi.System? _system;
    private readonly ChannelStatus[] _channelStatuses;
    /// <summary>MIDI port number for the corresponding track</summary>
    private readonly List<int> _midiPorts;
    private readonly Dictionary<int, int> _midiPortChannelOffsets;
    private int _currentPortOffset = 0;
    private int? _currentParameterChannel;

    /// <summary>
    /// Track only channels to clear
    /// </summary>
    private bool _addedReset = false;
    
    // Track reset position to insert effects right after
    private int _resetTrack = 0;
    private int _resetIndex = 0;

    private readonly Parameter<Effect.ReverbProcessorSnapshot>? _reverbParams;
    private readonly Parameter<Effect.ChorusProcessorSnapshot>? _chorusParams;
    private readonly Parameter<Effect.DelayProcessorSnapshot>? _delayParams;
    private readonly Parameter<Effect.InsertionProcessorSnapshot>? _insertionParams;
    private readonly Dictionary<int, Parameter<UserDrumModification>>? _userDrumSetParams;
    private readonly Dictionary<
        GlobalMidiParameter.Type,
        Parameter<GlobalMidiParameter>>? _midiParams;

    /// <summary> Current, for handleEvent </summary>
    private int _trackNum = 0;

    /// <summary>Current, for handleEvent</summary>
    private ArraySegment<int> _eventIndexes = [];

    /// <summary>
    /// Allows easy editing of the file by removing channels, changing programs,
    /// changing controllers and transposing channels. Note that this modifies the MIDI in-place.
    /// </summary>
    internal MidiEditor(Midi midi, Options opts)
    {
        SpessaLog.Info("Applying changes to the MIDI file ...");
        
        _midi = midi;
        _channelChanges = [];
        _clearedChannels = [];
        _channelStatuses = [];
        
        // Save Options
        _reverbParams = opts.ReverbParams;
        _chorusParams = opts.ChorusParams;
        _delayParams = opts.DelayParams;
        _insertionParams = opts.InsertionParams;
        _userDrumSetParams = opts.UserDrumSetParams;
        _midiParams = opts.MidiParams;
        _midiPortChannelOffsets = [];
        
        // Optimizations
        _clearDrumParams = opts.DrumSetupParams?.IsClear() is true;
        // Track only channels to change here
        if (opts.Channels is { } channels)
        {
            foreach (var (channel, ch) in channels)
            {
                switch (ch)
                {
                    case Parameter<ChannelModification>.Clear:
                        _clearedChannels.Add(channel);
                        break;
                    case Parameter<ChannelModification>.Replace(var value):
                        _channelChanges[channel] = value;
                        break;
                }
            }
        }
        
        // Go through all events one by one
        _system = Midi.System.GS;
        {
            if (opts.MidiParams?.TryGetValue(
                    GlobalMidiParameter.Type.System,
                    out var parameter) ?? false)
            {
                _system = parameter switch
                {
                    Parameter<GlobalMidiParameter>.Clear =>
                        null,
                    Parameter<GlobalMidiParameter>.Replace(var value) =>
                        value.AsMidiSystem,
                    _ => _system
                };
            }
        }
        
        // It copies midiPorts everywhere else, but here 0 works so DO NOT CHANGE!
        // MIDI port number for the corresponding track
        _midiPorts = [.. midi.Tracks.Select(t => t.Port)];
        
        // Assign port offsets
        for (var i = 0; i < midi.Tracks.Count; i++)
            AssignMidiPort(i, midi.Tracks[i].Port);
        
        var channelsAmount = _currentPortOffset;
        _channelStatuses = new ChannelStatus[channelsAmount];

        for (var i = 0; i < channelsAmount; i++)
        {
            var keyShift = 0;
            var fineTune = 0f;

            if (_channelChanges.GetValueOrDefault(i) is {} chanmod)
            {
                keyShift = chanmod.KeyShift ?? 0;
                fineTune = chanmod.FineTune ?? 0;
            }

            _channelStatuses[i] = new ChannelStatus
            {
                Channel = i,
                IsFirstNoteOn = true,
                Param = new ParamTracker(i),
                ClearedParams = (true, true, true),
                KeyShift = keyShift,
                FineTune = fineTune,
                CurrentKeyShift = 0,
                CurrentFineTune = 0,
            };
        }
    }

    public void Apply()
    {
        _midi.Iterate(HandleEvent);
        ApplyResetParams();
    }

    private void AssignMidiPort(int trackNum, int port)
    {
        // Do not assign ports to empty tracks
            
        // Midi port: channel offset
        if (_midi.Tracks[trackNum].Channels.Count == 0)
            return;
            
        // Assign new 16 channels if the port is not occupied yet
        if (_currentPortOffset == 0) 
        {
            _currentPortOffset += 16;
            _midiPortChannelOffsets[port] = 0;
        }
            
        if (_midiPortChannelOffsets.TryAdd(port, _currentPortOffset))
            _currentPortOffset += 16;
            
        _midiPorts[trackNum] = port;
    }
    
    /// <summary>
    /// This function adds the events before the current one IN ORDER they are in the array,
    /// So the first event in the array will end up as the first one before the current event.
    /// </summary>
    private void AddEventsBefore(params ReadOnlySpan<MidiMessage> events) 
    {
        foreach (var item in events) 
        {
            _midi.Tracks[_trackNum].Add(item, _eventIndexes[_trackNum]);
            _eventIndexes[_trackNum]++;
        }
    }

    /// <summary>Deletes this event, or parameter.</summary>
    private void DeleteThisEvent()
    {
        _midi.Tracks[_trackNum].DeleteEvent(
            _eventIndexes[_trackNum]--);
    }
    
    private void DeleteCurrentEvent() 
    {
        if (_currentParameterChannel is not null) 
        {
            DeleteCurrentParameter();
            return;
        }
        DeleteThisEvent();
    }

    private void DeleteCurrentParameter()
    {
        var index = _eventIndexes[_trackNum];
        var ch = _channelStatuses[_currentParameterChannel!.Value];
        // Delete the parameter selection pair + the data entry that we're currently processing.
        // We don't wait for lsb as it's not required to arrive :-(
        // Why, MIDI, why are you like this?
        // Now I have to handle this complex mess that has to work for either single or double data...
        // And both parameters aren't even required to be sent! Well, they are! But some files don't care.
        // And Sound Canvases don't seem to care either...

        // Testcase: MIDI_Jam & Spoon_Right In The Night.mid, channel 12.
        // That's why we track what we can and can't delete.
        var p = ch.Param;
        var msb = p.ParamMSB;
        var lsb = p.ParamLSB;

        // Delete the current data entry event first.
        // This is safe because it's the event currently being processed in the loop,
        // Meaning its index is always higher than or equal
        // To the cached MSB/LSB (on a different track).
        if (!ch.ClearedParams.Data)
        {
            DeleteThisEvent();
            SpessaLog.Info(
                $"Clearing Non/Registered Parameter on {ch.Channel
                }. (Current data entry + params)");
            
            // Shift the events down if they are on the same track (very likely)
            if (_trackNum == msb.Track && index < msb.Event) msb.Event--;
            if (_trackNum == lsb.Track && index < lsb.Event) lsb.Event--;
        }
        
        if (!ch.ClearedParams.MSB) 
        {
            // Delete data MSB
            DeleteEvent(msb.Event, msb.Track);
            SpessaLog.Info(
                $"Clearing Non/Registered Parameter on {ch.Channel
                }. (Data entry MSB)");

            // Shift the LSB down if they are on the same track (very likely)
            if (msb.Track == lsb.Track && msb.Event < lsb.Event)
                lsb.Event--;
        }
        
        if (!ch.ClearedParams.LSB) 
        {
            // Delete data LSB
            DeleteEvent(lsb.Event, lsb.Track);
            
            SpessaLog.Info(
                $"Clearing Non/Registered Parameter on {ch.Channel
                }. (Data entry LSB)");
        }

        p.ParamMSB = msb;
        p.ParamLSB = lsb;
        ch.Param = p;
        // Flag params as deleted
        ch.ClearedParams = (true, true, true);
        return;

        void DeleteEvent(int eventNum, int track)
        {
            _midi.Tracks[track].DeleteEvent(eventNum);
            _eventIndexes[track]--;
        }
    }

    private void HandleEvent(
        MidiMessage e, int trackNum, ArraySegment<int> eventIndexes)
    {
        _trackNum = trackNum;
        _eventIndexes = eventIndexes;
        _currentParameterChannel = null;
        
        var portOffset = _midiPortChannelOffsets.GetValueOrDefault(
            _midiPorts[trackNum], 0);
        if (e.StatusByte == MidiMessage.Type.MidiPort)
        {
            AssignMidiPort(trackNum, e.Data[0]);
            return;
        }

        // Only process voice + system exclusive messages
        if (!e.StatusByte.InRange(
                MidiMessage.Type.NoteOff,
                MidiMessage.Type.SystemExclusive))
            return;

        var status = e.StatusByte.Status;
        var midiChannel = e.StatusByte.Channel;
        var channel = midiChannel + portOffset;
        // Clear channel?
        if (e.StatusByte != MidiMessage.Type.SystemExclusive &&
            _clearedChannels.Contains(channel))
        {
            DeleteCurrentEvent();
            return;
        }
            
        var channelStatus = _channelStatuses[channel];
        ChannelModification? channelChange = null;
        {
            if (_channelChanges.TryGetValue(channel, out var val))
                channelChange = val;
        }

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (MidiMessage.TypeOf(status))
        {
            case MidiMessage.Type.NoteOn:
                // Is it first?
                if (channelStatus.IsFirstNoteOn) 
                {
                    FirstNoteOn(e.Ticks, channel);
                    channelStatus.IsFirstNoteOn = false;
                }
                // Transpose key (for zero it won't change anyway)
                e.Data.AsSpan()[0] +=
                    (byte)(channelStatus.KeyShift +
                           channelStatus.CurrentKeyShift);
                break;
            case MidiMessage.Type.NoteOff:
                if (channelChange == null) break;
                e.Data.AsSpan()[0] +=
                    (byte)(channelStatus.KeyShift +
                           channelStatus.CurrentKeyShift);
                break;
            case MidiMessage.Type.ProgramChange:
                // Do we delete it?
                if (channelChange?.Patch is not null)
                {
                    // This channel has program change. BEGONE!
                    DeleteCurrentEvent();
                    return;
                }

                break;
            case MidiMessage.Type.PitchWheel:
            {
                // Do we delete it?
                if (channelChange?.MidiParameters?.ContainsKey(
                        ChannelMidiParameter.Type.PitchWheel) is true)
                {
                    // Locked, remove
                    DeleteCurrentEvent();
                }

                break;
            }

            case MidiMessage.Type.ChannelPressure:
            {
                // Do we delete it?
                if (channelChange?.MidiParameters?.ContainsKey(
                        ChannelMidiParameter.Type.Pressure) is true)
                {
                    // Locked, remove
                    DeleteCurrentEvent();
                }

                break;
            }

            case MidiMessage.Type.ControllerChange:
            {
                HandleControllerChange((Midi.CC)e.Data[0], e.Data[1], channel);
                break;
            }

            case MidiMessage.Type.SystemExclusive:
                foreach (var syx in MidiUtils.AnalyzeSysEx(e))
                {
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (syx.MType)
                    {
                        case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                            when syx.AsAnalyzedParameter?.MType ==
                                 MidiUtils.AnalyzedParameter.Type.DrumSetup:
                            // Drum setup
                            if (_clearDrumParams)
                            {
                                DeleteCurrentEvent();
                                return;
                            }

                            break;

                        case MidiUtils.AnalyzedMessage.Type.ReverbParam:
                            // Delete all reverb params since we're setting new ones
                            if (_reverbParams != null)
                            {

                                DeleteCurrentEvent();
                                return;
                            }

                            break;

                        case MidiUtils.AnalyzedMessage.Type.ChorusParam:
                            // Delete all chorus params since we're setting new ones
                            if (_chorusParams != null)
                            {
                                DeleteCurrentEvent();
                                return;
                            }

                            break;

                        case MidiUtils.AnalyzedMessage.Type.DelayParam:
                            // Delete all delay params since we're setting new ones
                            if (_delayParams != null)
                            {
                                DeleteCurrentEvent();
                                return;
                            }

                            break;

                        case MidiUtils.AnalyzedMessage.Type.InsertionParam:
                            // Delete all insertion params since we're setting new ones
                            if (_insertionParams != null)
                            {
                                DeleteCurrentEvent();
                                return;
                            }

                            break;

                        case MidiUtils.AnalyzedMessage.Type.ProgramChange:
                        {
                            // SysEx can change programs
                            // Do we delete it?
                            var pc = syx.AsProgramChange!.Value;
                            if (_channelChanges.GetValueOrDefault(
                                    pc.Channel + portOffset)?.Patch is not null)
                                // This channel has program change. BEGONE!
                            {
                                DeleteCurrentEvent();
                                return;
                            }

                            break;
                        }

                        case MidiUtils.AnalyzedMessage.Type.GlobalMidiParameter:
                        {
                            var gmp = syx.AsGlobalMidiParameter!.Value;

                            if (_midiParams?.ContainsKey(gmp.PType) is true)
                            {
                                // Locked, remove
                                DeleteCurrentEvent();
                                return;
                            }

                            if (gmp.PType == GlobalMidiParameter.Type.System)
                            {
                                HandleReset(gmp.AsMidiSystem);
                                return;
                            }

                            break;
                        }

                        case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                            when syx.AsAnalyzedParameter?.MType ==
                                 MidiUtils.AnalyzedParameter.Type.ChannelMidiParameter:
                        {
                            var cmp = syx
                                .AsAnalyzedParameter!.Value
                                .AsChannelMidiParameter!.Value;
                            HandleChannelMIDIParam(
                                cmp.Channel + portOffset,
                                cmp.Param);
                            break;
                        }

                        case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                            when syx.AsAnalyzedParameter?.MType ==
                                 MidiUtils.AnalyzedParameter.Type.ControllerChange:
                        {
                            // SysEx can change controllers too!
                            var cc = syx
                                .AsAnalyzedParameter!.Value
                                .AsControllerChange!.Value;
                            HandleControllerChange(
                                cc.Controller, cc.Value, cc.Channel + portOffset);
                            break;
                        }
                        
                        case MidiUtils.AnalyzedMessage.Type.UserDrumSetup:
                        {
                            var uds = syx.AsUserDrumSetup!.Value;

                            var udsParams = _userDrumSetParams?
                                .GetValueOrDefault(uds.DrumSet);
                            if (udsParams == null) return;

                            // Clear whole drum set?
                            if (udsParams.IsClear()) 
                            {
                                // BEGONE!
                                DeleteCurrentEvent();
                                return;
                            }

                            var noteParams =
                                udsParams.AsReplace()!.Value.Mods.
                                    GetValueOrDefault(uds.MidiNote);
                            // Clear this note?
                            if (noteParams?.IsClear() is true)
                            {
                                // BEGONE!
                                DeleteCurrentEvent();
                                return;
                            }

                            // Clear this parameter on this note?
                            // Either clear or set value clears it
                            if (noteParams?.AsReplace()?.Value
                                    .ContainsKey(uds.Parameter.Type) is true)
                            {
                                // BEGONE!
                                DeleteCurrentEvent();
                                return;
                            }

                            break;
                        }
                    }
                }
                return;
        } // End of giga switch
    }

    private void HandleChannelMIDIParam(
        int channel, ChannelMidiParameter param)
    {
        var channelStatus = _channelStatuses[channel];
        var channelChange = _channelChanges
            .GetValueOrDefault(channel);
        if (channelChange is null) return;

        if (param.PType == ChannelMidiParameter.Type.FineTune &&
            channelStatus.FineTune != 0)
        {
            channelStatus.CurrentFineTune =
                param.AsFloat;
            // Add the relative fine tune to the existing one
            var newTune =
                channelStatus.FineTune +
                param.AsFloat;

            channelStatus.CurrentKeyShift =
                (int)Math.Truncate(newTune / 100);
            var targetTune = newTune % 100;

            SpessaLog.Info(
                $"Fine tuning already present on {channel} ({param.AsFloat}), " +
                $"new relative tune: {newTune} cents. Key shift: {channelStatus.CurrentKeyShift} semitones. " +
                $"Actual RPN value to set: {targetTune} cents.");

            // And update this tuning
            var index = _eventIndexes[_trackNum];
            var e = _midi.Tracks[_trackNum].Events[index];

            DeleteCurrentEvent();
            
            // Don't update tuning if no notes have played.
            if (channelStatus.IsFirstNoteOn) return;

            // And update this tuning
            AddEventsBefore(
                    MidiUtils.Set(
                    e.Ticks,
                    channel % 16,
                    _system,
                    (ChannelMidiParameter.Type.FineTune, targetTune))
                );
        }
        else if (
            channelChange?.MidiParameters
                ?.ContainsKey(param.PType) is true)
        {
            // Locked, remove
            // We don't remove fineTune because we can adjust it relatively
            DeleteCurrentEvent();
        }
    }
    
    private void HandleControllerChange(
        Midi.CC ccNum, int value, int channel)
    {
        // Change may be undefined but don't check, because we may encounter a "clear Drum param" request while the channel is not changed
        // This still involves removing the drum NRPN
        // Also param tracking
        var channelChange = _channelChanges.GetValueOrDefault(channel);
        var channelStatus = _channelStatuses[channel];

        var index = _eventIndexes[_trackNum];
        var change = channelChange?.Controllers
            ?.GetValueOrDefault(ccNum);
        if (change != null) 
        {
            // This controller is locked, BEGONE CHANGE!
            DeleteCurrentEvent();
            return;
        }

        switch (ccNum)
        {
            case Midi.CC.BankSelect:
            case Midi.CC.BankSelectLSB:
                if (channelChange?.Patch is not null)
                {
                    // BEGONE!
                    DeleteCurrentEvent();
                }

                return;
            
            case Midi.CC.RegisteredParameterLSB:
            case Midi.CC.RegisteredParameterMSB:
            case Midi.CC.NonRegisteredParameterMSB:
            case Midi.CC.NonRegisteredParameterLSB:
                // Flag the parameter as not cleaned
                channelStatus.ClearedParams =
                    ccNum is 
                        Midi.CC.NonRegisteredParameterLSB or
                        Midi.CC.RegisteredParameterLSB
                        ? channelStatus.ClearedParams with { LSB = false }
                        : channelStatus.ClearedParams with { MSB = false };

                channelStatus.Param.ControllerChange(
                    ccNum, value, _trackNum, index);
                return;

            case Midi.CC.DataEntryMSB:
            case Midi.CC.DataEntryLSB:
            {
                channelStatus.ClearedParams.Data = false;
                
                if (channelStatus.Param.ControllerChange(
                        ccNum, value, _trackNum, index) is not {} data)
                    return;

                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (data.MType)
                {
                    case MidiUtils.AnalyzedParameter.Type.DrumSetup:
                        if (_clearDrumParams)
                        {
                            // Drum param, BEGONE!
                            DeleteCurrentEvent();
                        }
                        return;

                    case MidiUtils.AnalyzedParameter.Type.ControllerChange:
                    {
                        // NRPN can change controllers too!
                        var (cc, val, chan) = 
                            data.AsControllerChange!.Value;
                        HandleControllerChange(cc, val, chan);
                        return;
                    }

                    case MidiUtils.AnalyzedParameter.Type.ChannelMidiParameter
                        when data.AsChannelMidiParameter is var (param, _):

                        HandleChannelMIDIParam(channel, param);
                        break;
                }

                // If the parameters (MSB, LSB and the first data) were cleared.
                // Some MIDIs send param MSB once and then set via LSB only, like:
                // MSB, LSB, Data, LSB, Data,
                // And even though it violates MIDI 1.0, it works...
                // So since we've used those, mark them as "cleaned" so future LSB-only entries won't delete them.
                channelStatus.ClearedParams.LSB = true;
                channelStatus.ClearedParams.MSB = true;
                return;
            }

            default: return;
        }
    }

    private void FirstNoteOn(int ticks, int channel)
    {
        var channelChange = _channelChanges.GetValueOrDefault(channel);
        
        // Make sure that we want to modify this channel at all
        if (channelChange is null) return;
        
        var channelStatus = _channelStatuses[channel];
        var midiChannel = channel % 16;
        
        // All right, so this is the first note on for this channel
        // The order is:
        // - patch selection
        // - relative fine tune
        // - controllers
        // - parameters
        
        // Program change
        if (channelChange.Patch?.AsReplace() is { Value: var patch })
        {
            SpessaLog.Info(
                $"Setting {channel} to {
                    patch.ToMidiString()}. Track num: {_trackNum}");

            var desiredBankMSB = patch.BankMSB;
            var desiredBankLSB = patch.BankLSB;
            var desiredProgram = patch.Program;
            
            // The output event order is: drums -> msb -> lsb -> program change
            if (
                patch.IsGMGSDrum &&
                (_system is null || 
                 !BankSelectHacks.IsSystemXG(_system.Value)) &&
                midiChannel != Synthesizer.Synthesizer.DEFAULT_PERCUSSION)
            {
                // Add gs drum change first
                SpessaLog.Info(
                    $"Adding GS Drum change on track {_trackNum}");
                AddEventsBefore(MidiUtils.Set(
                    ticks, midiChannel, Midi.System.GS,
                    ChannelMidiParameter.DrumMap(1)));
            }
            
            if (_system is {} system &&
                BankSelectHacks.IsSystemXG(system) &&
                patch.IsGMGSDrum)
            {
                // Best I can do is XG drums
                SpessaLog.Info(
                    $"Adding XG Drum change on track {_trackNum}");
                desiredBankMSB =
                    BankSelectHacks.GetDrumBank(system);
                desiredBankLSB = 0;
            }
            
            // Add bank change (MSB first)
            AddBank(false, desiredBankMSB);
            AddBank(true, desiredBankLSB);
            
            // Add program change
            var programChange = MidiMessage.ProgramChange(
                ticks, midiChannel, desiredProgram);
            AddEventsBefore(programChange);
            
            void AddBank(bool isLSB, int v)
            {
                var bankChange = MidiMessage.ControllerChange(
                    ticks,
                    midiChannel,
                    isLSB
                        ? Midi.CC.BankSelectLSB
                        : Midi.CC.BankSelect,
                    v);
                AddEventsBefore(bankChange);
            }
        }
        
        // Apply relative tuning (`fineTune`)
        if (channelChange.MidiParameters?.GetValueOrDefault(
                ChannelMidiParameter.Type.FineTune)?.AsReplace() is
            ({ AsFloat: var ft }) _)
        {
            // Add the relative tuning to the absolute MIDI param
            var newTune = channelStatus.FineTune + ft;
            SetParamFineTune(newTune);
        } 
        else if (channelStatus.FineTune != 0)
        {
            // Make the relative tuning be set in MIDI parameters
            var newTune =
                channelStatus.FineTune +
                channelStatus.CurrentFineTune;
            channelChange.MidiParameters ??= [];
            SetParamFineTune(newTune);
        }
        
        // Add controllers
        foreach (var (cc, v) in channelChange.Controllers ?? []) 
        {
            if (v.AsReplace() is not {Value: var value}) continue;
            var ccChange = MidiMessage.ControllerChange(
                ticks, midiChannel, cc, value);
            AddEventsBefore(ccChange);
        }
            
        // Add MIDI parameters
        foreach (var mpEntry in channelChange.MidiParameters ?? [])
        {
            if (mpEntry.Value.AsReplace() is not {Value: var value})
                continue;
            AddEventsBefore(MidiUtils.Set(
                ticks, midiChannel, _system, value));
        }

        return;

        void SetParamFineTune(float newTune)
        {
            channelStatus.CurrentKeyShift = (int)(newTune / 100);
            channelChange.Set(
                ChannelMidiParameter.FineTune(newTune % 100));
        }
    }

    private void HandleReset(Midi.System system)
    {
        if (system == Midi.System.GM)
        {
            SpessaLog.Info("GM on detected, removing!");
            DeleteCurrentEvent();
            _addedReset = false;
            return;
        }

        SpessaLog.Info($"{system} system on detected");
        _system = system;
        _addedReset = true; // Flag as true so reset won't get added
        _resetTrack = _trackNum;
        _resetIndex = _eventIndexes[_trackNum];
        
        // Reset NRPN (accuracy + prevent deletion before reset)
        foreach (var ch in _channelStatuses) 
        {
            ch.Param.Reset();
            ch.ClearedParams = (true, true, true);
        }
    }

    private void ApplyResetParams()
    {
        // Check for reset and insert it to ensure that a reset always exists.
        if (!_addedReset &&
            // And only when we add changes, removing them does not warrant the need for a gs reset
            _channelChanges.Values.Any(c =>
            c.Patch is not null and not Parameter<MidiPatch>.Clear))
        {
            // There's no reset, add it on the first track at index 0 (or 1 if track name is first)
            var index = 0;
            if (_midi.Tracks[0].Events[0].StatusByte ==
                MidiMessage.Type.TrackName)
                index++;
            // Add the requested system or GS. Clear breaks everything so we don't care.
            var targetSystem = Midi.System.GS;
            if (_midiParams?.TryGetValue(
                GlobalMidiParameter.Type.System, 
                out var value) is true)
            {
                if (value.AsReplace() is { Value: var param })
                    targetSystem = param.AsMidiSystem;
            }
            _midi.Tracks[0].Add(MidiUtils.Reset(0, targetSystem), index);
            _resetTrack = 0;
            _resetIndex = index;
            _system = targetSystem;
            SpessaLog.Info($"{targetSystem} reset on not detected. Adding it.");
        }

        var targetTicks = Math.Max(0, _midi.FirstNoteOn);
        // Insert right after reset
        var targetTrack = _midi.Tracks[_resetTrack];
        var targetIndex = _resetIndex + 1;
        
        /*
        ---
        MIDI RESET
        Here is the code that inserts all parameters after a reset
        ---
         */
        SpessaLog.Info(
            $"Inserting after reset detected on track {_resetTrack
            } on index {targetIndex}!");
        
        // Add MIDI parameters
        foreach (var ompEntry in _midiParams ?? [])
        {
            if (ompEntry.Key == GlobalMidiParameter.Type.System) 
                continue;
            if (ompEntry.Value is not 
                Parameter<GlobalMidiParameter>.Replace(var value))
                continue;
            
            targetTrack.EventList.InsertRange(
                targetIndex,
                MidiUtils.Set(targetTicks, _system, value));
        }
        
        // Add effects
        if (_reverbParams is
            Parameter<Effect.ReverbProcessorSnapshot>.Replace
                { Value: {} r })
        {
            var m = ReverbAddressMap;
            targetTrack.Add([
                GsMessage(m.Level, r.Level),
                GsMessage(m.PreLowPass, r.PreLowPass),
                GsMessage(m.Character, r.Character),
                GsMessage(m.Time, r.Time),
                GsMessage(m.DelayFeedback, r.DelayFeedback),
                GsMessage(m.PreDelayTime, r.PreDelayTime),
            ], targetIndex);
            
            MidiMessage GsMessage(int a3, int data) =>
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, a3, [(byte)data]);
        }
        
        if (_chorusParams is 
            Parameter<Effect.ChorusProcessorSnapshot>.Replace
                { Value: {} c }) 
        {
            var m = ChorusAddressMap;
            targetTrack.Add([
                GsMessage(m.Level, c.Level),
                GsMessage(m.PreLowPass, c.PreLowPass),
                GsMessage(m.Feedback, c.Feedback),
                GsMessage(m.Delay, c.Delay),
                GsMessage(m.Rate, c.Rate),
                GsMessage(m.Depth, c.Depth),
                GsMessage(m.SendLevelToReverb, c.SendLevelToReverb),
                GsMessage(m.SendLevelToDelay, c.SendLevelToDelay),
            ], targetIndex);
            
            MidiMessage GsMessage(int a3, int data) =>
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, a3, [(byte)data]);
        }
        
        if (_delayParams is 
            Parameter<Effect.DelayProcessorSnapshot>.Replace
                { Value: {} d }) 
        {
            var m = DelayAddressMap;
            targetTrack.Add([
                GsMessage(m.Level, d.Level),
                GsMessage(m.PreLowPass, d.PreLowPass),
                GsMessage(m.TimeCenter, d.TimeCenter),
                GsMessage(m.TimeRatioLeft, d.TimeRatioLeft),
                GsMessage(m.TimeRatioRight, d.TimeRatioRight),
                GsMessage(m.LevelCenter, d.LevelCenter),
                GsMessage(m.LevelLeft, d.LevelLeft),
                GsMessage(m.LevelRight, d.LevelRight),
                GsMessage(m.Feedback, d.Feedback),
                GsMessage(m.SendLevelToReverb, d.SendLevelToReverb),
            ], targetIndex);

            MidiMessage GsMessage(int a3, int data) =>
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, a3, [(byte)data]);
        }

        if (_insertionParams is 
            Parameter<Effect.InsertionProcessorSnapshot>.Replace
                { Value: var ins }) 
        {
            // Params and sends
            for (var param = 0; param < ins.Params.Count; param++)
            {
                var value = ins.Params[param];
                if (value == 255) continue;
                
                targetTrack.Add(
                    MidiUtils.GsMessage(targetTicks, 0x40, 0x03, param + 3, [value]),
                    targetIndex);
            }

            // Last means that it will be first, so the order is:
            // Type
            // Params and sends
            // Channels
            targetTrack.Add([
                MidiUtils.GsMessage(targetTicks, 0x40, 0x03, 0x00,
                    [(byte)(ins.Type >> 8), (byte)(ins.Type & 0x7f)]),
            ], targetIndex);
        }
        
        // User Drum parameters
        if (_userDrumSetParams is not null)
        {
            foreach (var (drumSet, udsParams) in _userDrumSetParams)
            {
                ApplyUserDrumSetChanges(drumSet, udsParams);        
            }
        }

        _midi.Flush();

        return;
        
        void ApplyUserDrumSetChanges(
            int drumSet,
            Parameter<UserDrumModification>? udsParams)
        {
            if (udsParams?.AsReplace() is not
                { Value: var userDrumSetParams }) return;
            
            foreach (var (midiNote, usdParams) in
                     userDrumSetParams.Mods)
            {
                // Note cleared
                if (usdParams.AsReplace() is not (
                    var usdRepl) _)
                    continue;

                foreach (var p in usdRepl)
                {
                    // Parameter cleared
                    if (p.Value.AsReplace() is not { } pRepl)
                        continue;
                    
                    targetTrack.Add(
                        MidiUtils.SetUserDrumParameter(
                            targetTicks, drumSet, midiNote, pRepl.Value),
                        targetIndex);
                }
            }
        }
    }
}