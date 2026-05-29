using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Sysex;

namespace SpessaSharp.Synthesizer;

/// <summary>The core synthesis engine of spessasynth.</summary>
public sealed class SpessaSharpProcessor
{
    /// <summary>Core synthesis engine.</summary>
    private readonly Synthesizer _synthCore;
    
    /// <summary>For applying the snapshot after an override sound bank too.</summary>
    private SynthesizerSnapshot? _savedSnapshot;
    
    /// <summary>Sample rate in Hertz.</summary>
    public readonly int SampleRate;

    public Action<Event>? OnEvent;

    public Macro.Reverb ReverbMacro
    {
        get => (Macro.Reverb)_synthCore.ReverbProcessor.Macro;
        set => Macro.SetReverb(_synthCore, value);
    }
    
    public Macro.Chorus ChorusMacro
    {
        get => (Macro.Chorus)_synthCore.ChorusProcessor.Macro;
        set => Macro.SetChorus(_synthCore, value);
    }
    
    public Macro.Delay DelayMacro
    {
        get => (Macro.Delay)_synthCore.DelayProcessor.Macro;
        set => Macro.SetDelay(_synthCore, value);
    }
    
    public void Process(
        Span<short> output, 
        int? startIndex = 0, 
        int? sampleCount = null)
        => _synthCore.Process(output, startIndex ?? 0, sampleCount);

    /// <summary>
    /// Renders float32 audio data to stereo outputs; buffer size must be equal or smaller than <b>maxBufferSize</b>. All float arrays must have the same length.
    /// </summary>
    /// <param name="left">The left output channel.</param>
    /// <param name="right">The right output channel.</param>
    /// <param name="startIndex">Start offset of the passed arrays, rendering starts at this index, defaults to 0.</param>
    /// <param name="sampleCount">The length of the rendered buffer, defaults to float32array length - startOffset.</param>
    public void Process(
        ArraySegment<float> left, 
        ArraySegment<float> right, 
        int? startIndex = 0, 
        int? sampleCount = null)
        => _synthCore.Process(left, right, startIndex ?? 0, sampleCount);

    /// <summary>
    /// Renders float32 audio data to stereo outputs; buffer size must be equal or smaller than <b>maxBufferSize</b>. All float arrays must have the same length.
    /// </summary>
    /// <param name="outputs">Any number stereo pairs (L, R) to render channels separately into.</param>
    /// <param name="effectsLeft">The left stereo effect output buffer.</param>
    /// <param name="effectsRight">The right stereo effect output buffer.</param>
    /// <param name="startIndex">Start offset of the passed arrays, rendering starts at this index, defaults to 0.</param>
    /// <param name="samples">The length of the rendered buffer, defaults to float32array length - startOffset.</param>
    public void ProcessSplit(
        ReadOnlySpan<(
            ArraySegment<float> Left,
            ArraySegment<float> Right)> outputs,
        Span<float> effectsLeft,
        Span<float> effectsRight,
        int? startIndex,
        int? samples = null) 
        => _synthCore.ProcessSplit(
            outputs, effectsLeft, effectsRight, startIndex ?? 0, samples);
    
    public void SendAddress(
        int a1, int a2, int a3, ReadOnlySpan<byte> data, int offset = 0) 
    {
        var result = (Span<byte>)stackalloc byte[
            data.Length + MidiUtils.GsDataMinLen];
        SystemExclusive(MidiUtils.GsData(a1, a2, a3, data, result), offset);
    }

    /// <summary>Executes a system exclusive message for the synthesizer. </summary>
    /// <param name="syx">The system exclusive message as an array of bytes.</param>
    /// <param name="channelOffset">The channel offset to apply (default is 0).</param>
    public void SystemExclusive(
        ReadOnlySpan<byte> syx, int? channelOffset = null)
        => _synthCore.SystemExclusive(syx, channelOffset ?? 0);

    /// <summary>
    /// Executes a MIDI controller change message on the specified channel.
    /// </summary>
    /// <param name="channel">The MIDI channel to change the controller on.</param>
    /// <param name="controller">The MIDI controller number (0-127).</param>
    /// <param name="value">The value of the controller (0-127).</param>
    public void ControllerChange(int channel, Midi.CC controller, int value) 
        => _synthCore.ControllerChange(channel, controller, value);

    /// <summary>
    /// Executes a MIDI Note On message on the specified channel.
    /// Starts playing a note.
    /// </summary>
    /// <remarks>If the velocity is 0, it will be treated as a Note Off message.</remarks>
    /// <param name="channel">The MIDI channel to send the note on.</param>
    /// <param name="midiNote">The MIDI note number to play.</param>
    /// <param name="velocity">The velocity of the note, from 0 to 127.</param>
    public void NoteOn(int channel, int midiNote, int velocity) => 
        _synthCore.NoteOn(channel, midiNote, velocity);
    
    /// <summary>Executes a MIDI Note Off message on the specified channel. Stops playing a note.</summary>
    /// <param name="channel">The MIDI channel to send the note off.</param>
    /// <param name="midiNote">The MIDI note number to stop playing.</param>
    public void NoteOff(int channel, int midiNote) =>
        _synthCore.NoteOff(channel, midiNote);
    
