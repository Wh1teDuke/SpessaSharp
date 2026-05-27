using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.MIDI.Utils;

internal static class ApplySnapshot
{
    /// <summary>
    /// Modifies the sequence according to the locked presets and controllers in the given snapshot
    /// Note that this ignores the MIDI parameters and only applies system parameter tuning.
    /// </summary>
    /// <param name="midi"></param>
    /// <param name="snapshot"></param>
    public static void To(Midi midi, SynthesizerSnapshot snapshot)
    {
        var channels = new Dictionary<
            int, ClearableParameter<ChannelModification>>();
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
                channels[channelNumber] = ClearableParameter
                    <ChannelModification>.OfClear();
                continue;
            }

            var keyShift =
                channelSnapshot.SystemParameters.KeyShift +
                (channelSnapshot.DrumChannel ? 0 : globalKeyShift);
            var fineTune =
                channelSnapshot.SystemParameters.FineTune +
                (channelSnapshot.DrumChannel ? 0 : globalFineTune);
            
            var patch = 
                ClearableParameter<MidiPatch>.OfNull();
            if (channelSnapshot.SystemParameters.PresetLock &&
                channelSnapshot.Patch != null)
                patch = ClearableParameter<MidiPatch>.OfReplace(
                    channelSnapshot.Patch.Value);

            var controllers = new Dictionary<Midi.CC, ClearableParameter<int>>();

            for (
                var ccNumber = 0;
                ccNumber < Reset.CONTROLLER_TABLE_SIZE;
                ccNumber++)
            {
                if (channelSnapshot.LockedControllers[ccNumber] ||
                    ccNumber == (int)Midi.CC.BankSelect)
                    continue;

                var targetValue = channelSnapshot.MidiControllers[ccNumber] >> 7; // Channel controllers are stored as 14 bit values
                controllers[(Midi.CC)ccNumber] = ClearableParameter<int>.
                    OfReplace(targetValue);
            }

            channels[channelNumber] = ClearableParameter<
                ChannelModification>.OfReplace(new ChannelModification(
                controllers,
                patch,
                keyShift,
                fineTune));
        }

        midi.Modify(new MidiModifyOptions
        {
            Channels = channels,
            DrumSetupParams = snapshot.SystemParameters.DrumLock ?
                ClearableParameter<object>.OfClear() : null,
            ReverbParams = snapshot.SystemParameters.ReverbLock
                ? ClearableParameter<Effect.ReverbProcessorSnapshot>.OfReplace(snapshot.ReverbProcessor)
                : null,
            ChorusParams = snapshot.SystemParameters.ChorusLock
                ? ClearableParameter<Effect.ChorusProcessorSnapshot>.OfReplace(snapshot.ChorusProcessor)
                : null,
            DelayParams = snapshot.SystemParameters.DelayLock
                ? ClearableParameter<Effect.DelayProcessorSnapshot>.OfReplace(snapshot.DelayProcessor)
                : null,
            InsertionParams = snapshot.SystemParameters.InsertionEffectLock
                ? ClearableParameter<Effect.InsertionProcessorSnapshot>.OfReplace(snapshot.InsertionProcessor)
                : null
        });
    }
}