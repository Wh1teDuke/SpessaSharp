using System.Diagnostics;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;


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
public readonly record struct MidiModifyOptions(
    Dictionary<int, ClearableParameter<ChannelModification>>? Channels,
    ClearableParameter<object>? DrumSetupParams,
    Dictionary<
        GlobalMidiParameter.Type,
        ClearableParameter<GlobalMidiParameter>>? MidiParams,
    ClearableParameter<Effect.ReverbProcessorSnapshot>? ReverbParams,
    ClearableParameter<Effect.ChorusProcessorSnapshot>? ChorusParams,
    ClearableParameter<Effect.DelayProcessorSnapshot>? DelayParams,
    ClearableParameter<Effect.InsertionProcessorSnapshot>? InsertionParams);

/// <summary>
/// Represents a value that means "clear this parameter" instead of "replace this parameter with".
/// Essentially:<br/>
/// - Null - no change.<br/>
/// - Clear - clear all changes of this parameter from the MIDI file.<br/>
/// - Replace - clear all changes of this parameter from the MIDI file and add T.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract record ClearableParameter<T>
{
    internal sealed record Null : ClearableParameter<T>;
    internal sealed record Replace(T Value) : ClearableParameter<T>;
    internal sealed record Clear: ClearableParameter<T>;

    public static ClearableParameter<T> OfNull() => new Null();
    public static ClearableParameter<T> OfClear() => new Clear();
    public static ClearableParameter<T> OfReplace(T value) => 
        new Replace(value);
}

/// <summary>
/// </summary>
public sealed class ChannelModification
{
    /// <summary>
    /// All controllers that should be modified for this channel.<br/>
    /// - Key: the MIDI controller number.<br/>
    /// - value:<br/>
    ///   - Clear - all controller changes for this controller are removed.<br/>
    ///   - Int - clear + sets the new controller at the start of the song, effectively locking them to the set value.
    /// </summary>
    public Dictionary<Midi.CC, ClearableParameter<int>>? Controllers;
    
    /// <summary>
    /// The new program of this channel.<br/>
    /// - Clear - all program changes for this channel are removed.<br/>
    /// - MidiPatch - clear + sets the new patch according to the MIDI system at the start of the sequence.
    /// </summary>
    public ClearableParameter<MidiPatch>? Patch;

    /// <summary>
    /// The new MIDI parameters of this channel.
    /// <list type="bullet">
    /// <item><description>Key: the MIDI parameter name.</description></item>
    /// <item><description>value:
    /// <list type="bullet">
    /// <item><description>Clear - all changes for this parameter are removed.</description></item>
    /// <item><description>Specific Value - clear + sets the new parameter at the start of the song, effectively locking them to the set value.</description></item>
    /// </list>
    /// </description></item>
    /// </list>
    /// </summary>
    public Dictionary<
        ChannelMidiParameter.Type,
        ClearableParameter<ChannelMidiParameter>>? MidiParameters;

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
}

internal static class MidiEditor
{
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

    /// <summary> Internal tracking interface </summary>
    public sealed class ChannelStatus
    {
        /// <summary>Tracks if the channel already had its first note on</summary>
        public bool IsFirstNoteOn;
        
        public ParamTracker Param;
        /// <summary>
        /// Some MIDIs send param MSB once and then set via LSB only, like:<br/>
        /// MSB,<br/>
        /// LSB,<br/>
        /// Data,<br/>
        /// LSB,<br/>
        /// Data,<br/>
        /// And even though it violates MIDI 1.0, it works...
        /// </summary>
        public (bool LSB, bool MSB, bool Data) ClearedParams;
        public int KeyShift;
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
    
