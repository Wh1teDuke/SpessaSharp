using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.MIDI.Utils;

internal static class ApplySnapshot
{
    /// <summary>
    /// Modifies the sequence <b>in-place</b> according to the locked presets and controllers in the given snapshot.
    /// <para>
    /// Note that System Parameters <b>fineTune</b> and <b>keyShift</b> are passed to the relative tuning parameters of the channels.
    /// Only locked MIDI parameters and controllers are applied.
    /// </para>
    /// </summary>
    public static void To(Midi midi, SynthesizerSnapshot snapshot)// TODO: Snapshot.ToEditorPatch
    {
        var channels = new Dictionary<
            int, 
            MidiEditor.Parameter<MidiEditor.ChannelModification>>();
        var globalKeyShift = snapshot.SystemParameters.KeyShift;
        var globalFineTune = snapshot.SystemParameters.FineTune;

        for (
            var channelNumber = 0; 
            channelNumber < snapshot.MidiChannels.Length; 
            channelNumber++)
        {
            var channelSnapshot = snapshot.MidiChannels[channelNumber];
            
            if (channelSnapshot.SystemParameters.IsMuted)
            {
                channels[channelNumber] = 
                    MidiEditor.Clear<MidiEditor.ChannelModification>();
                continue;
            }

            var keyShift =
                channelSnapshot.SystemParameters.KeyShift +
                (channelSnapshot.DrumChannel ? 0 : globalKeyShift);
            var fineTune =
                channelSnapshot.SystemParameters.FineTune +
                (channelSnapshot.DrumChannel ? 0 : globalFineTune);

            MidiEditor.Parameter<MidiPatch>? patch = null; 
            if (channelSnapshot.SystemParameters.PresetLock &&
                channelSnapshot.Patch != null)
                patch = MidiEditor.Replace(channelSnapshot.Patch.Value.Data);

            var controllers = 
                new Dictionary<Midi.CC, MidiEditor.Parameter<int>>();

            for (
                var ccNumber = 0;
                ccNumber < Reset.CONTROLLER_TABLE_SIZE;
                ccNumber++)
            {
                if (channelSnapshot.LockedControllers[ccNumber] ||
                    ccNumber == (int)Midi.CC.BankSelect)
                    continue;

                var targetValue = channelSnapshot.MidiControllers[ccNumber] >> 7; // Channel controllers are stored as 14 bit values
                controllers[(Midi.CC)ccNumber] = MidiEditor.Replace(targetValue);
            }

            var midiParameters = new Dictionary<
                ChannelMidiParameter.Type, 
                MidiEditor.Parameter<ChannelMidiParameter>>();

            foreach (var parameter in channelSnapshot.MidiParameters)
            {
                if (!channelSnapshot.LockedParameters[(int)parameter.PType])
                    continue;
                midiParameters[parameter.PType] = 
                    MidiEditor.Replace(parameter);
            }

            channels[channelNumber] = MidiEditor.Replace(
                new MidiEditor.ChannelModification
                {
                    Controllers = controllers,
                    Patch = patch,
                    MidiParameters = midiParameters,
                    KeyShift = keyShift,
                    FineTune = fineTune,
                });
        }
        
        var gMidiParameters = new Dictionary<
            GlobalMidiParameter.Type, 
            MidiEditor.Parameter<GlobalMidiParameter>>();

        foreach (var parameter in snapshot.MidiParameters)
        {
            if (!snapshot.LockedParameters[(int)parameter.PType])
                continue;
            gMidiParameters[parameter.PType] = 
                MidiEditor.Replace(parameter);
        }
        
        // User Drum Set
        Dictionary<
            int, 
            MidiEditor.Parameter<MidiEditor.UserDrumModification>>? 
            userDrumSetParams = null;

        if (snapshot.SystemParameters.DrumLock &&
            snapshot.UserDrumSets.Length > 0)
        {
            userDrumSetParams ??= [];

            for (var drumSetNumber = 0; 
                 drumSetNumber < snapshot.UserDrumSets.Length;
                 drumSetNumber++)
            {
                userDrumSetParams[drumSetNumber] = 
                    GetUserDrumMod(snapshot.UserDrumSets[drumSetNumber]);
            }
        }

        midi.Modify(new MidiEditor.Options
        {
            Channels = channels,
            DrumSetupParams =
                snapshot.SystemParameters.DrumLock 
                    ? MidiEditor.Clear<object>()
                    : null,
            MidiParams = gMidiParameters,
            ReverbParams = snapshot.SystemParameters.ReverbLock
                ? MidiEditor.Replace(snapshot.ReverbProcessor)
                : null,
            ChorusParams = snapshot.SystemParameters.ChorusLock
                ? MidiEditor.Replace(snapshot.ChorusProcessor)
                : null,
            DelayParams = snapshot.SystemParameters.DelayLock
                ? MidiEditor.Replace(snapshot.DelayProcessor)
                : null,
            InsertionParams = snapshot.SystemParameters.InsertionEffectLock
                ? MidiEditor.Replace(snapshot.InsertionProcessor)
                : null,
            UserDrumSetParams = userDrumSetParams,
        });

        return;

        MidiEditor.Parameter<MidiEditor.UserDrumModification> GetUserDrumMod(
            UserDrumSetParameter.Entry[] userDrumSet)
        {
            // Only set the ones that were changed
            var userDrumSetParams = 
                new MidiEditor.UserDrumModification([]);

            for (
                var midiNote = 0;
                midiNote < userDrumSet.Length;
                midiNote++)
            {
                var param = userDrumSet[midiNote];
                if (param == UserDrumSetParameter
                        .GetDefault(midiNote)[param.Type])
                    continue;

                if (!userDrumSetParams.Mods.TryGetValue(
                    midiNote, out var value))
                {
                    MidiEditor.Replace(out value, []);
                    userDrumSetParams.Mods[midiNote] = value;
                }

                var dict = value.AsReplace()!.Value;
                dict[param.Type] = param;
            }

            return userDrumSetParams;
        }
    }
}