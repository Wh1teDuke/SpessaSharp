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
    BitArray lockedParameters,
    ChannelSystemParameter[] systemParameters,
    byte[] octaveTuning,
    
    bool perNotePitch,
    CustomChannelVibrato customVibrato,
    
    DrumParameter[] drumParams,
    bool drumChannel,
    int channel)
{
    /// <summary>The currently selected MIDI patch of the channel.</summary>
    public readonly MidiPatch.Full? Patch = patch;

    /// <summary>Indicates the MIDI system when the preset was locked.</summary>
    public readonly Midi.System LockedSystem = lockedSystem;
    
    /// <summary>
    /// An array of MIDI controllers for the channel.
    /// This array is used to store the state of various MIDI controllers
    /// such as volume, pan, modulation, etc.
    /// <remarks>
    /// A bit of an explanation:
    /// The controller table is stored as an int16 array, it stores 14-bit values, allowing for full 14-bit LSB resolution.
    /// The only exception from this are the Registered and Non-Registered Parameter Numbers.
    /// Data entries do store it!
    /// </remarks>
    /// </summary>
    public readonly short[] MidiControllers = midiControllers;

    /// <summary>
    /// An array indicating if a controller, at the equivalent index in the <see cref="MidiChannel.MidiControllers"/> array, is locked
    /// (i.e., not allowed changing).
    /// A locked controller cannot be modified.
    /// </summary>
    public readonly BitArray LockedControllers = lockedControllers;
    
    /// <summary> An array for the MIDI 2.0 Per-note pitch wheels. </summary>
    public readonly short[] PitchWheels = pitchWheels;
    
    /// <summary> Used for handling SF2/AWE32 NRPN generator adjustments. </summary>
    public readonly Awe32NRPN.ChannelGenerators Generators = generators;
    
    /// <summary>
    /// The Channel MIDI Parameters of this channel. These are only editable via MIDI messages.
    /// </summary>
    public readonly ChannelMidiParameter[] MidiParameters = midiParameters;
    
    /// <summary>
    /// An object indicating if a Channel MIDI parameter, at the equivalent key, is locked
    /// (i.e., not allowed changing).
    /// A locked parameter cannot be modified.
    /// </summary>
    public readonly BitArray LockedParameters = lockedParameters;
    
    /// <summary>
    /// The Channel System Parameters of this channel.
    /// These are only editable via the API.
    /// </summary>
    public readonly ChannelSystemParameter[] SystemParameters = systemParameters;

    /// <summary>
    /// An array of octave tuning values for each note on the channel.
    /// Each index corresponds to a note (0 = C, 1 = C#, ..., 11 = B).
    /// Note: Repeated every 12 notes.
    /// </summary>
    public readonly byte[] OctaveTuning = octaveTuning;
    
    /// <summary>
    /// Per-note pitch wheel mode uses the pitchWheels table as source
    /// instead of the regular entry in the midiControllers table.
    /// </summary>
    public readonly bool PerNotePitch = perNotePitch;

    /// <summary>
    /// The vibrato settings for the channel.
    /// </summary>
    public readonly CustomChannelVibrato CustomVibrato = customVibrato;
    
    /// <summary>Parameters for each drum instrument.</summary>
    public readonly DrumParameter[] DrumParams = drumParams;

    /// <summary>Indicates whether the channel is a drum channel.</summary>
    public readonly bool DrumChannel = drumChannel;

    /// <summary>The channel number this snapshot represents.</summary>
    public readonly int Channel = channel;
    
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
            midiControllers: [.. chan.MidiControllers],
            lockedControllers: new BitArray(chan.LockedControllers),
            pitchWheels: [.. chan.PitchWheels],
            generators: gens,
            midiParameters: [.. chan.MidiParameters],
            lockedParameters: new BitArray(chan.LockedParameters),
            systemParameters: [.. chan.SystemParameters],
            octaveTuning: [.. chan.OctaveTuning],
            perNotePitch: chan.PerNotePitch,
            customVibrato: chan.CustomVibrato,
            drumParams: [.. chan.DrumParams],
            drumChannel: chan.DrumChannel,
            channel: chan.Channel);
    }

    /// <summary>Applies the snapshot to the specified channel.</summary>
    public void Apply(MidiChannel chan) 
    {
        chan.SetDrums(DrumChannel);

        // Restore controllers
        MidiControllers.CopyTo(chan.MidiControllers);
        chan.LockedControllers.SetAll(false);
        chan.LockedControllers.Or(LockedControllers);
        PitchWheels.CopyTo(chan.PitchWheels);
        OctaveTuning.CopyTo(chan.OctaveTuning);
        
        chan.PerNotePitch = PerNotePitch;
        chan.CustomVibrato = CustomVibrato;

        Generators.Offsets.CopyTo(chan.Generators.Offsets);
        Generators.Overrides.CopyTo(chan.Generators.Overrides);
        chan.Generators.OffsetsEnabled = Generators.OffsetsEnabled;
        chan.Generators.OverridesEnabled = Generators.OverridesEnabled;

        DrumParams.CopyTo(chan.DrumParams);
        chan.Set((ChannelSystemParameter.Type.PresetLock, false)); // Restored in master params
        if (Patch is {} patch) 
        {
            chan.SetBankMSB(patch.BankMSB);
            chan.SetBankLSB(patch.BankLSB);
            chan.SetIsGMGSDrum(patch.IsGMGSDrum);
            chan.ProgramChange(patch.Program);
            // Fallback if no preset matched and the flag didn't sync
            chan.SetDrumFlag(DrumChannel);
        } 
        else 
        {
            chan.SetDrumFlag(DrumChannel);
        }
        chan.LockedSystem = LockedSystem;
        
        // Restore MIDI parameters
        // Unlock them first
        chan.LockedParameters.SetAll(false);

        // Then set
        foreach (var param in MidiParameters)
            chan.Set(param);
        
        // Then re-lock
        chan.LockedParameters.SetAll(false);
        chan.LockedParameters.Or(LockedParameters);
        
        // Restore master parameters last
        foreach (var param in SystemParameters)
            chan.Set(param);
    }
}