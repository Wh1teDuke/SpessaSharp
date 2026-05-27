using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.Synthesizer.Engine;

/// <summary>Represents a snapshot of the synthesizer's state.</summary>
public sealed class SynthesizerSnapshot(
    ChannelSnapshot[] midiChannels,
    KeyModifier?[]?[] keyMappings,
    GlobalSystemParameter[] systemParameters,
    GlobalMidiParameter[] midiParameters,
    Effect.ReverbProcessorSnapshot reverbProcessor,
    Effect.ChorusProcessorSnapshot chorusProcessor,
    Effect.DelayProcessorSnapshot delayProcessor,
    Effect.InsertionProcessorSnapshot insertionProcessorProcessor)
{
    /// <summary>The individual channel snapshots.</summary>
    public readonly ChannelSnapshot[] MidiChannels = midiChannels;
    
    /// <summary>Key modifiers.</summary>
    public readonly KeyModifier?[]?[] KeyMappings = keyMappings;

    public readonly GlobalSystemParameter[] SystemParameters = systemParameters;
    public readonly GlobalMidiParameter[] MidiParameters = midiParameters;
    
    public readonly Effect.ReverbProcessorSnapshot ReverbProcessor = reverbProcessor;
    public readonly Effect.ChorusProcessorSnapshot ChorusProcessor = chorusProcessor;
    public readonly Effect.DelayProcessorSnapshot DelayProcessor = delayProcessor;
    public Effect.InsertionProcessorSnapshot InsertionProcessor = insertionProcessorProcessor;

    /// <summary>
    /// Creates a new synthesizer snapshot from the given SpessaSynthProcessor.
    /// </summary>
    /// <param name="synth">The processor to take a snapshot of.</param>
    /// <returns>The snapshot.</returns>
    public static SynthesizerSnapshot Get(Synthesizer synth) =>
        new(
            synth.MidiChannels.Select(c => c.GetSnapshot()).ToArray(),
            synth.KeyModifierManager.GetMappings(),
            synth.SystemParameters.ToArray(),
            synth.MidiParameters.ToArray(),
            synth.ReverbProcessor.GetSnapshot(),
            synth.ChorusProcessor.GetSnapshot(),
            synth.DelayProcessor.GetSnapshot(),
            synth.GetInsertionSnapshot());

    /// <summary>Applies the snapshot to the synthesizer.</summary>
    /// <param name="synth">The processor to apply the snapshot to.</param>
    public void Apply(Synthesizer synth) 
    {
        // Restore key modifiers
        synth.KeyModifierManager.SetMappings(KeyMappings);

        // Add channels if more needed
        while (synth.MidiChannels.Count < MidiChannels.Length) 
            synth.CreateMIDIChannel(true);

        // Restore channels
        for (var i = 0; i < MidiChannels.Length; i++)
            synth.MidiChannels[i].Apply(MidiChannels[i]);

        // Restore effect processors
        var rp = synth.ReverbProcessor;
        var rs = ReverbProcessor;
        
        rp.Level = rs.Level;
        rp.PreLowPass = rs.PreLowPass;
        rp.Character = rs.Character;
        rp.DelayFeedback = rs.DelayFeedback;
        rp.Time = rs.Time;
        rp.PreDelayTime = rs.PreDelayTime;
        
        var cp = synth.ChorusProcessor;
        var cs = ChorusProcessor;
        
        cp.Level = cs.Level;
        cp.PreLowPass = cs.PreLowPass;
        cp.Delay = cs.Delay;
        cp.Depth = cs.Depth;
        cp.Feedback = cs.Feedback;
        cp.Rate = cs.Rate;
        cp.SendLevelToDelay = cs.SendLevelToDelay;
        cp.SendLevelToReverb = cs.SendLevelToReverb;

        var dp = synth.DelayProcessor;
        var ds = DelayProcessor;
        
        dp.Feedback = ds.Feedback;
        dp.Level = ds.Level;
        dp.PreLowPass = dp.PreLowPass;
        dp.SendLevelToReverb = ds.SendLevelToReverb;
        dp.LevelCenter = ds.LevelCenter;
        dp.LevelLeft = ds.LevelLeft;
        dp.LevelRight = ds.LevelRight;
        dp.TimeCenter = ds.TimeCenter;
        dp.TimeRatioLeft = ds.TimeRatioLeft;
        dp.TimeRatioRight = ds.TimeRatioRight;

        // Restore insertion
        var ins = InsertionProcessor;
        synth.SystemExclusive(
            MidiUtils.GsData(0x40, 0x03, 0x00,
            (byte)(ins.Type >> 8), (byte)(ins.Type & 0x7f)));

        for (var i = 0; i < ins.Params.Count; i++)
            if (ins.Params[i] != 255)
                synth.SystemExclusive(
                    MidiUtils.GsData(0x40, 0x03, 3 + i, ins.Params[i]));

        for (var channel = 0; channel < ins.Channels.Count; channel++)
            synth.SystemExclusive(
                MidiUtils.GsData(
                    0x40, 
                    0x40 | MidiUtils.FromChannel(channel), 
                    0x22, 
                    (byte)(ins.Channels[channel] ? 1 : 0)));
        
        // Restore MIDI parameters
        foreach (var param in MidiParameters)
            synth.Set(param);

        // Restore system parameters last
        foreach (var param in SystemParameters)
            synth.Set(param);
    }
}