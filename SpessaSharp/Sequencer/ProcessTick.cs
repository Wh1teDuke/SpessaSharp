using SpessaSharp.MIDI;

namespace SpessaSharp.Sequencer;

internal static class ProcessTick
{
    /// <summary>Processes a single MIDI tick. Call this every rendering quantum to process the sequencer events in real-time.</summary>
    /// <param name="seq"></param>
    public static void Execute(SpessaSharpSequencer seq)
    {
        if (seq.Paused || seq.Midi is not {} midi) return;

        var currentTime = seq.CurrentTimeSec;
        var timeline = midi.Timeline;
        while (seq.PlayedTime < currentTime) 
        {
            // Find the next event and process it
            var tl = timeline[seq.Index++];
            var ev = midi[tl];

            seq.Tick = ev.Ticks;
            seq.ProcessEvent(ev, tl.Tr);

            // Check for loop
            if (seq.LoopCount > 0 && midi.Loop.End <= ev.Ticks) 
            {
                if (seq.LoopCount != int.MaxValue) 
                {
                    seq.LoopCount--;
                    seq.CallEvent(new Event.CbLoopCountChange(seq.LoopCount));
                }

                if (midi.Loop.Type == Midi.LoopType.Soft)
                    seq.JumpToTick(midi.Loop.Start);
                else
                    seq.SetTimeTicks(midi.Loop.Start);

                return;
            }
            
            // Check for end of track
            if (seq.Index >= timeline.Length ||
                // https://github.com/spessasus/spessasynth_core/issues/21
                ev.Ticks >= midi.LastVoiceEventTick)
            {
                // Stop the playback
                seq.SongIsFinished();
                return;
            }

            var nextEvent = midi[timeline[seq.Index]];
            seq.PlayedTime +=
                seq.OneTickToSeconds * (nextEvent.Ticks - ev.Ticks);
        }
    }
}