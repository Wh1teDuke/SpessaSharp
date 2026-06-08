using System.Collections.Frozen;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Utils;

namespace SpessaSharp.Sequencer;

internal static class SetTime
{
    private static readonly FrozenSet<Midi.CC> NonSkippableCCs = [
        Midi.CC.DataDecrement,
        Midi.CC.DataIncrement,
        Midi.CC.DataEntryMSB,
        Midi.CC.DataEntryLSB,
        Midi.CC.RegisteredParameterLSB,
        Midi.CC.RegisteredParameterMSB,
        Midi.CC.NonRegisteredParameterLSB,
        Midi.CC.NonRegisteredParameterMSB,
        Midi.CC.BankSelect,
        Midi.CC.BankSelectLSB,
        Midi.CC.ResetAllControllers,
        Midi.CC.MonoModeOn,
        Midi.CC.PolyModeOn,
    ];

    /// <summary> </summary>
    /// <param name="Param">NRPN tracking for controller changes</param>
    /// <param name="Controllers">Save controllers and send them only after.</param>
    /// <param name="PortamentoNote">Save portamento notes and send them only after (-1 means no portamento note).</param>
    /// <param name="PitchWheel">Save pitch wheels and send them only after.</param>
    private record struct ChannelStatus(
        ParamTracker Param,
        ArraySegment<short> Controllers,
        int PortamentoNote,
        int PitchWheel);
    
