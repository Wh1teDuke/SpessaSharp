using System.Collections;
using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

/// <summary>
/// This class represents a single MIDI Channel within the synthesizer.
/// </summary>
public sealed class MidiChannel: ISf2Channel
{
    /// <summary>
    /// Parameter that determines how voice assignment will be handled when sounds overlap on identical note numbers in the same channel (i.e., repeatedly struck notes). This is initialized to a mode suitable for each Part, so for general purposes there is no need to change this.
    /// </summary>
    public enum Assign
    {
        /// <summary> A new note will kill the previous one if it is still playing.</summary>
        Single,
        /// <summary>
        /// A new note will not kill the previous notes (default).
        /// While GS and XG differentiate 1 (Limited Multi for GS/Multi for XG) and 2 (Full Multi for GS/Inst (for Drum)),
        /// SpessaSynth treats them both as full Multi. (no note killing is performed)
        ///
        /// This may be useful for emulating SC-55 hi-hat cutoff or MSGS note cutoff.
        ///
        /// Refer to [SC-8850 Owner's Manual](https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf), page 238 for more description.
        /// Note that <b>SAME NOTE NUMBER KEY ON ASSIGN</b> in XG is also recognized as assign mode.
        /// </summary>
        FullMulti
    }
    
    /// <summary>
    /// An array of MIDI controllers for the channel.
    /// This array is used to store the state of various MIDI controllers such as volume, pan, modulation, etc.
    /// </summary>
    /// <remarks>
    /// A bit of an explanation:<br/>
    /// The controller table is stored as an int16 array, it stores 14-bit values, allowing for full 14-bit LSB resolution.
    /// The only exception from this are the Registered and Non-Registered Parameter Numbers. Data entries do store it!
    /// </remarks>
    public readonly short[] MidiControllers = new short[
        Engine.Channel.Reset.CONTROLLER_TABLE_SIZE];
    
    /// <summary>
    /// An array indicating if a controller, at the equivalent index in the midiControllers array, is locked (i.e., not allowed changing). A locked controller cannot be modified.
    /// </summary>
    internal readonly BitArray LockedControllers = 
        new (Engine.Channel.Reset.CONTROLLER_TABLE_SIZE);

    /// <summary>
    /// An object indicating if a Channel MIDI parameter, at the equivalent key, is locked
    /// (i.e., not allowed changing).
    /// A locked parameter cannot be modified.
    /// </summary>
    internal readonly BitArray LockedParameters = new(
        ChannelMidiParameter.Len);
    
    /// <summary> An array for the MIDI 2.0 Per-note pitch wheels. </summary>
    public readonly short[] PitchWheels = new short[128];

    /// <summary>
    /// An array of octave tuning values for each note on the channel.
    /// Each index corresponds to a note (0 = C, 1 = C#, ..., 11 = B).
    /// Note: Repeated every 12 notes.
    /// </summary>
    public readonly byte[] OctaveTuning = new byte[128];
    
    /// <summary>Parameters for each drum instrument.</summary>
    public readonly DrumParameters[] DrumParams = new DrumParameters[128];
    
    /// <summary>A system for dynamic modulator assignment for advanced system exclusives.</summary>
    public readonly DynamicModulatorManager DynamicModulators;

    public readonly List<Voice.Voice> Voices = new(32);

    /// <summary>Indicates whether this channel is a drum channel.</summary>
    public bool DrumChannel { get; internal set; }

    /// <summary>SF2 NRPN LSB for selecting a generator value.</summary>
    public int Sf2NRPNGeneratorLSB = 0;

    /// <summary>
    /// The currently selected MIDI patch of the channel. Note that the exact matching preset may not be available, but this represents exactly what MIDI asks for.
    /// </summary>
    public MidiPatch Patch;

    /// <summary>
    /// The preset currently assigned to the channel. Note that this may be undefined in some cases.<br/> https://github.com/spessasus/spessasynth_core/issues/48
    /// </summary>
    public SynthPatch? Preset { get; internal set; }