    /// <summary>
    /// Allows easy editing of the file by removing channels, changing programs,
    /// changing controllers and transposing channels. Note that this modifies the MIDI in-place.
    /// </summary>
    /// <param name="midi"></param>
    /// <param name="opts"></param>
    public static void Modify(Midi midi, MidiModifyOptions opts)
    {
        Debug.WriteLine("Applying changes to the MIDI file...");
        
        Debug.WriteLine($"Desired channel changes: {opts.Channels}");
        Debug.WriteLine($"Desired reverb parameters: {opts.ReverbParams}");
        Debug.WriteLine($"Desired chorus parameters: {opts.ChorusParams}");
        Debug.WriteLine($"Desired delay parameters: {opts.DelayParams}");
        Debug.WriteLine($"Desired insertion parameters: {opts.InsertionParams}");
        
        // Optimizations
        var clearDrumParams = opts.DrumSetupParams is ClearableParameter<object>.Clear;
        // Track only channels to clear
        var clearedChannels = new HashSet<int>();
        // Track only channels to change here
        var channelChanges = new Dictionary<int, ChannelModification>();

        if (opts.Channels is { } channels)
        {
            foreach (var (channel, ch) in channels)
            {
                if (ch is ClearableParameter<ChannelModification>.Clear)
                    clearedChannels.Add(channel);
                else
                    channelChanges[channel] = 
                        ((ClearableParameter<
                            ChannelModification>.Replace)ch).Value;
            }
        }
        
        // Go through all events one by one
        Midi.System? system = Midi.System.GS;
        if (opts.MidiParams?.TryGetValue(
            GlobalMidiParameter.Type.MidiSystem, out var parameter) ?? false)
        {
            system = parameter switch
            {
                ClearableParameter<GlobalMidiParameter>.Clear =>
                    null,
                ClearableParameter<GlobalMidiParameter>.Replace replace =>
                    replace.Value.AsMidiSystem,
                _ => system
            };
        }

        var addedReset = false;
        // Track reset position to insert effects right after
        var resetTrack = 0;
        var resetIndex = 0;
        
        // It copies midiPorts everywhere else, but here 0 works so DO NOT CHANGE!
        // > MIDI port number for the corresponding track
        var midiPorts = midi.Tracks.Select(t => t.Port).ToList();
        // > MIDI port: channel offset
        var midiPortChannelOffsets = new Dictionary<int, int>();
        var midiPortChannelOffset = 0;
        
        // Assign port offsets
        for (var i = 0; i < midi.Tracks.Count; i++)
            AssignMidiPort(i, midi.Tracks[i].Port);
        
        var channelsAmount = midiPortChannelOffset;
        var channelStatus = new ChannelStatus[channelsAmount];

        for (var i = 0; i < channelsAmount; i++)
        {
            var keyShift = channelChanges.GetValueOrDefault(i)?.KeyShift ?? 0;
            var fineTune = channelChanges.GetValueOrDefault(i)?.FineTune ?? 0;

            channelStatus[i] = new ChannelStatus
            {
                IsFirstNoteOn = true,
                Param = new ParamTracker(i),
                ClearedParams = (true, true, true),
                KeyShift = keyShift,
                FineTune = fineTune,
                CurrentKeyShift = 0,
                CurrentFineTune = 0,
            };
        }

        foreach (var entry in midi.Iterate())
        {
            ref var e = ref entry.Message;
            var trackNum = entry.TrackNum;
            var eventIndexes = entry.EventIndexes;

            var track = midi.Tracks[trackNum];
            var index = eventIndexes[trackNum];

            var portOffset = midiPortChannelOffsets.GetValueOrDefault(
                midiPorts[trackNum], 0);
            if (e.StatusByte.Is(MidiMessage.Type.MidiPort))
            {
                AssignMidiPort(trackNum, e.Data[0]);
                continue;
            }
            
            // Only process voice + system exclusive messages
            if (!e.StatusByte.InRange(
                    MidiMessage.Type.NoteOff,
                    MidiMessage.Type.SystemExclusive))
                continue;

            var status = e.StatusByte.Status;
            var midiChannel = e.StatusByte.Channel;
            var channel = midiChannel + portOffset;
            // Clear channel?
            if (!e.StatusByte.Is(MidiMessage.Type.SystemExclusive) &&
                clearedChannels.Contains(channel))
            {
                DeleteThisEvent();
                continue;
            }
            
            var chanStatus = channelStatus[channel];
            ChannelModification? optChanChange = null;
            if (channelChanges.TryGetValue(channel, out var val))
                optChanChange = val;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (MidiMessage.TypeOf(status))
            {
                case MidiMessage.Type.NoteOn:
                {
                    // Make sure that we want to modify this channel at all
                    if (optChanChange is not { } chanChange) break;

                    // Is it first?
                    if (chanStatus.IsFirstNoteOn)
                    {
                        chanStatus.IsFirstNoteOn = false;
                        // All right, so this is the first note on for this channel
                        // Order is effectively reversed since we're adding events before

                        // First: controllers
                        // Because FSMP does not like program changes after cc changes in embedded midis
                        // And since we use splice,
                        // Controllers get added first, then programs before them.
                        // Now add controllers
                        if (chanChange.Controllers is { } controllers)
                        {
                            foreach (var (cc, v) in controllers)
                            {
                                if (v is not ClearableParameter<int>.Replace repl)
                                    continue;

                                var ccChange = MidiMessage.ControllerChange(
                                    e.Ticks, midiChannel, cc, repl.Value);
                                AddEventBefore(ccChange);
                            }
                        }
                        
                        // Apply relative tuning (`fineTune`)
                        if (chanChange.MidiParameters is {} midiParameters &&
                            midiParameters.TryGetValue(
                            ChannelMidiParameter.Type.FineTune, 
                            out var clearableParameter) &&
                            clearableParameter is
                            ClearableParameter<ChannelMidiParameter>.Replace replace)
                        {
                            // Add the relative tuning to the absolute MIDI param
                            var newTune =
                                chanStatus.FineTune +
                                replace.Value.AsFloat;
                            chanStatus.CurrentKeyShift = (int)Math.Truncate(
                                newTune / 100);
                            midiParameters[ChannelMidiParameter.Type.FineTune] = 
                                ClearableParameter<ChannelMidiParameter>.OfReplace(
                                    (ChannelMidiParameter.Type.FineTune, newTune % 100));
                        } 
                        else 
                        {
                            // Make the relative tuning be set in MIDI parameters
                            var newTune =
                                chanStatus.FineTune +
                                chanStatus.CurrentFineTune;
                            chanStatus.CurrentKeyShift = (int)Math.Truncate(
                                newTune / 100);

                            var midiParams =
                                chanChange.MidiParameters ?? [];
                            midiParams[ChannelMidiParameter.Type.FineTune] = 
                                ClearableParameter<ChannelMidiParameter>.OfReplace(
                                    (ChannelMidiParameter.Type.FineTune, newTune % 100));
                            chanChange.MidiParameters = midiParams;
                        }

                        // Program change
                        if (chanChange.Patch is
                            ClearableParameter<MidiPatch>.Replace
                            { Value: var patch })
                        {
                            Debug.WriteLine(
                                $"Setting {channel} to {patch.ToMidiString()}. Track num: {trackNum}");

                            // Note: this is in reverse.
                            // The output event order is: drums -> lsb -> msb -> program change
                            var desiredBankMSB = patch.BankMSB;
                            var desiredBankLSB = patch.BankLSB;
                            var desiredProgram = patch.Program;

                            // Add program change
                            var programChange = MidiMessage.ProgramChange(
                                e.Ticks, midiChannel, desiredProgram);

                            AddEventBefore(programChange);

                            if (system is {} sys && BankSelectHacks.IsSystemXG(sys) &&
                                patch.IsGMGSDrum)
                            {
                                // Best I can do is XG drums
                                SpessaLog.Info(
                                    $"Adding XG Drum change on track {trackNum}");
                                desiredBankMSB =
                                    BankSelectHacks.GetDrumBank(sys);
                                desiredBankLSB = 0;
                            }

                            // Add bank change
                            var eTicks = e.Ticks;
                            AddBank(false, desiredBankMSB);
                            AddBank(true, desiredBankLSB);

                            if (
                                patch.IsGMGSDrum &&
                                (system is not {} sys1 || !BankSelectHacks.IsSystemXG(sys1)) &&
                                midiChannel != Synthesizer.Synthesizer.DEFAULT_PERCUSSION)
                            {
                                // Add gs drum change
                                SpessaLog.Info(
                                    $"Adding GS Drum change on track {trackNum}");
                                var chanAddress =
                                    0x10 | MidiUtils.ChannelToSyx(midiChannel);
                                AddEventBefore(MidiUtils.GsMessage(
                                    e.Ticks, 40, chanAddress, 0x15, [1]));
                            }

                            if (chanChange.MidiParameters is {} midiParams)
                            {
                                foreach (var mpEntry in midiParams)
                                {
                                    if (mpEntry.Value is not ClearableParameter<
                                        ChannelMidiParameter>.Replace repl)
                                        continue;
                                    AddEventsBefore(MidiUtils.Set(
                                        e.Ticks,
                                        midiChannel,
                                        system ?? Midi.System.GM,
                                        repl.Value));
                                }
                            }

                            void AddBank(bool isLSB, int v)
                            {
                                var bankChange = MidiMessage.ControllerChange(
                                    eTicks,
                                    midiChannel,
                                    isLSB
                                        ? Midi.CC.BankSelectLSB
                                        : Midi.CC.BankSelect,
                                    v);
                                AddEventBefore(bankChange);
                            }
                        }
                    }

                    // Transpose key (for zero it won't change anyway)
                    {
                        var eData = e.Data;
                        eData[0] += (byte)(
                            chanStatus.KeyShift + chanStatus.CurrentKeyShift);
                    }
                    break;
                }

                case MidiMessage.Type.NoteOff:
                {
                    var eData = e.Data;
                    if (optChanChange is null) break;
                    eData[0] += (byte)(
                        chanStatus.KeyShift + chanStatus.CurrentKeyShift);
                    break;
                }

                case MidiMessage.Type.ProgramChange:
                {
                    // Do we delete it?
                    if (optChanChange?.Patch is not null)
                    {
                        // This channel has program change. BEGONE!
                        DeleteThisEvent();
                        goto Continue;
                    }
                    break;
                }

                case MidiMessage.Type.PitchWheel:
                {
                    // Do we delete it?
                    if (optChanChange?.MidiParameters?.ContainsKey(
                        ChannelMidiParameter.Type.PitchWheel) ?? false)
                    {
                        // Locked, remove
                        DeleteThisEvent();
                    }
                    break;
                }
                
                case MidiMessage.Type.ChannelPressure:
                {
                    // Do we delete it?
                    if (optChanChange?.MidiParameters?.ContainsKey(
                        ChannelMidiParameter.Type.Pressure) ?? false)
                    {
                        // Locked, remove
                        DeleteThisEvent();
                    }
                    break;
                }

                case MidiMessage.Type.ControllerChange:
                    var ccNum = (Midi.CC)e.Data[0];
                    var value = e.Data[1];
                    if (optChanChange?.Controllers?.ContainsKey(ccNum) is true)
                    {
                        // This controller is locked, BEGONE CHANGE!
                        DeleteThisEvent();
                        goto Continue;
                    }

                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (ccNum)
                    {
                        case Midi.CC.BankSelect:
                        case Midi.CC.BankSelectLSB:
                            if (optChanChange?.Patch is not null)
                            {
                                // BEGONE!
                                DeleteThisEvent();
                                goto Continue;
                            }
                            break;
                        
                        case Midi.CC.RegisteredParameterLSB:
                        case Midi.CC.RegisteredParameterMSB:
                        case Midi.CC.NonRegisteredParameterMSB:
                        case Midi.CC.NonRegisteredParameterLSB:
                            // Flag the parameter as not cleaned
                            chanStatus.ClearedParams =
                                ccNum is Midi.CC.NonRegisteredParameterLSB
                                or Midi.CC.RegisteredParameterLSB
                                ? chanStatus.ClearedParams with { LSB = false }
                                : chanStatus.ClearedParams with { MSB = false };

                            chanStatus.Param.ControllerChange(
                                ccNum, value, trackNum, index);
                            goto Continue;

                        // NRPN we care about only uses MSB
                        case Midi.CC.DataEntryMSB:
                        case Midi.CC.DataEntryLSB:
                            chanStatus.ClearedParams = 
                                chanStatus.ClearedParams with { Data = false };
                            var optData = chanStatus.Param.ControllerChange(
                                ccNum, value, trackNum, index);
                            
                            if (optData is not {} data)
                                goto Continue;

                            // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                            switch (data.MType)
                            {
                                case MidiUtils.AnalyzedParameter.Type.DrumSetup:
                                    if (clearDrumParams)
                                    {
                                        // Drum param, BEGONE!
                                        DeleteParameter(channel);
                                    }
                                    goto Continue;

                                case MidiUtils.AnalyzedParameter.Type.ControllerChange:
                                {
                                    // NRPN can change controllers too!
                                    var (cc, _, chan) = 
                                        data.AsControllerChange!.Value;

                                    if (optChanChange?.Controllers?.ContainsKey(ccNum) is true)
                                    {
                                        // This controller is locked, BEGONE CHANGE!
                                        DeleteParameter(chan);
                                        goto Continue;
                                    }
                                    
                                    if (cc is Midi.CC.BankSelect
                                        or Midi.CC.BankSelectLSB &&
                                       optChanChange?.Patch is not null) 
                                    {
                                        // BEGONE!
                                        DeleteParameter(chan);
                                    }

                                    break;
                                }

                                case MidiUtils.AnalyzedParameter.Type.ChannelMidiParameter:
                                    var cmp =
                                        data.AsChannelMidiParameter!.Value;
                                    
                                    if (cmp.Param.PType == ChannelMidiParameter.Type.FineTune &&
                                        chanStatus.FineTune != 0)
                                    {
                                        chanStatus.CurrentFineTune = 
                                            cmp.Param.AsFloat;
                                        // Add the relative fine tune to the existing one
                                        var newTune =
                                            chanStatus.FineTune + cmp.Param.AsFloat;

                                        chanStatus.CurrentKeyShift =
                                            (int)Math.Truncate(newTune / 100);
                                        var targetTune = newTune % 100;

                                        SpessaLog.Info(
                                            $"Fine tuning already present on {channel} ({cmp.Param.AsFloat}), " +
                                            $"new relative tune: {newTune} cents. Key shift: {chanStatus.CurrentKeyShift} semitones. " +
                                            $"Actual RPN value to set: {targetTune} cents.");
                                        
                                        // And update this tuning
                                        // This event is either data MSB or LSB, so update appropriately
                                        var updatedData =
                                            (int)float.Floor(targetTune * 81.92f) +
                                            8_192;

                                        var eData = e.Data;
                                        eData[1] = (byte)(
                                            ccNum == Midi.CC.DataEntryMSB
                                                ? updatedData >> 7
                                                : updatedData & 0x7f);
                                    } 
                                    else if (
                                        optChanChange?.MidiParameters
                                        ?.ContainsKey(cmp.Param.PType) ?? false)
                                    {
                                        // Locked, remove
                                        // We don't remove fineTune because we can adjust it relatively
                                        DeleteParameter(channel);
                                    }

                                    goto Continue;
                            }

                            // If the parameters (MSB, LSB and the first data) were cleared.
                            // Some MIDIs send param MSB once and then set via LSB only, like:
                            // MSB,
                            // LSB,
                            // Data,
                            // LSB,
                            // Data,
                            // And even though it violates MIDI 1.0, it works...
                            // So since we've used those, mark them as "cleaned" so future LSB-only entries won't delete them.
                            chanStatus.ClearedParams = chanStatus
                                .ClearedParams with { LSB = true, MSB = true };
                            goto Continue;
                        
                        default: goto Continue;
                    }

                    break;
                
                case MidiMessage.Type.SystemExclusive:
                    var syx = MidiUtils.AnalyzeSysEx(e);

                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (syx.MType)
                    {
                        default: break;
                        
                        case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                            when syx.AsAnalyzedParameter?.MType ==
                                 MidiUtils.AnalyzedParameter.Type.DrumSetup:
                            // Drum setup
                            if (clearDrumParams) DeleteThisEvent();
                            goto Continue;

                        case MidiUtils.AnalyzedMessage.Type.ReverbParam:
                            // Delete all reverb params since we're setting new ones
                            if (opts.ReverbParams != null) DeleteThisEvent();
                            goto Continue;
                            
                        case MidiUtils.AnalyzedMessage.Type.ChorusParam:
                            // Delete all chorus params since we're setting new ones
                            if (opts.ChorusParams != null) DeleteThisEvent();
                            goto Continue;
                            
                        case MidiUtils.AnalyzedMessage.Type.DelayParam:
                            // Delete all delay params since we're setting new ones
                            if (opts.DelayParams != null) DeleteThisEvent();
                            goto Continue;
                            
                        case MidiUtils.AnalyzedMessage.Type.InsertionParam:
                            // Delete all insertion params since we're setting new ones
                            if (opts.InsertionParams != null) DeleteThisEvent();
                            goto Continue;
                                                    
                        case MidiUtils.AnalyzedMessage.Type.ProgramChange:
                        {
                            // SysEx can change programs
                            // Do we delete it?
                            var pc = syx.AsProgramChange!.Value;
                            if (channelChanges.TryGetValue(
                                    pc.Channel + portOffset, out var cMod) &&
                                cMod.Patch is not null)
                                // This channel has program change. BEGONE!
                                DeleteThisEvent();
                            goto Continue;
                        }

                        case MidiUtils.AnalyzedMessage.Type.GlobalMidiParameter:
                        {
                            var gmp = syx.AsGlobalMidiParameter!.Value;

                            if (opts.MidiParams?.ContainsKey(gmp.PType) ?? false)
                            {
                                DeleteThisEvent();
                                goto Continue;
                            }
                            
                            if (gmp.PType == GlobalMidiParameter.Type.MidiSystem)
                            {
                                switch (gmp.AsMidiSystem)
                                {
                                    case Midi.System.XG:
                                        SpessaLog.Info("XG system on detected");

                                        system = Midi.System.XG;
                                        addedReset = true; // Flag as true so reset won't get added
                                        resetTrack = trackNum;
                                        resetIndex = index;
                                        // Reset NRPN (accuracy + prevent deletion before reset)
                                        foreach (ref var ch in channelStatus.AsSpan()) 
                                        {
                                            ch.Param.Reset();
                                            ch.ClearedParams = (true, true, true);
                                        }
                                        goto Continue;
                                    case Midi.System.GM2:
                                        SpessaLog.Info("GM2 system on detected");

                                        system = Midi.System.GM2;
                                        addedReset = true; // Flag as true so reset won't get added
                                        resetTrack = trackNum;
                                        resetIndex = index;
                                        // Reset NRPN (accuracy + prevent deletion before reset)
                                        foreach (ref var ch in channelStatus.AsSpan())
                                        {
                                            ch.Param.Reset();
                                            ch.ClearedParams = (true, true, true);
                                        }
                                        goto Continue;
                                    case Midi.System.GS:
                                        // Check for GS on
                                        // That's a GS on, we're done here
                                        SpessaLog.Info("GM2 system on detected");
                            
                                        addedReset = true;
                                        resetTrack = trackNum;
                                        resetIndex = index;
                                        // Reset NRPN (accuracy + prevent deletion before reset)
                                        foreach (ref var ch in channelStatus.AsSpan()) 
                                        {
                                            ch.Param.Reset();
                                            ch.ClearedParams = (true, true, true);
                                        }
                                        goto Continue;
                                    case Midi.System.GM:
                                        // Check for GM on
                                        // That's a GM1 system change, remove it!
                                        SpessaLog.Info("GM on detected, removing!");

                                        DeleteThisEvent();
                                        addedReset = false;
                                        goto Continue;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
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

                            var syxChannel = channelChanges
                                .GetValueOrDefault(cmp.Channel + portOffset);
                            
                            if (syxChannel?.MidiParameters?
                                .ContainsKey(cmp.Param.PType) ?? false)
                            {
                                // Locked, remove
                                DeleteThisEvent();
                                goto Continue;
                            }
                            
                            if (cmp.Param.PType == ChannelMidiParameter.Type.FineTune)
                            {
                                var syxStatus = channelStatus[
                                    cmp.Channel + portOffset];
                            
                                if (syxStatus.IsFirstNoteOn &&
                                    syxChannel != null)
                                {
                                    // No note-on yet. Then use it as relative!
                                    var newTune = 
                                        syxStatus.FineTune + cmp.Param.AsFloat;
                                    syxStatus.CurrentKeyShift += (int)
                                        float.Truncate(newTune / 100);
                                    syxStatus.FineTune = newTune % 100;

                                    SpessaLog.Info(
                                        $"Fine tuning already present on {
                                            channel}, new relative tune: {newTune} cents");
                                    DeleteThisEvent();
                                }

                                break;
                            }

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
                            if (channelChanges.TryGetValue(
                                    cc.Channel + portOffset, out var syxChannel))
                            {
                                if (syxChannel.Controllers?.ContainsKey(cc.Controller) is true)
                                {
                                    // This controller is locked, BEGONE CHANGE!
                                    DeleteThisEvent();
                                    goto Continue;    
                                }
                                if (cc.Controller is Midi.CC.BankSelect or Midi.CC.BankSelectLSB &&
                                    syxChannel.Patch is not null)
                                {
                                    // BEGONE!
                                    DeleteThisEvent();
                                }
                            }

                            goto Continue;
                        }
                    }

                    break;
                
                default: break;
            }
            
            Continue:;
            continue;

            void DeleteThisEvent()
            {
                track.DeleteEvent(index);
                eventIndexes[trackNum]--;
            }

            void AddEventBefore(MidiMessage e)
            {
                track.Add(e, index);
                eventIndexes[trackNum]++;
            }

            /*
             * This function adds the events IN ORDER they are in the array,
             * So the first event in the array will end up as the first one before the current event.
             */
            void AddEventsBefore(MidiMessage[] events)
            {
                for (var i = events.Length - 1; i >= 0; i--)
                    AddEventBefore(events[i]);
            }

            void DeleteParameter(int channel)
            {
                ref var ch = ref channelStatus[channel];
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
                
                SpessaLog.Info(
                    $"Clearing Non/Registered Parameter on {channel}." +
                    $"\n Clear MSB:  {ch.ClearedParams.MSB}," +
                    $"\n Clear LSB:  {ch.ClearedParams.LSB}," +
                    $"\n Clear data: {ch.ClearedParams.Data}.");

                // Delete the current data entry event first.
                // This is safe because it's the event currently being processed in the loop,
                // Meaning its index is always higher than or equal
                // To the cached MSB/LSB (on a different track).
                if (!ch.ClearedParams.Data)
                {
                    DeleteThisEvent();
                    
                    // Shift the events down if they are on the same track (very likely)
                    if (trackNum == msb.Track && index < msb.Event) msb.Event--;
                    if (trackNum == lsb.Track && index < lsb.Event) lsb.Event--;
                }
                
                if (!ch.ClearedParams.MSB) 
                {
                    // Delete data MSB
                    midi.Tracks[msb.Track].DeleteEvent(msb.Event);
                    eventIndexes[msb.Track]--;

                    // Shift the LSB down if they are on the same track (very likely)
                    if (msb.Track == lsb.Track && msb.Event < lsb.Event)
                        lsb.Event--;
                }
                
                if (!ch.ClearedParams.LSB) 
                {
                    // Delete data LSB
                    midi.Tracks[lsb.Track].DeleteEvent(lsb.Event);
                    eventIndexes[lsb.Track]--;
                }

                p.ParamMSB = msb;
                p.ParamLSB = lsb;
                // Flag params as deleted
                ch.ClearedParams = (true, true, true);
                ch.Param = p;
            }
        }

        // Check for reset and insert it to ensure that a reset always exists.
        if (!addedReset &&
            channelChanges.Values.Any(c => 
            c.Patch is not null and not ClearableParameter<MidiPatch>.Clear))
        {
            // There's no reset, add it on the first track at index 0 (or 1 if track name is first)
            var index = 0;
            if (midi.Tracks[0].Events[0].StatusByte.Is(
                    MidiMessage.Type.TrackName))
                index++;
            // Add the requested system or GS. Clear breaks everything so we don't care.
            var targetSystem = Midi.System.GS;
            if (opts.MidiParams?.TryGetValue(
                GlobalMidiParameter.Type.MidiSystem, 
                out var value) ?? false)
            {
                if (value is ClearableParameter<GlobalMidiParameter>.Replace replace)
                    targetSystem = replace.Value.AsMidiSystem;
            }
            midi.Tracks[0].Add(MidiUtils.Reset(0, targetSystem), index);
            resetTrack = 0;
            resetIndex = index;
            system = targetSystem;
            SpessaLog.Info($"{targetSystem} reset on not detected. Adding it.");
        }

        var targetTicks = Math.Max(0, midi.FirstNoteOn);
        // Insert right after reset
        var targetTrack = midi.Tracks[resetTrack];
        var targetIndex = resetIndex + 1;
        
        // Add MIDI parameters
        foreach (var ompEntry in opts.MidiParams ?? [])
        {
            if (ompEntry.Key == GlobalMidiParameter.Type.MidiSystem) 
                continue;
            if (ompEntry.Value is not 
                ClearableParameter<GlobalMidiParameter>.Replace repl)
                continue;
            
            targetTrack.EventList.InsertRange(
                targetIndex,
                MidiUtils.Set(
                    targetTicks, system ?? Midi.System.GM, repl.Value));
        }
        
        if (opts.ReverbParams is 
            ClearableParameter<Effect.ReverbProcessorSnapshot>.Replace
                { Value: {} r }) 
        {
            var m = ReverbAddressMap;
            targetTrack.Add([
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Level, [(byte)r.Level]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.PreLowPass, [(byte)r.PreLowPass]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Character, [(byte)r.Character]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Time, [(byte)r.Time]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.DelayFeedback, [(byte)r.DelayFeedback]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.PreDelayTime, [(byte)r.PreDelayTime]),
                ], targetIndex);
        }
        
        if (opts.ChorusParams is 
            ClearableParameter<Effect.ChorusProcessorSnapshot>.Replace
                { Value: {} c }) 
        {
            var m = ChorusAddressMap;
            targetTrack.Add([
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Level, [(byte)c.Level]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.PreLowPass, [(byte)c.PreLowPass]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Feedback, [(byte)c.Feedback]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Delay, [(byte)c.Delay]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Rate, [(byte)c.Rate]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Depth, [(byte)c.Depth]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.SendLevelToReverb, [(byte)c.SendLevelToReverb]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.SendLevelToDelay, [(byte)c.SendLevelToDelay]),
            ], targetIndex);
        }
        
        if (opts.DelayParams is 
            ClearableParameter<Effect.DelayProcessorSnapshot>.Replace
                { Value: {} d }) 
        {
            var m = DelayAddressMap;
            targetTrack.Add([
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Level, [(byte)d.Level]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.PreLowPass, [(byte)d.PreLowPass]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.TimeCenter, [(byte)d.TimeCenter]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.TimeRatioLeft, [(byte)d.TimeRatioLeft]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.TimeRatioRight, [(byte)d.TimeRatioRight]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.LevelCenter, [(byte)d.LevelCenter]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.LevelLeft, [(byte)d.LevelLeft]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.LevelRight, [(byte)d.LevelRight]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.Feedback, [(byte)d.Feedback]),
                MidiUtils.GsMessage(targetTicks, 0x40, 0x01, m.SendLevelToReverb, [(byte)d.SendLevelToReverb]),
            ], targetIndex);
        }

        if (opts.InsertionParams is 
            ClearableParameter<Effect.InsertionProcessorSnapshot>.Replace
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

        midi.Flush();
        return;

        void AssignMidiPort(int trackNum, int port)
        {
            // Midi port: channel offset
            if (midi.Tracks[trackNum].Channels.Count == 0)
                return;
            
            // Assign new 16 channels if the port is not occupied yet
            if (midiPortChannelOffset == 0) 
            {
                midiPortChannelOffset += 16;
                midiPortChannelOffsets[port] = 0;
            }
            
            if (midiPortChannelOffsets.TryAdd(port, midiPortChannelOffset))
                midiPortChannelOffset += 16;
            
            midiPorts[trackNum] = port;
        }
    }
}