    /// <summary>
    /// Executes a MIDI Poly Pressure (Aftertouch) message on the specified channel.
    /// This differs from the Channel Pressure in that it's per-note and not for the whole channel.
    /// </summary>
    /// <param name="channel">The MIDI channel to send the poly pressure on.</param>
    /// <param name="midiNote">The MIDI note number to apply the pressure to.</param>
    /// <param name="pressure">The pressure value, from 0 to 127.</param>
    public void PolyPressure(int channel, int midiNote, int pressure) =>
        _synthCore.PolyPressure(channel, midiNote, pressure);
    
    /// <summary>Executes a MIDI Channel Pressure (Aftertouch) message on the specified channel. </summary>
    /// <param name="channel">The MIDI channel to send the channel pressure on.</param>
    /// <param name="pressure">The pressure value, from 0 to 127.</param>
    public void ChannelPressure(int channel, int pressure) =>
        _synthCore.ChannelPressure(channel, pressure);
    
    /// <summary> Executes a MIDI Pitch Wheel message on the specified channel. </summary>
    /// <param name="channel">The MIDI channel to send the pitch wheel on.</param>
    /// <param name="pitch">The new pitch value: 0-16383</param>
    /// <param name="midiNote">The MIDI note number (optional), pass -1 for the regular pitch wheel.</param>
    public void PitchWheel(int channel, int pitch, int? midiNote = null) =>
        _synthCore.PitchWheel(channel, (short)pitch, midiNote);
    
    /// <summary> Executes a MIDI Program Change message on the specified channel. </summary>
    /// <param name="channel">The MIDI channel to send the program change on.</param>
    /// <param name="programNumber">programNumber The program number to change to, from 0 to 127.</param>
    public void ProgramChange(int channel, int programNumber) =>
        _synthCore.ProgramChange(channel, programNumber);