    /// <summary> Indicates the MIDI system when the preset was locked. </summary>
    internal Midi.System LockedSystem = Midi.System.GS;

    /// <summary> Channel's current voice count. </summary>
    public int VoiceCount => Voices.Count;
    
    /// <summary> The channel's number (0-based index) </summary>
    public readonly int Channel;
    
    /// <summary> Core synthesis engine. </summary>
    internal readonly Synthesizer SynthCore;
    
    /*
    ==========
    PUBLIC API
    ==========
    */
    
    
    /// <summary>Locks or unlocks a given controller. This prevents any changes to it until it's unlocked.</summary>
    /// <param name="controller">The MIDI controller number (0-127).</param>
    /// <param name="isLocked">If the controller should be locked.</param>
    public void LockController(Midi.CC controller, bool isLocked) =>
        LockedControllers[(int)controller] = isLocked;
    
    /// <summary> Sets a system parameter of the channel. </summary>
    /// <param name="parameter">The type and value of the system parameter to set.</param>
    public void Set(ChannelSystemParameter parameter) =>
        ChannelSystemParameters.Set(this, parameter);

    /// <summary>Sets a MIDI channel parameter of the synthesizer.</summary>
    /// <param name="parameter">The type and value of the MIDI channel parameter to set.</param>
    public void Set(ChannelMidiParameter parameter) =>
        ChannelMidiParameters.Set(this, parameter);

    /// <summary>
    /// Locks or unlocks a given Channel MIDI Parameter.
    /// This prevents any changes to it until it's unlocked.
    /// </summary>
    /// <param name="parameter">The Channel MIDI Parameter to lock.</param>
    /// <param name="isLocked">If the parameter should be locked.</param>
    public void LockParameter(
            ChannelMidiParameter.Type parameter, bool isLocked) =>
        LockedParameters[(int)parameter] = isLocked;
    
    /*
    =================
    END OF PUBLIC API
    =================
    */

    /// <summary> Per-note pitch wheel mode uses the pitchWheels table as source instead of the regular entry in the midiControllers table. </summary>
    internal bool PerNotePitch = false;
    
    /// <summary>
    /// Current pan in range [-500;500]<br/>
    /// Updated on master/MIDI parameter change.
    /// This is used to avoid a big addition for every voice rendering call.
    /// </summary>
    internal int CurrentPan = 0;
    
    /// <summary>
    /// Current tuning in cents
    /// Updated on master/MIDI parameter change.
    /// </summary>
    internal float CurrentTuning = 0;

    /// <summary>
    /// Current key-shift.
    /// Updated on master/MIDI parameter change.
    /// </summary>
    internal int CurrentKeyShift = 0;

    /// <summary>
    /// Current gain.
    /// Updated on master/MIDI parameter change.
    /// </summary>
    internal float CurrentGain = 0;

    /// <summary>The last pressed note on this channel for portamento tracking.</summary>
    /// <remarks>
    /// This is not a `MIDIChannelParameter` and is strictly internal,
    /// mostly because we don't want to send events for every note on message.
    /// It can be set with Portamento Control CC anyway.
    /// </remarks>
    internal int LastPortamentoNote = -1;

    /// <summary>
    /// If the portamento should be executed once regardless of Portamento on/off.
    /// Adhering to the MIDI spec, CC#84 ignores on/off.
    /// </summary>
    /// <remarks>This is also not a `MIDIChannelParameter` for the same reason as `lastPortamentoNote`</remarks>
    internal bool PortamentoForce = false;

    /// <summary>
    /// The last pressed note on this channel in mono mode.
    /// Used for tracking and releasing this note on a new Note On event.
    /// -1 means none.
    /// </summary>
    internal int LastMonoNote = -1;

