using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class NoteOff
{
    public static void Send(MidiChannel chan, int midiNote)
    {
        if (midiNote is > 127 or < 0) return;

        var synth = chan.SynthCore;

        if (// If high performance mode, kill notes instead of stopping them
            (synth.SystemParameters.BlackMIDIMode &&
             // If the channel is percussion channel, do not kill the notes
             !chan.DrumChannel) ||
            // If "receive note off" is enabled, kill the note (force quick release)
            (chan.DrumChannel && chan.DrumParams[midiNote].RxNoteOff)) 
        {
            // Instantly kill the note
            chan.KillNote(midiNote);
            synth.CallEvent(new Event.CbNoteOff(midiNote, chan.Channel));
            return;
        }

        chan.PlayingNotes[midiNote] = false;
        var mono = !chan.MidiParameters.PolyMode;
        // Mono mode overrides sustain
        var sustain = !mono && chan.MidiControllers[
            (int)Midi.CC.SustainPedal] >= 8_192;
        var vc = 0;
        var noteID = chan.NoteOffID[midiNote];
        // Only update if note on is above this
        // Testcase: overlapping_notes_test (multiple note off)
        if (noteID < chan.NoteOnID[midiNote])
            chan.NoteOffID[midiNote]++;

        if (chan.VoiceCount > 0)
        {
            var cTime = (float)synth.CurrentTime;
            foreach (var v in synth.Voices)
            {
                if (v.Channel == chan.Channel &&
                    v.IsActive &&
                    v.MidiNote == midiNote &&
                    v.NoteID == noteID &&
                    !v.IsInRelease) 
                {
                    if (sustain) v.IsHeld = true;
                    else v.ReleaseVoice(cTime);

                    if (++vc >= chan.VoiceCount) break; // We already checked all the voices
                }
            }
        }
        
        synth.CallEvent(new Event.CbNoteOff(midiNote, chan.Channel));
        
        // Mono mode, restore highest still pressed note
        if (mono) 
        {
            for (var i = chan.PlayingNotes.Length - 1; i >= 0; i--)
            {
                if (!chan.PlayingNotes[i]) continue;

                if (chan.LastMonoNote == midiNote)
                {
                    // The if condition above:
                    // Ensure that we don't retrigger note that's not this one
                    // For example notes might go like this:
                    // On 50, 60, 70
                    // Off 50 -> Jumps to 70
                    // Off 60 -> We're not the last note so no change, don't jump to 70 again
                    // The note will be set automatically below
                    chan.NoteOn(i, chan.LastMonoVelocity, false);
                }

                return;
            }
            
            // No note is playing
            chan.LastMonoNote = -1;
        }
    }
}