    /// <summary> Plays the MIDI file to a specific time or ticks. </summary>
    /// <param name="seq"></param>
    /// <param name="time">In seconds.</param>
    /// <param name="ticks">Optional MIDI ticks, when given is used instead of time.</param>
    /// <returns>True if the MIDI file is not finished.</returns>
    public static bool To(
        SpessaSharpSequencer seq, double time, int? ticks = null)
    {
        if (seq.Midi == null) return false;

        seq.OneTickToSeconds = 60d / (120 * seq.Midi.TimeDivision);
        // Reset everything

        seq.SendMidiReset();
        seq.PlayedTime = 0;
        seq.Index = 0;

        // We save the pitch wheels, programs and controllers here
        // To only send them once after going through the events
        var channelsToSave = seq.Synth.MidiChannels.Length;
        
        var channels = Util.Rent<ChannelStatus?>(channelsToSave);
        for (var i = 0; i < channelsToSave; i++) 
            channels[i] = NewChanStatus(i);

        // Save tempo changes
        // Testcase:
        // Piano Concerto No. 2 in G minor, Op 16 - I. Cadenza (Ky6000).mid
        // With 46k changes!
        MidiMessage? savedTempo = null;
        var savedTempoTrack = 0;

        var timeline = seq.Midi.Timeline;
        var tracks = seq.Midi.Tracks;
        
        while (true) 
        {
            // Find the next event
            var tl = timeline[seq.Index];
            var trackIndex = tl.Tr;
            var track = tracks[trackIndex];
            var ev = track.Events[tl.Ev];

            if (ticks == null) 
            {
                if (seq.PlayedTime >= time) break;
            } 
            else 
            {
                if (ev.Ticks >= ticks) break;
            }

            // Skip note ons
            MidiMessage.Type status;
            var statusChannel = 0;
            if (ev.StatusByte.Byte is >= 0x80 and < 0xf0) 
            {
                // Voice message
                status = MidiMessage.TypeOf(ev.StatusByte.Status);
                statusChannel = ev.StatusByte.Channel;
            } 
            else
                status = MidiMessage.TypeOf(ev.StatusByte.Byte);

            // Keep in mind midi ports to determine the channel!
            var channel = statusChannel;

            var portChanOffset = seq.MidiPortChannelOffsets[track.Port];
            if (portChanOffset >= 0) channel += portChanOffset;

            // Ensure that the channel is always there (safety precaution)
            if (channel >= channels.Count)
                Util.Grow(ref channels, channel + 1);

            channels[channel] ??= NewChanStatus(channel);
            var ch = channels[channel]!.Value;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (status)
            {
                // Skip note messages
                case MidiMessage.Type.NoteOn:
                {
                    // Always track the last note, even if portamento isn't applied.
                    // See: https://github.com/spessasus/spessasynth_core/issues/77
                    ch.PortamentoNote = ev.Data[0];
                    break;
                }
                case MidiMessage.Type.NoteOff:
                {
                    break;
                }
                // Skip pitch wheel
                case MidiMessage.Type.PitchWheel:
                    ch.PitchWheel = (ev.Data[1] << 7) | ev.Data[0];
                    break;

                case MidiMessage.Type.SystemExclusive:
                {
                    var analyzed = MidiUtils.AnalyzeSysEx(ev.Data);
                    // Sysex may change controllers
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (analyzed.MType) 
                    {
                        default:
                            seq.ProcessEvent(ev, trackIndex);
                            break;

                        /*
                        Program change cannot be skipped.
                        Some MIDIs edit drums via sysEx and skipping program changes causes them to be sent after, resetting the params.
                        Testcase: (GS88Pro)Th19_1S(KR.Palto47)
                         */
                        case MidiUtils.AnalyzedMessage.Type.AnalyzedParameter
                            when analyzed.AsAnalyzedParameter is
                                { AsControllerChange: var
                                    (controller, value, chan) }:
                        {
                            // Channel number may be above 15
                            if (chan >= channelsToSave) break;
                            
                            // Empty tracks cannot controller change
                            if (seq.Midi.IsMultiPort &&
                                track.Channels.Count == 0)
                                break;

                            if (controller == Midi.CC.ResetAllControllers) 
                            {
                                ResetAllControllers(chan);
                                break;
                            }
                            if (NonSkippableCCs.Contains(controller))
                                seq.SendCC(chan, controller, value);
                            else channels[channel]?.Controllers.AsSpan()
                                [(int)controller] = (short)(value << 7);
                            break;
                        }
                    }
                    break;
                }

                case MidiMessage.Type.ControllerChange:
                {
                    // Empty tracks cannot controller change
                    if (seq.Midi.IsMultiPort && track.Channels.Count == 0)
                        break;

                    // Do not skip data entries
                    var controller = (Midi.CC)ev.Data[0];
                    var value = ev.Data[1];
                    
                    // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                    switch (controller) 
                    {
                        default: 
                            if (controller == Midi.CC.ResetAllControllers) 
                            {
                                ResetAllControllers(channel);
                                break;
                            }

                            if (NonSkippableCCs.Contains(controller))
                                seq.SendCC(channel, controller, value);
                            else ch.Controllers.AsSpan()[(int)controller] = 
                                (short)(value << 7);
                            break;

                        // Parameter tracking
                        case Midi.CC.RegisteredParameterMSB:
                        case Midi.CC.RegisteredParameterLSB:
                        case Midi.CC.NonRegisteredParameterLSB:
                        case Midi.CC.NonRegisteredParameterMSB: 
                            // Track and event indexes are irrelevant here
                            ch.Param.ControllerChange(controller, value, 0, 0);
                            // Always send regardless
                            seq.SendCC(channel, controller, value);
                            break;

                        case Midi.CC.DataEntryMSB:
                        case Midi.CC.DataEntryLSB: 
                        {
                            var analyzed = ch.Param.ControllerChange(
                                controller, value, 0, 0)!.Value;
                            // Always send regardless
                            seq.SendCC(channel, controller, value);

                            // NRPN may change controllers
                            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                            switch (analyzed.MType) 
                            {
                                default:
                                    break;

                                case MidiUtils.AnalyzedParameter.Type.ControllerChange:
                                {
                                    var cc =
                                        analyzed.AsControllerChange!.Value;
                                    if (NonSkippableCCs.Contains(cc.Controller))
                                        seq.SendCC(
                                            channel, cc.Controller, cc.Value);
                                    else
                                        ch.Controllers.AsSpan()[
                                            (int)cc.Controller] =
                                            (short)(cc.Value << 7);
                                    break;
                                }
                            }
                            break;
                        }
                    }
                    
                    break;
                }
                case MidiMessage.Type.SetTempo:
                {
                    var tempoBPM = 
                        60_000_000d / Util.ReadBigEndian(ev.Data[..3]);
                    seq.OneTickToSeconds = 
                        60d / (tempoBPM * seq.Midi.TimeDivision);
                    savedTempo = ev;
                    savedTempoTrack = trackIndex;
                    break;
                }
                default:
                    /*
                    Program change cannot be skipped.
                    Some MIDIs edit drums via sysEx and skipping program changes causes them to be sent after, resetting the params.
                    Testcase: (GS88Pro)Th19_1S(KR.Palto47)
                     */
                    seq.ProcessEvent(ev, trackIndex);
                    break;
            }

            channels[channel] = ch;

            // Find the next event
            if (++seq.Index >= timeline.Length)
            {
                seq.Stop();
                ReturnArrays();
                return false;
            }

            var nextEvent = seq.Midi[timeline[seq.Index]];
            seq.PlayedTime +=
                seq.OneTickToSeconds * (nextEvent.Ticks - ev.Ticks);
        }

        var defMidiControllers = 
            Reset.DefaultMidiControllers.AsSpan();
        
        // For all synth channels
        for (var channel = 0; channel < channels.Count; channel++) 
        {
            var optCh = channels[channel];
            if (optCh is not { } ch) continue;
            
            // Restore pitch wheels
            seq.SendPitchWheel(channel, ch.PitchWheel);
            
            // Restoring portamento (only if currently active)
            // Note: we do it before controllers as portamento control may want to override it
            if (ch.PortamentoNote >= 0) 
            {
                if (seq.ExternalPlayback)
                {
                    seq.SendCC(
                        channel,
                        Midi.CC.PortamentoControl,
                        ch.PortamentoNote);    
                }
                else
                {
                    seq.Synth.MidiChannels[channel].SetLastNote(
                        ch.PortamentoNote);
                }
            }

            // Restoring saved controllers
            // Every controller that has changed
            for (var i = 0; i < Reset.CONTROLLER_TABLE_SIZE; i++)
            {
                var value = ch.Controllers[i] >> 7;
                if (value != defMidiControllers[i] &&
                    !NonSkippableCCs.Contains((Midi.CC)i))
                    seq.SendCC(channel, (Midi.CC)i, value);
            }
        }
        
        // Restoring tempo
        if (savedTempo != null)
            seq.CallEvent(
                new Event.CbMetaEvent(savedTempo.Value, savedTempoTrack));

        // Restoring paused time
        if (seq.Paused) seq.PausedTime = (float)seq.PlayedTime;

        ReturnArrays();
        return true;

        ChannelStatus NewChanStatus(int i)
        {
            var controlls = Util.Rent<short>(128);
            Reset.DefaultMidiControllers.AsSpan().CopyTo(
                controlls);
            return new ChannelStatus(
                PitchWheel: 8_192,
                Controllers: controlls,
                Param: new ParamTracker(i),
                PortamentoNote: -1);
        }

        void ReturnArrays()
        {
            foreach (var optChan in channels)
                if (optChan is {} chan) Util.Return(chan.Controllers);
            Util.Return(channels);
        }
        
        /*
         * RP-15 compliant reset
         * https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp15.pdf
         */
        void ResetAllControllers(int chan)
        {
            if (chan >= channels.Count || channels[chan] == null)
                return;
            
            var ch = channels.AsSpan()[chan]!.Value;
            
            // Reset pitch wheel
            ch.PitchWheel = 8_192;
            ch.Param.Reset();

            var controllers = ch.Controllers;
            var defControllers = 
                Reset.DefaultMidiControllers.AsSpan();
            foreach (var resetCC in Reset.Rp15ResetCCNums) 
                controllers[(int)resetCC] = defControllers[(int)resetCC];

            channels[chan] = ch;
        }
    }
}