    /// <summary>
    /// The last pressed note's velocity on this channel in mono mode.
    /// </summary>
    internal int LastMonoVelocity = 0;

    /// <summary>
    /// For Mono Mode restoring notes.<br/>
    /// playingNotes[midiNote]
    /// </summary>
    internal readonly BitArray PlayingNotes = new(128);

    internal readonly Awe32NRPN.ChannelGenerators Generators = new();

    internal readonly ChannelMidiParameter[] MidiParamArray = 
        ChannelMidiParameters.Default.ToArray();

    /// <summary> All master parameters of this channel. </summary>
    internal readonly ChannelSystemParameter[] SystemParamArray =
        ChannelSystemParameters.Default.ToArray();// Copy, not set!
    
    /// <summary>
    /// Note On message tracking, for grouping voices for specific Note On messages.
    /// Used for overlapping Note Ons.
    /// MIDI note: current note on ID
    /// </summary>
    internal int[] NoteOnID = new int[128];

    /// <summary>
    /// Note Off message tracking, for grouping voices for specific Note On messages.
    /// Used for overlapping Note Ons.
    /// MIDI note: current note on ID
    /// </summary>
    internal int[] NoteOffID = new int[128];
    
    /// <summary>
    /// If the last Parameter was RPN. If false then the last parameter was NRPN.
    /// </summary>
    internal bool LastParameterIsRegistered = true;
    
    /// <summary> Indicates whether the channel is muted. </summary>
    public bool IsMuted { get; private set; }

    /// <summary>Constructs a new MIDI channel.</summary>
    /// <param name="synthCore"></param>
    /// <param name="preset"></param>
    /// <param name="channelNumber"></param>
    internal MidiChannel(
        Synthesizer synthCore,
        SynthPatch? preset,
        int channelNumber)
    {
        PitchWheels.AsSpan().Fill(8_192);
        
        SynthCore = synthCore;
        Preset = preset;
        Channel = channelNumber;
        MidiParamArray.RxChannel = channelNumber;
        DynamicModulators = new DynamicModulatorManager(channelNumber);
        
        ResetGeneratorOverrides();
        ResetGeneratorOffsets();
        
        DrumParams.AsSpan().Fill(DrumParameters.Default);
        
        ResetDrumParams();
    }

    /*
    ==========
    PUBLIC API
    ==========
     */
    
    /// <summary>
    /// The channel master parameters of this channel.
    /// These are only editable via the API.
    /// </summary>
    public ReadOnlySpan<ChannelSystemParameter> SystemParameters =>
        SystemParamArray;

    /// <summary>
    /// The channel MIDI parameters of this channel.
    /// These are only editable via MIDI messages.
    /// </summary>
    public ReadOnlySpan<ChannelMidiParameter> MidiParameters => MidiParamArray;
    
    /*
    =================
    END OF PUBLIC API
    =================
    */
    
    internal Midi.System ChannelSystem =>
        SystemParamArray.PresetLock
            ? LockedSystem
            : SynthCore.MidiParameters.System;
    
    /*
    ==========
    PUBLIC API
    ==========
     */

    /// <summary>
    /// Changes the preset to, or from drums. Note that this executes a program change.
    /// </summary>
    /// <param name="isDrum">If the channel should be a drum preset or not.</param>
    /// <exception cref="Exception"></exception>
    public void SetDrums(bool isDrum) 
    {
        if (BankSelectHacks.IsSystemXG(ChannelSystem)) 
        {
            if (isDrum) 
            {
                SetBankMSB(BankSelectHacks.GetDrumBank(ChannelSystem));
                SetBankLSB(0);
            } 
            else 
            {
                if (Channel % 16 == Synthesizer.DEFAULT_PERCUSSION)
                    throw SpessaException.Invalid(
                        $"Cannot disable drums on channel {Channel} for XG.");
                SetBankMSB(0);
                SetBankLSB(0);
            }
        } 
        else SetGSDrums(isDrum);

        SetDrumFlag(isDrum);
        ProgramChange(Patch.Program);
    }
        
