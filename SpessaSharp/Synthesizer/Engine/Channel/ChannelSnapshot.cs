using System.Collections;
using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>Represents a snapshot of a single channel's state in the synthesizer.</summary>
public sealed class ChannelSnapshot(
    MidiPatch.Full? patch,
    Midi.System lockedSystem,
    
    short[] midiControllers,
    BitArray lockedControllers,
    short[] pitchWheels,
    Awe32NRPN.ChannelGenerators generators,
    
    ChannelMidiParameter[] midiParameters,
    ChannelSystemParameter[] systemParameters,
    byte[] octaveTuning,
    
    bool perNotePitch,
    
    DrumParameters[] drumParams,
    bool drumChannel,
    int channelNumber)
{
    /// <summary>The MIDI patch that the channel is using.</summary>
    public readonly MidiPatch.Full? Patch = patch;

    /// <summary>Indicates the MIDI system when the preset was locked</summary>
    public readonly Midi.System LockedSystem = lockedSystem;
    
    /// <summary>The array of all MIDI controllers (in 14-bit values) with the modulator sources at the end.</summary>
    public readonly short[] MidiControllers = midiControllers;

    /// <summary>An array of booleans, indicating if the controller with a current index is locked.</summary>
    public readonly BitArray LockedControllers = lockedControllers;
    
    public readonly short[] PitchWheels = pitchWheels;
    
    public readonly Awe32NRPN.ChannelGenerators Generators = generators;
    
    public readonly ChannelMidiParameter[] MidiParameters = midiParameters;
    public readonly ChannelSystemParameter[] SystemParameters = systemParameters;

    /// <summary>The channel's octave tuning in cents.</summary>
    public readonly byte[] OctaveTuning = octaveTuning;
    
    public readonly bool PerNotePitch = perNotePitch;
    
    /// <summary>Parameters for each drum instrument.</summary>
    public readonly DrumParameters[] DrumParams = drumParams;

    /// <summary>Indicates whether the channel is a drum channel.</summary>
    public readonly bool DrumChannel = drumChannel;

    /// <summary>The channel number this snapshot represents.</summary>
    public readonly int ChannelNumber = channelNumber;
    
    // Creates a new channel snapshot.

    /// <summary>Creates a snapshot of the channel's state </summary>
    /// <returns></returns>
    public static ChannelSnapshot Get(MidiChannel chan)
    {
        var gens = new Awe32NRPN.ChannelGenerators
        {
            OffsetsEnabled = chan.Generators.OffsetsEnabled,
            OverridesEnabled = chan.Generators.OverridesEnabled,
        };
        
        chan.Generators.Offsets.CopyTo(gens.Offsets);
        chan.Generators.Overrides.CopyTo(gens.Overrides);
        
        return new ChannelSnapshot(
            patch: chan.Preset?.Patch,
            lockedSystem: chan.LockedSystem,
            midiControllers: chan.MidiControllers.ToArray(),
            lockedControllers: new BitArray(chan.LockedControllers),
            pitchWheels: chan.PitchWheels.ToArray(),
            generators: gens,
            midiParameters: chan.MidiParameters.ToArray(),
            systemParameters: chan.SystemParameters.ToArray(),
            octaveTuning: chan.OctaveTuning.ToArray(),
            perNotePitch: chan.PerNotePitch,
            drumParams: chan.DrumParams.ToArray(),
            drumChannel: chan.DrumChannel,
            channelNumber: chan.Channel);
    }

    /// <summary>Applies the snapshot to the specified channel.</summary>
    public void Apply(MidiChannel chan) 
    {
        chan.SetDrums(DrumChannel);

        // Restore controllers
        MidiControllers.CopyTo(chan.MidiControllers);
        chan.LockedControllers.Or(LockedControllers);
        PitchWheels.CopyTo(chan.PitchWheels);
        OctaveTuning.CopyTo(chan.OctaveTuning);
        
        chan.PerNotePitch = PerNotePitch;

        Generators.Offsets.CopyTo(chan.Generators.Offsets);
        Generators.Overrides.CopyTo(chan.Generators.Overrides);
        chan.Generators.OffsetsEnabled = Generators.OffsetsEnabled;
        chan.Generators.OverridesEnabled = Generators.OverridesEnabled;

        DrumParams.CopyTo(chan.DrumParams);
        chan.Set((ChannelSystemParameter.Type.PresetLock, false)); // Restored in master params
        if (Patch.HasValue) chan.SetPatch(Patch.Value);
        chan.LockedSystem = LockedSystem;
        
        // Restore MIDI parameters
        foreach (var param in MidiParameters)
            chan.Set(param);
        
        // Restore master parameters last
        foreach (var param in SystemParameters)
            chan.Set(param);
    }
}