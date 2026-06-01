using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
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
                channels[channelNumber] = MidiEditor.Parameter
                    <MidiEditor.ChannelModification>.OfClear();
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
                patch = MidiEditor.Parameter<MidiPatch>.OfReplace(
                    channelSnapshot.Patch.Value);

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
                controllers[(Midi.CC)ccNumber] = MidiEditor.Parameter<int>.
                    OfReplace(targetValue);
            }

            var midiParameters = new Dictionary<
                ChannelMidiParameter.Type, MidiEditor.Parameter<
                    ChannelMidiParameter>>();

            foreach (var parameter in channelSnapshot.MidiParameters)
            {
                if (!channelSnapshot.LockedParameters[(int)parameter.PType])
                    continue;
                midiParameters[parameter.PType] = MidiEditor.Parameter<
                    ChannelMidiParameter>.OfReplace(parameter);
            }

            channels[channelNumber] = MidiEditor.Parameter<
                MidiEditor.ChannelModification>.OfReplace(
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
                MidiEditor.Parameter<
                    GlobalMidiParameter>.OfReplace(parameter);
        }

        midi.Modify(new MidiEditor.Options
        {
            Channels = channels,
            DrumSetupParams =
                snapshot.SystemParameters.DrumLock 
                    ? MidiEditor.Parameter<object>.OfClear() 
                    : null,
            MidiParams = gMidiParameters,
            ReverbParams = snapshot.SystemParameters.ReverbLock
                ? MidiEditor.Parameter<Effect.ReverbProcessorSnapshot>
                    .OfReplace(snapshot.ReverbProcessor)
                : null,
            ChorusParams = snapshot.SystemParameters.ChorusLock
                ? MidiEditor.Parameter<Effect.ChorusProcessorSnapshot>
                    .OfReplace(snapshot.ChorusProcessor)
                : null,
            DelayParams = snapshot.SystemParameters.DelayLock
                ? MidiEditor.Parameter<Effect.DelayProcessorSnapshot>
                    .OfReplace(snapshot.DelayProcessor)
                : null,
            InsertionParams = snapshot.SystemParameters.InsertionEffectLock
                ? MidiEditor.Parameter<Effect.InsertionProcessorSnapshot>
                    .OfReplace(snapshot.InsertionProcessor)
                : null,
        });
    }
}