    /// <summary> Stops all notes on the channel. </summary>
    /// <param name="force">If true, stops all notes immediately, otherwise applies release time.</param>
    public void StopAllNotes(bool force = false) 
    {
        // Clear IDs
        NoteOnID.AsSpan().Clear();
        NoteOffID.AsSpan().Clear();
        PlayingNotes.SetAll(false);
        
        if (force) 
        {
            // Force stop all
            for (var i = Voices.Count - 1; i >= 0; i--)
                SynthCore.Free(Voices[i]);
        } 
        else 
        {
            // Gracefully stop
            var cTime = (float)SynthCore.CurrentTime;
            foreach (var v in Voices)
                v.ReleaseVoice(cTime);
        }

        SynthCore.CallEvent(new Event.CbStopAll(Channel, force));
    }
    
    /*
    =================
    END OF PUBLIC API
    =================
     */
    
    // MIDI messages
    
    /// <summary> Sends a "MIDI Note on" message and starts a note. </summary>
    /// <param name="midiNote">The MIDI note number (0-127).</param>
    /// <param name="velocity">The velocity of the note (0-127). If less than 1, it will send a note off instead.</param>
    /// <param name="emit">If the note on should be updated and emitted (non-internal)</param>
    internal void NoteOn(int midiNote, int velocity, bool emit = true) =>
        Engine.Channel.NoteOn.Send(this, midiNote, velocity);

    /// <summary>
    /// Releases a note by its MIDI note number. If the note is in high performance mode and the channel is not a drum channel, it kills the note instead of releasing it.
    /// </summary>
    /// <param name="midiNote">The MIDI note number to release (0-127).</param>
    internal void NoteOff(int midiNote) => 
        Engine.Channel.NoteOff.Send(this, midiNote);

    /// <summary> Changes the program (preset) of the channel. </summary>
    /// <param name="program">The program number (0-127) to change to.</param>
    internal void ProgramChange(int program) =>
        Engine.Channel.ProgramChange.Send(this, program);
    
    /// <summary>
    /// Handles MIDI controller changes for a channel.
    /// </summary>
    /// <remarks>
    /// This function processes MIDI controller changes, updating the channel's
    /// midiControllers table and handling special cases like bank select,
    /// data entry, and sustain pedal. It also computes modulators for all voices
    /// in the channel based on the controller change.
    /// to allow changes.
    /// </remarks>
    /// <param name="controller">The MIDI controller number (0-127).</param>
    /// <param name="value">The value of the controller (0-127).</param>
    /// <param name="sendEvent">If an event should be emitted.</param>
    internal void ControllerChange(
        Midi.CC controller,
        int value,
        bool sendEvent = true) =>
        Engine.Channel.ControllerChange.Send(
            this, controller, value, sendEvent);

    /// <summary>
    /// Reset this channel to its default state. Except for the locked controllers.
    /// </summary>
    /// <param name="sendCCEvents"></param>
    internal void Reset(bool sendCCEvents = true) => 
        Engine.Channel.Reset.Channel(this, sendCCEvents);

    /// <summary>
    /// https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp15.pdf<br/>
    /// Reset controllers according to RP-15 Recommended Practice.
    /// </summary>
    internal void ResetRP15() => Engine.Channel.Reset.RP15(this);

    /// <summary>Executes a data entry coarse (MSB) change for the current channel.</summary>
    internal void DataEntry() => Engine.Channel.DataEntry.Execute(this);

    // Voice rendering methods

