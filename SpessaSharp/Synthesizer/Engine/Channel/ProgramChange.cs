using System.ComponentModel.Design.Serialization;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Utils;

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
        
        // Commit changes made to user drums
        // SCVA does not play drum sounds until the change is sent, even if this patch was selected before then.
        // See the corresponding test in MIDI tests.
        if (preset is
            {
                IsGMGSDrum: true,
                Program: Synthesizer.GS_USER_DRUM_1 or Synthesizer.GS_USER_DRUM_2
            })
        {
            // Purge cache for this preset to cache the new drum voice data
            chan.SynthCore.PurgeCachedPatch(preset.Patch);
            // Copy drum param data
            if (preset is UserDrumSet uds)
                uds.CopyInto(chan.DrumParams);
            else
                SpessaLog.Warn(
                    $"Current patch should be GS User Drum! Instead found {preset.Name}.");
        }

        // Do not spread the preset as we don't want to copy it entirely.
        chan.SynthCore.CallEvent(
            new Event.CbProgramChange(preset.Patch, chan.Channel));
    }
}