using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.Utils;

namespace SpessaSharp.Sequencer;

internal static class ProcessEvent
{
    /// <summary>Processes a MIDI event.</summary>
    /// <param name="seq"></param>
    /// <param name="ev">The MIDI event to process.</param>
    /// <param name="trackIndex">The index of the track the event belongs to.</param>
    public static void Execute(
        SpessaSharpSequencer seq, MidiMessage ev, int trackIndex)
    {
        if (seq.ExternalPlayback && // Do not send meta events
            ev.StatusByte.Byte >= 0x80)
        {
            var data = new byte[ev.Data.Count + 1];
            data[0] = ev.StatusByte.Byte;
            ev.Data.CopyTo(data, 1);
            
            seq.SendMidiMessage(data);
            return;
        }
        
        var track = seq.Midi!.Tracks[trackIndex];
        MidiMessage.Type status;
        var channel = 0;
        if (ev.StatusByte.Byte is >= 0x80 and < 0xf0) 
        {
            // Voice message
            status = MidiMessage.TypeOf(ev.StatusByte.Byte & 0xf0);
            channel = ev.StatusByte.Channel;
        } 
        else
            status = MidiMessage.TypeOf(ev.StatusByte.Byte);

        var offset = 0;
        var portChanOffset = seq.MidiPortChannelOffsets[
            seq.CurrentPorts[trackIndex]]; 
        if (portChanOffset >= 0) offset = portChanOffset;
        
        channel += offset;
        
        /*
         Process the event
         Note: We do not use the .sendMessage on the synth here
         as it does not allow us to use more than 16 channels,
         which we need since the sequencer handles multi-port stuff, not the synth!
        */
        switch (status)
        {
            case MidiMessage.Type.NoteOn:
            {
                var velocity = ev.Data[1];
                if (velocity > 0) 
                {
                    seq.Synth.NoteOn(
                        channel,
                        ev.Data[0],
                        velocity);

                    seq.PlayingNotes[channel, ev.Data[0]] = velocity;
                } 
                else 
                {
                    seq.Synth.NoteOff(channel, ev.Data[0]);
                    seq.PlayingNotes.Remove(channel, ev.Data[0]);
                }
                break;
            }

            case MidiMessage.Type.NoteOff:
            {
                seq.Synth.NoteOff(channel, ev.Data[0]);
                seq.PlayingNotes.Remove(channel, ev.Data[0]);
                break;
            }
            
            case MidiMessage.Type.PitchWheel:
                seq.Synth.PitchWheel(channel, (ev.Data[1] << 7) | ev.Data[0]);
                break;

            case MidiMessage.Type.ControllerChange:
                // Empty tracks cannot cc change
                if (seq.Midi!.IsMultiPort && track.Channels.Count == 0)
                    return;

                seq.Synth.ControllerChange(
                    channel, (Midi.CC)ev.Data[0], ev.Data[1]);
                break;
            
            case MidiMessage.Type.ProgramChange:
                // Empty tracks cannot program change
                if (seq.Midi!.IsMultiPort && track.Channels.Count == 0)
                    return;

                seq.Synth.ProgramChange(
                    channel, ev.Data[0]);
                break;
            
            case MidiMessage.Type.PolyPressure:
                seq.Synth.PolyPressure(
                    channel,
                    ev.Data[0],
                    ev.Data[1]);
                break;
            
            case MidiMessage.Type.ChannelPressure:
                seq.Synth.ChannelPressure(
                    channel, ev.Data[0]);
                break;

            case MidiMessage.Type.SystemExclusive:
                seq.Synth.SystemExclusive(ev.Data, offset);
                break;
            
            case MidiMessage.Type.SetTempo:
            {
                var tempoBPM = 60_000_000d / Util.ReadBigEndian(ev.Data[..3]);
                seq.CurrentTempo = tempoBPM;
            
                seq.OneTickToSeconds =
                    60d / (tempoBPM * seq.Midi!.TimeDivision);
                if (seq.OneTickToSeconds == 0) 
                {
                    seq.CurrentTempo = 120;
                    seq.OneTickToSeconds =
                        60d / (120 * seq.Midi!.TimeDivision);
                    Debug.WriteLine("Invalid tempo! Falling back to 120 BPM");
                }
                break;
            }

            case MidiMessage.Type.TimeSignature:
            {
                seq.TimeSignature = (ev.Data[0], ev.Data[1]);
                break;
            }

            case              
                MidiMessage.Type.EndOfTrack or
                MidiMessage.Type.MidiChannelPrefix or
                MidiMessage.Type.SongPosition or
                MidiMessage.Type.ActiveSensing or
                MidiMessage.Type.KeySignature or
                MidiMessage.Type.SequenceNumber or
                MidiMessage.Type.SequenceSpecific or
                MidiMessage.Type.Text or
                MidiMessage.Type.Lyric or
                MidiMessage.Type.Copyright or
                MidiMessage.Type.TrackName or
                MidiMessage.Type.Marker or
                MidiMessage.Type.CuePoint or
                MidiMessage.Type.InstrumentName or
                MidiMessage.Type.ProgramName: break;
            
            case MidiMessage.Type.MidiPort:
                seq.AssignPort(trackIndex, ev.Data[0]);
                break;
            
            case MidiMessage.Type.Reset:
                seq.Synth.StopAllChannels();
                seq.Synth.Reset();
                break;
         
            default:
                Debug.WriteLine($"[WARN] Unrecognized Event: {ev.StatusByte.Byte
                } status byte: {(MidiMessage.Type)ev.StatusByte.Status}");  
                break;
        }
        
        if (MidiMessage.ID(status) is >= 0 and < 0x80)
            seq.CallEvent(new Event.CbMetaEvent(ev, trackIndex));
    }
}