    /// <summary> Renders a voice to the stereo output buffer </summary>
    /// <param name="voice">The voice to render</param>
    /// <param name="timeNow">Current time in seconds</param>
    /// <param name="outputL">The left output buffer</param>
    /// <param name="outputR">The right output buffer</param>
    /// <param name="startIndex"></param>
    /// <param name="sampleCount"></param>
    internal void RenderVoice(
        Voice.Voice voice,
        float timeNow,
        Span<float> outputL,
        Span<float> outputR,
        int startIndex,
        int sampleCount) => Engine.Channel.RenderVoice.Execute(
        this, voice, timeNow, outputL, outputR, startIndex, sampleCount);

    /// <summary>Sets the octave tuning for a given channel.</summary>
    /// <remarks>Cent tunings are relative.</remarks>
    /// <param name="tuning">The tuning array of 12 values, each representing the tuning for a note in the octave.</param>
    /// <exception cref="Exception"></exception>
    public void SetOctaveTuning(ReadOnlySpan<byte> tuning)
    {
        if (tuning.Length != 12)
            throw SpessaException.Invalid("Tuning is not the length of 12.");

        for (var i = 0; i < 128; i++)
            OctaveTuning[i] = tuning[i % 12];
    }

    /// <summary> Sets the pitch of the given channel. </summary>
    /// <param name="pitch">The pitch (0 - 16383)</param>
    /// <param name="midiNote">The MIDI note number, pass -1 for the regular pitch wheel</param>
    internal void PitchWheel(short pitch, int? midiNote = null)
    {
        if (midiNote is not { } note)
        {
            // Disable the per note pitch mode
            PerNotePitch = false;
            Set((ChannelMidiParameter.Type.PitchWheel, pitch));
        }
        else
        {
            if (!PerNotePitch)
                // Enable the per-note pitch (fill the pitches with the current CC value)
                PitchWheels.AsSpan().Fill((short)MidiParamArray.PitchWheel);

            PerNotePitch = true;
            PitchWheels[note] = pitch;
            
            // Recompute only specific modulators
            ComputeModulatorsAll(
                0, 
                Modulator.Source.ID(
                    Modulator.Source.ControllerSource.PitchWheel));
        }
    }

    /// <summary>
    /// Sets the pressure of the given note on a specific channel. This is used for polyphonic pressure (aftertouch).
    /// </summary>
    /// <param name="midiNote">0 - 127, the MIDI note number to set the pressure for.</param>
    /// <param name="pressure">0 - 127, the pressure value to set for the note.</param>
    internal void PolyPressure(int midiNote, int pressure)
    {
        // Note to self: don't use computeModulatorsAll here as we're setting the pressure!
        foreach (var v in Voices)
        {
            if (v.MidiNote != midiNote) continue;

            v.Pressure = pressure;
            ComputeModulators(
                v, 
                0, 
                Modulator.Source.ID(
                    Modulator.Source.ControllerSource.PolyPressure));
        }

        SynthCore.CallEvent(
            new Event.CbPolyPressure(Channel, midiNote, pressure));
    }
    
    internal void UpdateInternalParams()
    {
        var globalSystem = SynthCore.SystemParameters;
        var channelSystem = SystemParamArray;
        var globalMIDI = SynthCore.MidiParameters;
        var channelMIDI = MidiParamArray;

        // Note:
        // - System -> System Parameter
        // - MIDI -> MIDI Parameter
        
        // Only Channel System is processed for drum channels.
        // Drum channels ignore key shift
        // Testcase: th07_19_user_gm.mid
        CurrentKeyShift = DrumChannel
            ? channelSystem.KeyShift
            :   // Global System
                globalSystem.KeyShift +
                // Global MIDI
                globalMIDI.KeyShift +
                // Channel System
                channelSystem.KeyShift +
                // Channel MIDI
                channelMIDI.KeyShift;
        
        // Only Channel System is processed for drum channels
        CurrentTuning = DrumChannel 
            ? channelSystem.FineTune
            :   // Global System
                globalSystem.FineTune +
                // Global MIDI
                globalMIDI.FineTune +
                // Channel System
                channelSystem.FineTune +
                // Channel MIDI
                channelMIDI.FineTune;

        // [-1;1] normalized
        var currentPanNormalized =
            // Global System
            globalSystem.Pan +
            // Global MIDI
            globalMIDI.Pan +
            // Channel System
            channelSystem.Pan;
        // Channel MIDI is the pan controller

        // For faster renderVoice calculation
        CurrentPan = Util.Round(currentPanNormalized * 500);

        CurrentGain =
            // Global forced
            Synthesizer.SPESSASYNTH_GAIN_FACTOR *
            // Global System
            globalSystem.Gain *
            // Global MIDI
            // It corresponds to CC volume, so volume is squared.
            float.Pow(globalMIDI.Volume, 2) *
            // Channel System
            channelSystem.Gain;
        // Channel MIDI are the volume/expression controllers
    }
    
