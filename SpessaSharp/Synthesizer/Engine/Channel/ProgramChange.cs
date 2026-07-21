using SpessaSharp.Synthesizer.Engine.Channel.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class ProgramChange
{
    /// <summary> Changes the program (preset) of the channel. </summary>
    /// <param name="chan"></param>
    /// <param name="program">The program number (0-127) to change to.</param>
    public static void Send(MidiChannel chan, int program)
    {
        if (chan.SystemParameters.PresetLock) return;

        chan.Patch = chan.Patch with { Program = program };
        var preset = chan.SynthCore.SoundBankManager.GetPreset(
            chan.Patch, chan.ChannelSystem);

        if (preset == null) 
        {
            preset = chan.SynthCore.MissingPreset(
                chan.Patch, chan.ChannelSystem);
            if (preset == null) return;
        }

        chan.Preset = preset;

        // Drums first
        // SC resets drum params on program change
        if (preset.IsDrum != chan.DrumChannel)
            chan.SetDrumFlag(preset.IsDrum);

        chan.ResetDrumParams();
        
        // Commit changes made to user drums by purging their cache.
        // SCVA does not play drum sounds until the change is sent, even if this patch was selected before then.
        // See the corresponding test in MIDI tests.
        if (preset is
            {
                IsGMGSDrum: true,
                Program: Synthesizer.GS_USER_DRUM_1 or Synthesizer.GS_USER_DRUM_2
            }) chan.SynthCore.PurgeCachedPatch(preset.Patch);
        
        // Do not spread the preset as we don't want to copy it entirely.
        chan.SynthCore.CallEvent(
            new Event.CbProgramChange(preset.Patch, chan.Channel));
    }
}