    /// <summary>Processes a raw MIDI message and allows scheduling it at a specific time.</summary>
    /// <param name="message">The MIDI message to process.</param>
    /// <param name="channelOffset">The channel offset for the message. It will be added to message's channel number if applicable</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessage(
        ArraySegment<byte> message, 
        int? channelOffset = null, 
        double? time = null) 
        => _synthCore.ProcessMessage(message, channelOffset ?? 0, time);
    
    /// <summary>Processes a raw MIDI</summary>
    /// <param name="message">The MIDI message to process.</param>
    /// <param name="channelOffset">The channel offset for the message. It will be added to message's channel number if applicable</param>
    public void ProcessMessage(
        ReadOnlySpan<byte> message, int? channelOffset = null) 
        => _synthCore.ProcessMessage(message, channelOffset ?? 0);

    /// <summary>Creates a new synthesizer engine.</summary>
    /// <param name="sampleRate">sample rate, in Hertz.</param>
    /// <param name="opts">The processor's options.</param>
    public SpessaSharpProcessor(
        int sampleRate, Synthesizer.Options? opts = null)
    {
        SampleRate = sampleRate;
        
        // Initialize the protected synth values
        var options = opts ?? Synthesizer.Options.Default;
        
        _synthCore = new Synthesizer(
            CallEvent, MissingPreset, SampleRate, options);
        
        for (var i = 0; i < Synthesizer.MIDI_CHANNEL_COUNT; i++)
            // Don't send events as we're creating the initial channels
            _synthCore.CreateMIDIChannel(false);
        
        Debug.WriteLine("SpessaSharp is ready!");
    }
    
    /// <summary>All MIDI channels of the synthesizer.</summary>
    public ReadOnlySpan<MidiChannel> MidiChannels => 
        CollectionsMarshal.AsSpan(_synthCore.MidiChannels);

    /// <summary>
    /// The global MIDI parameters of the synthesizer.
    /// These are only editable via MIDI messages.
    /// </summary>
    public ReadOnlySpan<GlobalMidiParameter> MidiParameters => 
        _synthCore.MidiParameters;

    /// <summary>
    /// The global system parameters of the synthesizer.
    /// These are only editable via the API.
    /// </summary>
    public ReadOnlySpan<GlobalSystemParameter> SystemParameters =>
        _synthCore.SystemParameters;
    
    /// <summary> Current total amount of voices that are currently playing. </summary>
    public int VoiceCount => _synthCore.VoiceCount;
    
    /// <summary>The current time of the synthesizer, in seconds. You probably should not modify this directly.</summary>
    public double CurrentTime => _synthCore.CurrentTime;
    
    /// <summary>Synthesizer's reverb processor.</summary>
    public Effect.ReverbProcessor ReverbProcessor =>
        _synthCore.ReverbProcessor;
    
    /// <summary>Synthesizer's Chorus processor.</summary>
    public Effect.ChorusProcessor ChorusProcessor =>
        _synthCore.ChorusProcessor;
    
    /// <summary>Synthesizer's Delay processor.</summary>
    public Effect.DelayProcessor DelayProcessor => _synthCore.DelayProcessor;
    
    /// <summary>The sound bank manager, which manages all sound banks and presets.</summary>
    public SoundBankManager SoundBankManager => _synthCore.SoundBankManager;
    
    /// <summary>Handles the custom key overrides: velocity and preset</summary>
    public KeyModifier.Manager KeyModifierManager =>
        _synthCore.KeyModifierManager;
    
    /// <summary>A handler for missing presets during program change. By default, it warns to console.</summary>
    /// <param name="patch">The MIDI patch that was requested.</param>
    /// <param name="system">The MIDI System for the request.</param>
    /// <returns>If a BasicPreset instance is returned, it will be used by the channel.</returns>
    public BasicPreset? OnMissingPreset(MidiPatch patch, Midi.System system)
    {
        Debug.WriteLine(
            $"[WARN] No preset found for ${patch.ToMidiString()
            }! Did you forget to add a sound bank?");
        return null;
    }
    
    /// <summary>Sets a system parameter of the synthesizer.</summary>
    /// <param name="param">The type and value of the system parameter to set.</param>
    public void Set(GlobalSystemParameter param) =>
        _synthCore.Set(param);
    
    /// <summary>
    /// Executes a full synthesizer reset.
    /// This will reset all controllers to their default values, except for the locked controllers.
    /// </summary>
    public void Reset() => _synthCore.Reset();
    
    /// <summary>Applies the snapshot to the synth.</summary>
    /// <param name="snapshot">The snapshot to apply.</param>
    public void Apply(SynthesizerSnapshot snapshot) 
    {
        _savedSnapshot = snapshot;
        snapshot.Apply(_synthCore);
    }

    /// <summary>Gets a synthesizer snapshot from this processor instance.</summary>
    /// <returns></returns>
    public SynthesizerSnapshot GetSnapshot() =>
        SynthesizerSnapshot.Get(_synthCore);
    
    /// <summary>Sets the embedded sound bank.</summary>
    /// <param name="bank">The sound bank file to set.</param>
    /// <param name="offset">The bank offset of the embedded sound bank.</param>
    internal void SetEmbeddedSoundBank(ArraySegment<byte> bank, int offset) 
    {
        // The embedded bank is set as the first bank in the manager,
        // With a special ID that is randomized.
        var loadedFont = SoundBank.SoundBank.From(bank);
        _synthCore.SoundBankManager.Add(
            loadedFont,
            Synthesizer.EMBEDDED_SOUND_BANK_ID,
            offset,
            false);

        // Rearrange so the embedded is first (most important as it overrides all others)
        var order = _synthCore.SoundBankManager.PriorityOrder;
        order.RemoveAt(order.Count - 1);
        order.Insert(0, Synthesizer.EMBEDDED_SOUND_BANK_ID);
        _synthCore.SoundBankManager.PriorityOrder = order;

        // Apply snapshot again if applicable
        if (_savedSnapshot != null) Apply(_savedSnapshot);

        Debug.WriteLine(
            $"Embedded sound bank set at offset {offset}");
    }
    
    /// <summary>Removes the embedded sound bank from the synthesizer.</summary>
    internal void ClearEmbeddedBank() 
    {
        if (_synthCore.SoundBankManager.SoundBankList.Any(
                s => s.ID == Synthesizer.EMBEDDED_SOUND_BANK_ID))
            _synthCore.SoundBankManager.DeleteSoundBank(
                Synthesizer.EMBEDDED_SOUND_BANK_ID);
    }
    
    /// <summary>Creates a new MIDI channel and adds it to the synthesizer.</summary>
    public void CreateMIDIChannel() => 
        _synthCore.CreateMIDIChannel(true);

    /// <summary> Stops all notes on all channels. </summary>
    /// <param name="force">If true, all notes are stopped immediately, otherwise they are stopped gracefully.</param>
    public void StopAllChannels(bool force = false) => 
        _synthCore.StopAllChannels(force);

    /// <summary> Destroy the synthesizer processor, clearing all channels and voices. This is irreversible, so use with caution.</summary>
    public void DestroySynthProcessor() => _synthCore.Destroy();
    
    /// <summary> Clears the synthesizer's voice cache. </summary>
    public void ClearCache() => _synthCore.ClearCache();

    /// <summary> Gets voices for a preset. </summary>
    /// <remarks> Intended to be used by the sequencer.</remarks>
    /// <param name="preset">The preset to get voices for.</param>
    /// <param name="midiNote">The MIDI note to use.</param>
    /// <param name="velocity">The velocity to use.</param>
    /// <returns>is an array of voices.</returns>
    internal Synthesizer.CachedVoiceList GetVoicesForPreset(
        BasicPreset preset, int midiNote, int velocity) =>
        _synthCore.GetVoicesForPreset(preset, midiNote, velocity);

    // Private methods
    
    /// <summary> Calls synth event</summary>
    /// <param name="ev">The event name and data</param>
    private void CallEvent(Event ev) => 
        OnEvent?.Invoke(ev);

    private BasicPreset? MissingPreset(
        MidiPatch patch, 
        Midi.System system) => OnMissingPreset(patch, system);
}