    /// <summary>
    /// Sets the channel to a given MIDI patch. Note that this executes a program change.
    /// </summary>
    /// <param name="patch">The MIDI patch to set the channel to.</param>
    internal void SetPatch(MidiPatch patch) 
    {
        SetBankMSB(patch.BankMSB);
        SetBankLSB(patch.BankLSB);
        SetGSDrums(patch.IsGMGSDrum);
        ProgramChange(patch.Program);
    }
    
    /// <summary> Sets the GM/GS drum flag. </summary>
    /// <param name="drums"></param>
    internal void SetGSDrums(bool drums) 
    {
        if (drums == Patch.IsGMGSDrum) return;
        SetBankLSB(0);
        SetBankMSB(0);
        Patch = Patch with { IsGMGSDrum = drums };
    }
    
    /// <summary>Stops a note nearly instantly.</summary>
    /// <param name="midiNote">The note to stop.</param>
    /// <param name="releaseTime">In timecents, defaults to -12_000 (very short release).</param>
    internal void KillNote(int midiNote, int releaseTime = -12_000) 
    {
        NoteOffID[midiNote] = 0;
        NoteOnID[midiNote] = 0;

        var cTime = (float)SynthCore.CurrentTime;
        foreach (var v in Voices)
        {
            if (v.MidiNote != midiNote) continue;

            v.OverrideReleaseVolEnv = releaseTime; // Set release to be very short
            v.IsInRelease = false; // Force release again
            v.ReleaseVoice(cTime);
        }
    }

    internal void Free(Voice.Voice voice)
    {
        Debug.Assert(voice.LocalIndex != -1);
        Debug.Assert(voice.Channel == this);
        
        var i = voice.LocalIndex;
        (Voices[^1], Voices[i]) = (Voices[i], Voices[^1]);
        Voices.RemoveAt(Voices.Count - 1);
        
        voice.LocalIndex = -1;
        voice.Channel = null;
        
        if (Voices.Count > 0 && i != Voices.Count)
            Voices[i].LocalIndex = i;
    }

    /// <summary>Applies the <b>ChannelSnapshot</b> to this <b>MIDIChannel</b> instance.</summary>
    /// <param name="snapshot">The snapshot to apply.</param>
    internal void Apply(ChannelSnapshot snapshot) => snapshot.Apply(this);

    internal ChannelSnapshot GetSnapshot() => ChannelSnapshot.Get(this);

    /// <summary>Strictly internal, used by the sequencer for very accurate portamento recreation.</summary>
    /// <param name="midiNote"></param>
    internal void SetLastNote(int midiNote) => LastPortamentoNote = midiNote;

    internal void Destroy()
    {
        Preset = null;
        LockedControllers.SetAll(false);
        SystemParamArray.AsSpan().Clear();
        MidiParamArray.AsSpan().Clear();
        MidiControllers.AsSpan().Clear();
    }
    
    internal void ResetGeneratorOverrides() 
    {
        Generators.Overrides.AsSpan().Fill(
            Synthesizer.GENERATOR_OVERRIDE_NO_CHANGE_VALUE);
        Generators.OverridesEnabled = false;
    }
    
