using System.Diagnostics;
using SpessaSharp.MIDI;

namespace SpessaSharp.Sequencer;

internal static class NewSequence
{
    /// <summary> Assigns a MIDI port channel offset to a track. </summary>
    /// <param name="seq"></param>
    /// <param name="trackNum">The track number to assign the port to.</param>
    /// <param name="port">The MIDI port number to assign.</param>
    public static void AssignPort(
        SpessaSharpSequencer seq, int trackNum, int port) 
    {
        // Do not assign ports to empty tracks
        if (seq.Midi!.Tracks[trackNum].Channels.Count == 0)
            return;

        // Assign new 16 channels if the port is not occupied yet
        if (seq.MidiPortChannelOffset == 0) 
        {
            seq.MidiPortChannelOffset += 16;
            seq.MidiPortChannelOffsets[port] = 0;
        }

        if (seq.MidiPortChannelOffsets[port] < 0) 
        {
            if (seq.Synth.MidiChannels.Length < seq.MidiPortChannelOffset + 15)
                seq.AddNewMidiPort();
            seq.MidiPortChannelOffsets[port] = seq.MidiPortChannelOffset;
            seq.MidiPortChannelOffset += 16;
        }

        seq.CurrentPorts[trackNum] = port;
    }

    /// <summary>Loads a new sequence internally.</summary>
    /// <param name="seq"></param>
    /// <param name="parsedMidi">The parsed MIDI data to load.</param>
    /// <exception cref="Exception"></exception>
    public static void Load(
        SpessaSharpSequencer seq, Midi parsedMidi)
    {
        if (parsedMidi.Tracks.Count == 0)
            throw new ArgumentException("This MIDI has no tracks!");

        if (parsedMidi.Duration == TimeSpan.Zero) 
        {
            // https://github.com/spessasus/SpessaSynth/issues/106
            Debug.WriteLine("[WARN] This MIDI file has a duration of exactly 0 seconds.");
            seq.PausedTime = 0;
            seq.IsFinished = true;
            return;
        }

        seq.OneTickToSeconds = 60d / (120 * parsedMidi.TimeDivision);
        seq.Midi = parsedMidi;
        seq.IsFinished = false;

        // Clear old embedded bank if exists
        seq.Synth.ClearEmbeddedBank();

        // Check for embedded soundfont
        if (seq.Midi.EmbeddedSoundBank != null) 
        {
            Debug.WriteLine(
                "Embedded soundbank detected! Using it.");

            seq.Synth.SetEmbeddedSoundBank(
                seq.Midi.EmbeddedSoundBank.Value,
                seq.Midi.BankOffset);

            // Preload if it has an embedded sound bank
            if (seq.Preload) 
                seq.Midi.Preload(seq.Synth);
        }

        // Copy over the port data
        seq.CurrentPorts.Clear();
        foreach (var t in seq.Midi.Tracks)
            seq.CurrentPorts.Add(t.Port);

        // Clear last port data
        seq.MidiPortChannelOffset = 0;
        seq.MidiPortChannelOffsets.AsSpan().Fill(-1);
        
        // Assign port offsets
        for (var trackIndex = 0; trackIndex < seq.Midi.Tracks.Count; trackIndex++)
        {
            var track = seq.Midi.Tracks[trackIndex];
            seq.AssignPort(trackIndex, track.Port);
        }

        seq.FirstNoteTime = seq.Midi.MidiTicksToSeconds(
            seq.Midi.FirstNoteOn);

        Debug.WriteLine(
            $"Total song time: {seq.Midi.Duration:mm\\:ss}");

        seq.CallEvent(new Event.CbSongChange(seq._songIndex));

        if (seq.Midi.Duration.TotalSeconds <= 0.2) 
        {
            Debug.WriteLine(
                $"[WARN] Very short song: ({seq.Midi.Duration:mm\\:ss}). Disabling loop!");

            seq.LoopCount = 0;
        }

        // Reset the time
        seq.CurrentTimeSec = 0;
    }
}