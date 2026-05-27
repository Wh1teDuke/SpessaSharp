using System.Diagnostics;
using SpessaSharp.Synthesizer.Engine.Effects;
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
/// <param name="Controllers">
/// All controllers that should be modified for this channel.<br/>
/// - Key: the MIDI controller number.<br/>
/// - value:<br/>
///   - Clear - all controller changes for this controller are removed.<br/>
///   - Int - clear + sets the new controller at the start of the song, effectively locking them to the set value.
/// </param>
/// <param name="KeyShift">The channel key shift in semitones. Note on/off numbers are shifted.</param>
/// <param name="FineTune">The channel tuning in cents. Tuned using RPN Fine Tune. Range is <b>[-100; 99.986]</b> cents.</param>
/// <param name="Patch">
/// The new program of this channel.<br/>
/// - Clear - all program changes for this channel are removed.<br/>
/// - MidiPatch - clear + sets the new patch according to the MIDI system at the start of the sequence.
/// </param>
public readonly record struct ChannelModification(
    Dictionary<Midi.CC, ClearableParameter<int>>? Controllers,
    ClearableParameter<MidiPatch>? Patch,
    int? KeyShift,
    float? FineTune);

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
    
    private static MidiMessage GetControllerChange(
            int channel, Midi.CC cc, int value, int ticks) => new(
        ticks,
        (byte)(MidiMessage.ID(
            MidiMessage.Type.ControllerChange) | (channel % 16)),
        new []{(byte)cc, (byte)value});
    
    
    /// <summary> Internal tracking interface </summary>
    /// <param name="IsFirstNoteOn">Tracks if the channel already had its first note on</param>
    /// <param name="Param">RPN/NRPN tracking</param>
    /// <param name="ClearedParams">
    /// Some MIDIs send param MSB once and then set via LSB only, like:<br/>
    /// MSB,<br/>
    /// LSB,<br/>
    /// Data,<br/>
    /// LSB,<br/>
    /// Data,<br/>
    /// And even though it violates MIDI 1.0, it works...
    /// </param>
    public record struct ChannelStatus(
        bool IsFirstNoteOn, 
        ParamTracker Param,
        (bool LSB, bool MSB, bool Data) ClearedParams,
        int KeyShift,
        float FineTune);
    
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
        var system = Midi.System.GS;
        var addedGs = false;
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
            var keyShift = channelChanges.GetValueOrDefault(i).KeyShift ?? 0;
            var fineTune = channelChanges.GetValueOrDefault(i).FineTune ?? 0;

            channelStatus[i] = new ChannelStatus(
                IsFirstNoteOn: true,
                Param: new ParamTracker(i),
                ClearedParams: (true, true, true),
                KeyShift: keyShift,
                FineTune: fineTune);
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
            
            ref var chanStatus = ref channelStatus[channel];
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
                        // All right, so this is the first note on

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

                                var ccChange = GetControllerChange(
                                    midiChannel, cc, repl.Value, e.Ticks);
                                AddEventBefore(ccChange);
                            }
                        }

                        // Tuning
                        if (chanStatus.FineTune != 0)
                        {
                            // Add rpn
                            // 64 is the center, 96 = 50 cents up
                            var data = (int)float.Floor(
                                chanStatus.FineTune * 81.92f) + 8_192;
                            var rpnCoarse = GetControllerChange(
                                midiChannel,
                                Midi.CC.RegisteredParameterMSB,
                                0, e.Ticks);
                            var rpnFine = GetControllerChange(
                                midiChannel,
                                Midi.CC.RegisteredParameterLSB,
                                1, e.Ticks);
                            var dataEntryCoarse = GetControllerChange(
                                channel,
                                Midi.CC.DataEntryMSB,
                                (data >> 7) & 0x7f, e.Ticks);
                            var dataEntryFine = GetControllerChange(
                                midiChannel,
                                Midi.CC.DataEntryLSB,
                                data & 0x7f, e.Ticks);

                            AddEventBefore(dataEntryFine);
                            AddEventBefore(dataEntryCoarse);
                            AddEventBefore(rpnFine);
                            AddEventBefore(rpnCoarse);
                        }

                        // Program change
                        if (chanChange.Patch is
                            ClearableParameter<MidiPatch>.Replace
                            {
                                Value: var patch
                            })
                        {
                            Debug.WriteLine(
                                $"Setting {channel} to {patch.ToMidiString()}. Track num: {trackNum}");

                            // Note: this is in reverse.
                            // The output event order is: drums -> lsb -> msb -> program change
                            var desiredBankMSB = patch.BankMSB;
                            var desiredBankLSB = patch.BankLSB;
                            var desiredProgram = patch.Program;

                            // Add program change
                            var programChange = new MidiMessage(
                                e.Ticks,
                                (byte)(MidiMessage.ID(
                                           MidiMessage.Type.ProgramChange)
                                       | midiChannel),
                                new[] { (byte)desiredProgram });

                            AddEventBefore(programChange);

                            if (BankSelectHacks.IsSystemXG(system) &&
                                patch.IsGMGSDrum)
                            {
                                // Best I can do is XG drums
                                Debug.WriteLine(
                                    $"Adding XG Drum change on track {trackNum}");
                                desiredBankMSB =
                                    BankSelectHacks.GetDrumBank(system);
                                desiredBankLSB = 0;
                            }

                            // Add bank change
                            var eTicks = e.Ticks;
                            AddBank(false, desiredBankMSB);
                            AddBank(true, desiredBankLSB);

                            if (
                                patch.IsGMGSDrum &&
                                !BankSelectHacks.IsSystemXG(system) &&
                                midiChannel != Synthesizer.Synthesizer.DEFAULT_PERCUSSION)
                            {
                                // Add gs drum change
                                Debug.WriteLine(
                                    $"Adding GS Drum change on track {trackNum}");
                                AddEventBefore(MidiUtils.GsDrumChange(
                                    e.Ticks, midiChannel, 1));
                            }

                            void AddBank(bool isLSB, int v)
                            {
                                var bankChange = GetControllerChange(
                                    midiChannel,
                                    isLSB
                                        ? Midi.CC.BankSelectLSB
                                        : Midi.CC.BankSelect,
                                    v, eTicks);
                                AddEventBefore(bankChange);
                            }
                        }
                    }

                    // Transpose key (for zero it won't change anyway)
                    {
                        var eData = e.Data;
                        eData[0] += (byte)chanStatus.KeyShift;
                        e = e with { Data = eData };
                    }
                    break;
                }

                case MidiMessage.Type.NoteOff:
                {
                    var eData = e.Data;
                    if (optChanChange is null) break;
                    eData[0] += (byte)chanStatus.KeyShift;
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
                                case MidiUtils.AnalyzedMessage.Type.DrumSetup:
                                    if (clearDrumParams)
                                    {
                                        // Drum param, BEGONE!
                                        DeleteParameter(channel);
                                    }
                                    goto Continue;

                                case MidiUtils.AnalyzedMessage.Type.ControllerChange:
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

                                case MidiUtils.AnalyzedMessage.Type.FineTune:
                                    if (chanStatus.FineTune != 0)
                                    {
                                        if (chanStatus.IsFirstNoteOn)
                                        {
                                            var ft = data.AsFineTune!.Value;
                                            // No note-on yet. Then use it as relative!
                                            var newTune = 
                                                chanStatus.FineTune + ft.Value;
                                            chanStatus.KeyShift +=
                                                (int)float.Truncate(
                                                    newTune / 100);
                                            chanStatus.FineTune =
                                                newTune % 100;

                                            Debug.WriteLine(
                                                $"Fine tuning already present on {
                                                    channel}, new relative tune: {newTune} cents");
                                        }
                                        
                                        // We're tuning it ourselves, BEGONE!
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
                        
                        case MidiUtils.AnalyzedMessage.Type.XgReset:
                            SpessaLog.Info("XG system on detected");

                            system = Midi.System.XG;
                            addedGs = true; // Flag as true so gs won't get added
                            resetTrack = trackNum;
                            resetIndex = index;
                            // Reset NRPN (accuracy + prevent deletion before reset)
                            foreach (ref var ch in channelStatus.AsSpan()) 
                            {
                                ch.Param.Reset();
                                ch.ClearedParams = (true, true, true);
                            }
                            goto Continue;
                        
                        case MidiUtils.AnalyzedMessage.Type.Gm2On:
                            SpessaLog.Info("GM2 system on detected");

                            system = Midi.System.GM2;
                            addedGs = true; // Flag as true so gs won't get added
                            resetTrack = trackNum;
                            resetIndex = index;
                            // Reset NRPN (accuracy + prevent deletion before reset)
                            foreach (ref var ch in channelStatus.AsSpan())
                            {
                                ch.Param.Reset();
                                ch.ClearedParams = (true, true, true);
                            }
                            goto Continue;

                        case MidiUtils.AnalyzedMessage.Type.GsReset:
                            // Check for GS on
                            // That's a GS on, we're done here
                            SpessaLog.Info("GM2 system on detected");
                            
                            addedGs = true;
                            resetTrack = trackNum;
                            resetIndex = index;
                            // Reset NRPN (accuracy + prevent deletion before reset)
                            foreach (ref var ch in channelStatus.AsSpan()) 
                            {
                                ch.Param.Reset();
                                ch.ClearedParams = (true, true, true);
                            }
                            goto Continue;
                        
                        case MidiUtils.AnalyzedMessage.Type.GmOff:
                        case MidiUtils.AnalyzedMessage.Type.GmOn:
                            // Check for GM on
                            // That's a GM1 system change, remove it!
                            SpessaLog.Info("GM on detected, removing!");

                            DeleteThisEvent();
                            addedGs = false;
                            goto Continue;
                        
                        case MidiUtils.AnalyzedMessage.Type.DrumSetup:
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
                        
                        case MidiUtils.AnalyzedMessage.Type.FineTune:
                        {
                            var sft = syx.AsFineTune!.Value;
                            ref var syxStatus = ref channelStatus[
                                sft.Channel + portOffset];
                            
                            if (syxStatus.IsFirstNoteOn && 
                                channelChanges.ContainsKey(sft.Channel + portOffset))
                            {
                                // No note-on yet. Then use it as relative!
                                var newTune = syxStatus.FineTune + sft.Value;
                                syxStatus.KeyShift += (int)float.Truncate(
                                    newTune / 100);
                                syxStatus.FineTune = newTune % 100;

                                Debug.WriteLine(
                                    $"Fine tuning already present on {
                                        channel}, new relative tune: {newTune} cents");
                                DeleteThisEvent();
                            }
                            break;
                        }


                        case MidiUtils.AnalyzedMessage.Type.ControllerChange:
                        {
                            // SysEx can change controllers too!
                            var cc = syx.AsControllerChange!.Value;
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

            void AddEventBefore(MidiMessage e, int offset = 0)
            {
                track.Add(e, index + offset);
                eventIndexes[trackNum]++;
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

        // Check for GS reset and insert it to ensure that a reset always exists.
        if (!addedGs &&
            channelChanges.Values.Any(c => 
            c.Patch is not null and not ClearableParameter<MidiPatch>.Clear))
        {
            // Gs is not on, add it on the first track at index 0 (or 1 if track name is first)
            var index = 0;
            if (midi.Tracks[0].Events[0].StatusByte.Is(
                    MidiMessage.Type.TrackName))
                index++;
            midi.Tracks[0].Add(MidiUtils.GsReset(0), index);
            resetTrack = 0;
            resetIndex = index;
            SpessaLog.Info("GS on not detected. Adding it.");
        }
        
        // Add Effects
        var targetTicks = Math.Max(0, midi.FirstNoteOn);
        // Insert right after reset
        var targetTrack = midi.Tracks[resetTrack];
        var targetIndex = resetIndex + 1;
        
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
            for (var channel = 0; channel < ins.Channels.Count; channel++)
            {
                if (!ins.Channels[channel]) continue;
                targetTrack.Add(
                    MidiUtils.GsMessage(
                        targetTicks, 
                        0x40, 
                        0x40 | MidiUtils.FromChannel(channel), 0x22, [1]),
                    targetTicks);
            }
            
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