    internal void SetGeneratorOverride(
        Generator.Type gen,
        short value,
        bool realtime = false) 
    {
        Generators.Overrides[(int)gen] = value;
        Generators.OverridesEnabled = true;

        if (!realtime) return;

        foreach (var v in Voices)
        {
            v.Generators[(int)gen] = value;
            ComputeModulators(v);
        }
    }

    internal void ResetGeneratorOffsets() 
    {
        Generators.Offsets.AsSpan().Clear();
        Generators.OffsetsEnabled = false;
    }
    
    internal void SetGeneratorOffset(Generator.Type gen, short value) 
    {
        Generators.Offsets[(short)gen] = 
            (short)(value * Generator.Limits[gen].NRPN);

        Generators.OffsetsEnabled = true;
        foreach (var v in Voices) ComputeModulators(v);
    }
    
    /// <summary> Mutes or unmutes a channel. </summary>
    /// <param name="isMuted">If the channel should be muted.</param>
    public void MuteChannel(bool isMuted) 
    {
        if (isMuted) StopAllNotes(true);

        IsMuted = isMuted;

        SynthCore.CallEvent(new Event.CbMuteChannel(Channel, isMuted));
    }
    
    internal void ResetDrumParams() 
    {
        if (!DrumChannel || SynthCore.SystemParameters.DrumLock)
            return;

        var i = 0;
        var isXG = ChannelSystem == Midi.System.XG;

        foreach (ref var p in DrumParams.AsSpan())
        {
            var rcGain = Engine.Channel.Reset.DefaultDrumReverb[i++] / 127f;
            p = new DrumParameters(
                Pitch: 0,
                Gain: 1,
                ExclusiveClass: 0,
                Pan: 64,
                ReverbGain: rcGain,
                ChorusGain: isXG ? rcGain : 0, // Mirror reverb on XG only, GS has no chorus by default
                DelayGain: 0, // No drums have delay
                RxNoteOn: true,
                RxNoteOff: false
            );
        }
    }
    
    internal void ComputeModulatorsAll(int sourceUsesCC, int sourceIndex)
    {
        Debug.Assert(sourceUsesCC is >= -1 and <= 1);

        foreach (var v in Voices)
            ComputeModulators(v, sourceUsesCC, sourceIndex);
    }
    
    internal void SetBankMSB(int bankMSB)
    {
        if (SystemParameters.PresetLock) return;
        Patch = Patch with { BankMSB = bankMSB };
    }

    internal void SetBankLSB(int bankLSB) 
    {
        if (SystemParameters.PresetLock) return;
        Patch = Patch with { BankLSB = bankLSB };
    }
    
    /// <summary> Sets drums on channel. </summary>
    /// <param name="isDrum"></param>
    internal void SetDrumFlag(bool isDrum) 
    {
        if (
            DrumChannel == isDrum ||
            Preset == null ||
            SystemParameters.PresetLock) return;

        DrumChannel = isDrum;
        UpdateInternalParams();
    }

    internal short ComputeModulator(
        Voice.Voice voice, short pitchWheel, int modulatorIndex) =>
            CptModulator.Compute(
            this, voice, pitchWheel, modulatorIndex);

    internal void ComputeModulators(
        Voice.Voice voice, int sourceUsesCC = -1, int sourceIndex = 0)
    {
        Debug.Assert(sourceUsesCC is >= -1 and <= 1);
        CptModulator.ComputeAll(this, voice, sourceUsesCC, sourceIndex);
    }
    
    // SF2Channel Interface

    public ReadOnlySpan<short> GetMidiControllers => MidiControllers;

    public (int Pressure, int PitchWheel, float PitchWheelRange) GetMidiParameters => (
        MidiParamArray.Pressure,
        MidiParamArray.PitchWheel,
        MidiParamArray.PitchWheelRange);
}