using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Effects.GS;
using SpessaSharp.Synthesizer.Engine.Effects.GS.Insertion;
using SpessaSharp.Synthesizer.Engine.Effects.XG;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Sysex;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer;

/// <summary> The core synthesis engine which interacts with channels and holds all the synth parameters. </summary>
public sealed class Synthesizer
{
    /// <summary>Buffer size is recommended to be very small, as this is the interval between modulator updates and LFO updates</summary>
    public const int SPESSA_BUFSIZE = 128;

    public const int VOICE_CAP = 350;
    public const Midi.System DefaultMode = Midi.System.GS;
    public const short GENERATOR_OVERRIDE_NO_CHANGE_VALUE = short.MaxValue;
    /// <summary>The program number of GS User Drum Set 1.</summary>
    public const int GS_USER_DRUM_1 = 64;
    /// <summary>The program number of GS User Drum Set 2.</summary>
    public const int GS_USER_DRUM_2 = 65;
    
    /// <summary>This sounds way nicer for an instant hi-hat cutoff</summary>
    public const float MIN_EXCLUSIVE_LENGTH = .07f;

    /// <summary>
    /// This gain factor ensures that spessasynth doesn't stay too loud.
    /// You can set the `gain` system parameter to an inverse of it to negate the effect.
    /// </summary>
    public const float SPESSASYNTH_GAIN_FACTOR = .6f;

    /// <summary>
    /// If the note is released faster than that, it forced to last that long
    /// This is used mostly for drum channels, where a lot of midis like to send instant note off after a note on
    /// </summary>
    public const float MIN_NOTE_LENGTH = .03f;

    public const int MIDI_CHANNEL_COUNT = 16;
    public const int MIDI_DRUM_CHANNEL = 9;

    /// <summary>
    /// MU2000 has 4 insertion effects. We can do more but leave it at 4 for now
    /// </summary>
    public const int XG_INSERTION_COUNT = 4;

    /// <summary>Used globally to identify the embedded sound bank. This is used to prevent the embedded bank from being deleted.</summary>
    internal static readonly string EMBEDDED_SOUND_BANK_ID =
        $"SPESSASHARP_EMBEDDED_BANK_{Guid.NewGuid()}_DO_NOT_DELETE";

    /// <summary>
    /// This is needed because effects (regular ones) are send straight from the mono signal, whereas
    /// insertion effects receive the panned audio (twice), which reduces gain by a factor of cos(pi/4) * cos(pi/4) (master pan + voice pan).
    /// This reverses it.
    /// 1 / Math.cos(Math.PI / 4) ** 2 == 2
    /// </summary>
    public const float EFX_SENDS_GAIN_CORRECTION = 2;

    /// <summary>Initialization options of the Synthesizer</summary>
    /// <param name="MaxBufferSize">
    /// The maximum buffer size the synthesizer can render at once.
    /// Attempting to <see cref="Synthesizer.Process(Span{short}, int, int?)"/> more samples than this will result in an error.
    /// Defaults to 128.</param>
    /// <param name="EventsEnabled">Indicates if the event system is enabled. This can be changed later.</param>
    /// <param name="InitialTime">The initial time of the synth, in seconds.</param>
    /// <param name="EffectsEnabled">Indicates if the effects are enabled. This can be changed later.</param>
    /// <param name="GSReverbProcessor">Optional custom GS reverb processor for the synthesizer. Leave undefined to use the default.</param>
    /// <param name="GSChorusProcessor">Optional custom GS chorus processor for the synthesizer. Leave undefined to use the default.</param>
    /// <param name="GSDelayProcessor">Optional Custom GS delay processor for the synthesizer. Leave undefined to use the default.</param>
    public readonly record struct Options(
        int MaxBufferSize,
        bool EventsEnabled,
        float InitialTime,
        bool EffectsEnabled,
        GSEffect.ReverbProcessor? GSReverbProcessor = null,
        GSEffect.ChorusProcessor? GSChorusProcessor = null,
        GSEffect.DelayProcessor? GSDelayProcessor = null)
    {
        public static readonly Options Default = new()
        {
            EventsEnabled = true,
            EffectsEnabled = true,
            InitialTime = 0,
            MaxBufferSize = SPESSA_BUFSIZE,
        };
    }

    /// <summary> Looping mode of the sample. </summary>
    public enum SampleLoopingMode
    {
        /// <summary>No loop.</summary>
        m0,

        /// <summary>Loop.</summary>
        m1,

        /// <summary>UNOFFICIAL: polyphone 2.4 added start on release.</summary>
        m2,

        /// <summary> Loop then play when released. </summary>
        m3,
    }

    ///<summary>The available interpolation types of the synthesizer.</summary>
    public enum InterpolationType
    {
        Linear,
        NearestNeighbor,
        Hermite,
    }

    /// <summary>Gain smoothing for rapid volume changes. Must be run EVERY SAMPLE</summary>
    private const float GAIN_SMOOTHING_FACTOR = 0.01f;

    /// <summary>Pan smoothing for rapid pan changes</summary>
    private const float PAN_SMOOTHING_FACTOR = 0.05f;

    /// <summary>Unused voices of this synthesizer</summary>
    public readonly List<Voice> FreeVoices;

    /// <summary>Active Voices of this synthesizer</summary>
    public readonly List<Voice> Voices;

    /// <summary>All MIDI channels of the synthesizer.</summary>
    public readonly List<MidiChannel> MidiChannels = new(64);

    /// <summary>The maximum allowed buffer size to render.</summary>
    public readonly int MaxBufferSize;

    /// <summary>The buffer to use when rendering a voice.</summary>
    public readonly float[] VoiceBuffer;

    /// <summary>The insertion processor's left input buffer.</summary>
    public readonly float[] InsertionInputL;

    /// <summary>The GS insertion processor's right input buffer.</summary>
    public readonly float[] InsertionInputR;

    /// <summary>The GS reverb processor's input buffer.</summary>
    public readonly float[] GSReverbInput;

    /// <summary>The GS chorus processor's input buffer.</summary>
    public readonly float[] GSChorusInput;

    /// <summary>The GS delay processor's input buffer.</summary>
    public readonly float[] GSDelayInput;

    /// <summary>Delay is not used outside SC-88+ MIDIs, this is an optimization.</summary>
    public bool GSDelayActive;

    /// <summary>
    /// The XG reverb block's left input buffer.
    /// XG effects are stereo, unlike GS effects (except insertion).
    /// </summary>
    internal readonly float[] XGReverbInputL;
    /// <summary> The XG reverb block's right input buffer. </summary>
    internal readonly float[] XGReverbInputR;

    /// <summary> The XG chorus block's left input buffer. </summary>
    internal readonly float[] XGChorusInputL;
    /// <summary> The XG chorus block's right input buffer. </summary>
    internal readonly float[] XGChorusInputR;

    /// <summary> The XG variation block's left input buffer (system mode). </summary>
    internal readonly float[] XGVariationInputL;
    /// <summary> The XG variation block's right input buffer (system mode). </summary>
    internal readonly float[] XGVariationInputR;

    /// <summary>The sound bank manager, which manages all sound banks and presets.</summary>
    public readonly SoundBankManager SoundBankManager;

    public readonly int SampleRate;

    /// <summary>
    /// This.tunings[program * 128 + key] = midiNote,cents (fraction).
    /// All MIDI Tuning Standard tunings, 128 keys for each of 128 programs.
    /// -1 means no change.
    /// </summary>
    public readonly float[] Tunings = new float[128 * 128];

    /// <summary>
    /// An object indicating if a Global MIDI parameter, at the equivalent key, is locked
    /// (i.e., not allowed changing).
    /// A locked parameter cannot be modified.
    /// </summary>
    internal readonly BitArray LockedParameters = new(GlobalMidiParameter.Len);

    /// <summary>The global MIDI parameters of the synthesizer.</summary>
    public readonly GlobalMidiParameter[] MidiParameters =
        GlobalMidiParameters.Default.ToArray(); // Copy, not set!

    /// <summary>The system parameters of the synthesizer.</summary>
    public readonly GlobalSystemParameter[] SystemParameters =
        GlobalSystemParameters.Default.ToArray(); // Copy, not set!

    /// <summary>The current time of the synthesizer, in seconds.</summary>
    public double CurrentTime;

    /// <summary> Synth's default (reset) preset. </summary>
    public SynthPatch? DefaultPreset;
    
    /// <summary> Synth's default (reset) drum preset. </summary>
    public SynthPatch? DrumPreset;
    
    /// <summary> Gain smoothing factor, adjusted to the sample rate. </summary>
    public readonly float GainSmoothingFactor;

    /// <summary> Pan smoothing factor, adjusted to the sample rate. </summary>
    public readonly float PanSmoothingFactor;

    /// <summary> Calls when an event occurs. </summary>
    public readonly Action<Event> EventCallbackHandler;

    public delegate BasicPreset? MissingPresetHandler(
        MidiPatch path, Midi.System system);

    public readonly MissingPresetHandler MissingPreset;

    internal readonly record struct CachedVoiceList(
        CachedVoice? Single,
        ArraySegment<CachedVoice>? Multi);

    /// <summary>
    /// Cached voices for all presets for this synthesizer.
    /// Nesting is calculated in getCachedVoiceIndex, returns a list of voices for this note.
    /// </summary>
    private readonly Dictionary<(MidiPatch Patch, byte Key, byte Vel),
        CachedVoiceList> _cachedVoices = new (200);
    
    private readonly CachedVoice.Base.Cache _cvbCache;

    /// <summary>
    /// Locks or unlocks a given Global MIDI Parameter.
    /// This prevents any changes to it until it's unlocked.
    /// </summary>
    /// <param name="parameter">The Global MIDI Parameter to lock.</param>
    /// <param name="isLocked">If the parameter should be locked.</param>
    public void LockParameter(
        GlobalMidiParameter.Type parameter, bool isLocked) =>
        LockedParameters[(int)parameter] = isLocked;

    /// <summary>Sets a system parameter of the synthesizer. </summary>
    /// <param name="param">The type and value of the system parameter to set.</param>
    public void Set(GlobalSystemParameter param) =>
        GlobalSystemParameters.Set(this, param);

    /// <summary>Executes a system exclusive message for the synthesizer.</summary>
    /// <remarks>
    /// This is a rather extensive method that handles various system exclusive messages,
    /// including Roland GS, MIDI Tuning Standard, and other non-realtime messages.
    /// </remarks>
    /// <param name="syx">The system exclusive message as an array of bytes.</param>
    /// <param name="channelOffset">channelOffset The channel offset to apply (default is 0).</param>
    public void SystemExclusive(ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        channelOffset += PortSelectChannelOffset;
        var manufacturer = syx[0];
        // Ensure that the device ID matches
        var deviceID = SystemParameters.DeviceID;

        if (// The device ID can be set to "all" which it is by default
            deviceID != -1 &&
            syx[1] != 0x7f && // 0x7f means broadcast, i.e. all MIDI devices
            deviceID != syx[1])
            // Not our device ID
            return;

        switch (manufacturer)
        {
            default:
                SpessaLog.Unsupported(
                    "System Exclusive",
                    syx,
                    $"Unknown manufacturer: {manufacturer}");
                break;

            // Non realtime GM
            case 0x7e:
            // Realtime GM
            case 0x7f: 
                Universal.SystemExclusive(this, syx, channelOffset);
                break;
            
            // Roland
            case 0x41: 
                Roland.SystemExclusive(this, syx, channelOffset);
                break;

            // Yamaha
            case 0x43: 
                Yamaha.SystemExclusive(this, syx, channelOffset);
                break;

            // Port select (Falcosoft MIDI Player)
            // https://www.vogons.org/viewtopic.php?p=1404746#p1404746
            case 0xf5: 
                if (syx.Length < 2) return;
                PortSelectChannelOffset = (syx[1] - 1) * 16;
                // Create new port if needed
                while (MidiChannels.Count <= PortSelectChannelOffset)
                {
                    SpessaLog.Info(
                        $"Port select, channel offset {
                        PortSelectChannelOffset}. Creating a new port!");
                    for (var i = 0; i < 16; i++)
                        CreateMIDIChannel(true);
                }
                break;
        }
    }

    /// <summary> Current total amount of voices that are currently playing. </summary>
    public int VoiceCount => Voices.Count;

    /// <summary>
    /// The synthesizer's GS reverb processor.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
    /// </summary>
    internal readonly GSEffect.ReverbProcessor GSReverbProcessor;

    /// <summary>
    /// The synthesizer's GS chorus processor.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
    /// </summary>
    internal readonly GSEffect.ChorusProcessor GSChorusProcessor;

    /// <summary>
    /// The synthesizer's GS delay processor.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>gm</c> <c>gm2</c> or <c>gs</c>.
    /// </summary>
    internal readonly GSEffect.DelayProcessor GSDelayProcessor;

    /// <summary> Insertion is not used outside SC-88Pro+ MIDIs, this is an optimization. </summary>
    internal bool GSInsertionActive;

    /// <summary>
    /// The synthesizer's XG variation block.
    /// Variation is public so voice render can check if sends should be routed to it.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>xg</c>.
    /// </summary>
    internal readonly XGVariationBlock XGVariationBlock;

    /// <summary>
    /// The synthesizer's XG reverb block.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>xg</c>.
    /// </summary>
    internal readonly XGReverbBlock XGReverbBlock;
    /// <summary>
    /// The synthesizer's XG chorus block.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>xg</c>.
    /// </summary>
    internal readonly XGChorusBlock XGChorusBlock;
    /// <summary>
    /// XG insertion effect blocks.
    /// Used when <see cref="GlobalMidiParameter.Type.System"/> is <c>xg</c>.
    /// </summary>
    internal readonly List<XGInsertionBlock> XGInsertionBlocks = [];

    /// <summary>
    /// A sysEx may set a "Part" (channel) to receive on a different channel number.
    /// This slows down the access, so this toggle tracks if it's enabled or not.
    /// </summary>
    public bool CustomChannelNumbers { get; internal set; }

    /// <summary>Sets a global MIDI parameter of the synthesizer.</summary>
    /// <param name="param">The type and value of the global MIDI parameter to set.</param>
    internal void Set(GlobalMidiParameter param) =>
        GlobalMidiParameters.Set(this, param);

    /// <summary> The fallback GS processor when the requested insertion is not available. </summary>
    internal readonly ThruFX GSInsertionFallback = new();

    /// <summary> The GS current insertion processor. </summary>
    internal GSEffect.GSInsertionProcessor GSInsertionProcessor;

    /// <summary>
    /// All the insertion effects available to the processor.<br/>
    /// The key is the EFX type stored as MSB lshift 8 | LSB
    /// </summary>
    internal readonly FrozenDictionary<int, GSEffect.GSInsertionProcessor>
        InsertionEffects;

    /// <summary> For F5 system exclusive </summary>
    internal int PortSelectChannelOffset;

    /// <summary>For GS insertion snapshot tracking<br/>
    /// 20 parameters (0-19) + 3 sends<br/>
    /// Index to gs is Addr3 - 3 (for example EFX PARAMETER 1 is 0x03 and here it's 0)<br/>
    /// Note: 255 means "no change"</summary>
    internal readonly byte[] GSInsertionParams = new byte[23];

    /// <summary>For smoothing the filter cutoff frequency.</summary>
    internal readonly float SmoothingConstant;

    /// <summary>Last time the priorities were assigned. Used to prevent assigning priorities multiple times when more than one voice is triggered during a quantum. </summary>
    private double _lastPriorityAssignmentTime;

    private readonly record struct EventQueueData(
        ArraySegment<byte>? AsSegment,
        (byte A, byte? B, byte? C)? AsInline);

    /// <summary>Synth's event queue from the main thread</summary>
    private readonly PriorityQueue<
            (EventQueueData Message, int ChannelOffset), double>
        _eventQueue = new(64);

    /// <summary>The time of a single sample, in seconds.</summary>
    private readonly double _sampleTime;

    internal Synthesizer(
        Action<Event> eventCallback,
        MissingPresetHandler missingPreset,
        int sampleRate,
        Options options)
    {
        // Force static init now
        RuntimeHelpers.RunClassConstructor(typeof(UnitConverter).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RenderVoice).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ModulationEnvelope).TypeHandle);

        // 
        SmoothingConstant =
            LowpassFilter.FILTER_SMOOTHING_FACTOR * (44_100f / sampleRate);
        SoundBankManager = new SoundBankManager(UpdatePresetList);
        _cvbCache = new CachedVoice.Base.Cache(sampleRate);

        Tunings.AsSpan().Fill(-1);
        GSInsertionParams.AsSpan().Fill(255);

        GSInsertionProcessor = GSInsertionFallback;

        EventCallbackHandler = eventCallback;
        MissingPreset = missingPreset;
        SampleRate = sampleRate;
        _sampleTime = 1d / sampleRate;
        CurrentTime = options.InitialTime;
        Set((
            GlobalSystemParameter.Type.EffectsEnabled,
            options.EffectsEnabled));
        Set((
            GlobalSystemParameter.Type.EventsEnabled,
            options.EventsEnabled));
        MaxBufferSize = options.MaxBufferSize;
        
        // For GS user drum set
        SoundBankManager.SystemGetter = () => MidiParameters.System;
        // These smoothing factors were tested on 44,100 Hz, adjust them to target sample rate here
        // Volume  smoothing factor
        GainSmoothingFactor = GAIN_SMOOTHING_FACTOR * (44_100f / sampleRate);
        // Pan smoothing factor
        PanSmoothingFactor = PAN_SMOOTHING_FACTOR * (44_100f / sampleRate);

        var bufSize = MaxBufferSize;
        // Initialize effects
        GSReverbProcessor =
            options.GSReverbProcessor ?? new GSReverb(sampleRate, bufSize);
        GSChorusProcessor =
            options.GSChorusProcessor ?? new GSChorus(sampleRate, bufSize);
        GSDelayProcessor =
            options.GSDelayProcessor ?? new GSDelay(sampleRate, bufSize);
        
        XGReverbBlock = new XGReverbBlock(sampleRate, bufSize);
        XGChorusBlock = new XGChorusBlock(sampleRate, bufSize);
        XGVariationBlock = new XGVariationBlock(sampleRate, bufSize);
        
        for (var i = 0; i < XG_INSERTION_COUNT; i++)
            XGInsertionBlocks.Add(new XGInsertionBlock(sampleRate, bufSize));

        // Initialize buffers
        VoiceBuffer = new float[bufSize];
        InsertionInputL = new float[bufSize];
        InsertionInputR = new float[bufSize];
        GSReverbInput = new float[bufSize];
        GSChorusInput = new float[bufSize];
        GSDelayInput = new float[bufSize];
        
        XGReverbInputL = new float[bufSize];
        XGReverbInputR = new float[bufSize];
        XGChorusInputL = new float[bufSize];
        XGChorusInputR = new float[bufSize];
        XGVariationInputL = new float[bufSize];
        XGVariationInputR = new float[bufSize];

        // Register insertion
        var insertions = new Dictionary<int, GSEffect.GSInsertionProcessor>();
        foreach (var proc in GSEffect.GSInsertionProcessor.List)
        {
            var p = proc(SampleRate, MaxBufferSize);
            insertions[p.Type] = p;
        }

        InsertionEffects = insertions.ToFrozenDictionary();

        ResetGSInsertionParams(); // Initial setup

        // Initialize voices
        var voiceCap = SystemParameters.VoiceCap;
        FreeVoices = new List<Voice>(voiceCap);
        Voices = new List<Voice>(voiceCap);
        AllocateNewVoices(voiceCap);
    }

    public void ControllerChange(
        int channel, Midi.CC controller, int value)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.ControllerChange(controller, value);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .ControllerChange(controller, value);
    }

    public void NoteOn(int channel, int midiNote, int velocity)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.NoteOn(midiNote, velocity);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .NoteOn(midiNote, velocity);
    }

    public void NoteOff(int channel, int midiNote)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.NoteOff(midiNote);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .NoteOff(midiNote);
    }

    public void PolyPressure(int channel, int midiNote, int pressure)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.PolyPressure(midiNote, pressure);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .PolyPressure(midiNote, pressure);
    }

    public void ChannelPressure(int channel, int pressure)
    {
        var param = (ChannelMidiParameter.Type.Pressure, pressure);

        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.Set(param);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset].Set(param);
    }

    public void PitchWheel(int channel, short pitch, int? midiNote = null)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.PitchWheel(pitch, midiNote);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .PitchWheel(pitch, midiNote);
    }

    public void ProgramChange(int channel, int programNumber)
    {
        if (CustomChannelNumbers)
        {
            foreach (var ch in MidiChannels)
                if (ch.MidiParameters.RxChannel == channel)
                    ch.ProgramChange(programNumber);
            return;
        }

        MidiChannels[channel + PortSelectChannelOffset]
            .ProgramChange(programNumber);
    }

    /// <summary>Assigns the first available voice for use. If none available, will assign priorities.</summary>
    /// <returns></returns>
    public Voice AssignVoice()
    {
        if (FreeVoices.Count > 0)
        {
            var v = FreeVoices[^1];
            Debug.Assert(v.GlobalIndex == -1);
            Debug.Assert(v.LocalIndex == -1);
            Debug.Assert(v.Channel == null);

            FreeVoices.RemoveAt(FreeVoices.Count - 1);
            Voices.Add(v);
            // Prevent this voice from being stolen
            v.Priority = int.MaxValue;
            v.GlobalIndex = Voices.Count - 1;
            return v;
        }

        // No match, assign priorities
        if (SystemParameters.AutoAllocateVoices)
        {
            var newVoiceCap = SystemParameters.VoiceCap + 1;
            SpessaLog.Info(
                $"Allocating a new voice, total count {newVoiceCap}.");

            // Allocate a new voice and return it
            AllocateNewVoices(1);
            Set((GlobalSystemParameter.Type.VoiceCap, newVoiceCap));
            return AssignVoice();
        }

        AssignVoicePriorities();

        var lowest = Voices[0];
        foreach (var voice in Voices)
            if (voice.Priority < lowest.Priority)
                lowest = voice;
        lowest.Priority = int.MaxValue;
        return lowest;
    }

    /// <summary>Stops all notes on all channels.</summary>
    /// <param name="force">If true, all notes are stopped immediately, otherwise they are stopped gracefully.</param>
    public void StopAllChannels(bool force)
    {
        SpessaLog.Info("Stop all received!");
        foreach (var channel in MidiChannels)
            channel.StopAllNotes(force);
    }

    /// <summary>Processes a raw MIDI message.</summary>
    /// <param name="message">The message to process.</param>
    /// <param name="channelOffset">The channel offset for the message.</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessage(
        ReadOnlySpan<byte> message, int channelOffset = 0, double? time = null)
    {
        if (time == null || time <= CurrentTime)
        {
            ProcessMessageInternal(message, channelOffset);
            return;
        }

        EventQueueData data;

        if (message.Length > 3)
            data = new EventQueueData(
                AsSegment: message.ToArray(), AsInline: null);

        else
        {
            (byte A, byte? B, byte? C) bytes = (0, null, null);
            bytes.A = message[0];
            if (message.Length > 1) bytes.B = message[1];
            if (message.Length > 2) bytes.C = message[2];

            data = new EventQueueData(AsSegment: null, AsInline: bytes);
        }

        _eventQueue.Enqueue((data, channelOffset), time.Value);
    }

    /// <summary>Processes a raw MIDI message.</summary>
    /// <param name="message">The message to process.</param>
    /// <param name="channelOffset">The channel offset for the message.</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessage(
        MidiMessage message, int channelOffset = 0, double? time = null)
    {
        var len = 1 + message.Data.Count;
        var data = len >= 256
            ? new byte[len]
            : stackalloc byte[len];

        data[0] = message.StatusByte.Byte;
        message.Data.AsSpan().CopyTo(data[1..]);
        ProcessMessage(data, channelOffset, time);
    }

    /// <summary>Processes multiple MIDI messages.</summary>
    /// <param name="messages">The messages to process.</param>
    /// <param name="channelOffset">The channel offset for the messages</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessages(
        ReadOnlySpan<ArraySegment<byte>> messages, 
        int channelOffset = 0, 
        double? time = null)
    {
        foreach (var msg in messages)
            ProcessMessage(msg, channelOffset, time);
    }
    
    /// <summary>Processes multiple MIDI messages.</summary>
    /// <param name="messages">The messages to process.</param>
    /// <param name="channelOffset">The channel offset for the messages</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessages(
        ReadOnlySpan<MidiMessage> messages, 
        int channelOffset = 0, 
        double? time = null)
    {
        foreach (var msg in messages)
            ProcessMessage(msg, channelOffset, time);
    }

    public void Destroy() 
    {
        Voices.Clear();
        FreeVoices.Clear();

        foreach (var c in MidiChannels) c.Destroy();

        ClearCache();
        MidiChannels.Clear();
        SoundBankManager.Destroy();
    }
    
    /// <summary> </summary>
    /// <param name="channel">Channel to get voices for</param>
    /// <param name="midiNote">The MIDI note to use</param>
    /// <param name="velocity">The velocity to use</param>
    /// <returns>An array of Voices</returns>
    internal CachedVoiceList GetVoices(
        int channel, int midiNote, int velocity)
    {
        var channelObject = MidiChannels[channel];

        var preset = channelObject.Preset;

        // Warning is handled in program change
        return preset == null
            ? new CachedVoiceList(null, ArraySegment<CachedVoice>.Empty) 
            : GetVoicesForPreset(preset, (byte)midiNote, (byte)velocity);
    }
    
    public void CreateMIDIChannel(bool sendEvent) 
    {
        var channel = new MidiChannel(
            this, DefaultPreset, DrumPreset, MidiChannels.Count);
        MidiChannels.Add(channel);
        if (sendEvent) CallEvent(Event.OfChannelAdded());
    }
    
    /// <summary>Executes a full system reset of the synthesizer. This will reset all controllers to their default values, except for the locked controllers.</summary>
    /// <param name="system">The MIDI system to reset the synthesizer to. Defaults to <b>gs</b>.</param>
    public void Reset(Midi.System system = DefaultMode)
    {
        // Call here because there are returns in this function.
        CallEvent(new Event.CbReset(system));
        // Reset MIDI parameters
        Set(system);
        Set((GlobalMidiParameter.Type.Volume, 1f));
        Set((GlobalMidiParameter.Type.Pan, 0f));
        Set((GlobalMidiParameter.Type.KeyShift, 0));
        Set((GlobalMidiParameter.Type.FineTune, 0f));
        // Reset private props
        Tunings.AsSpan().Fill(-1); // Set all to no change
        PortSelectChannelOffset = 0;
        CustomChannelNumbers = false;
        // Hall2 default
        SetReverbMacro(4);
        // Chorus3 default
        SetChorusMacro(2);
        // Delay1 default
        SetDelayMacro(0);
        ResetInsertion();
        
        XGReverbBlock.Reset();
        XGChorusBlock.Reset();
        XGVariationBlock.Reset();
        foreach (var insertion in XGInsertionBlocks)
            insertion.Reset();

        // Avoid crashing
        if (DrumPreset == null || DefaultPreset == null) return;

        // Reset GS user drums
        if (!SystemParameters.UserDrumLock)
            foreach (var userDrum in SoundBankManager.UserDrumSets)
                userDrum.Reset();
        
        // Reset channels
        // Do not send CC changes as we call reset
        foreach (var ch in MidiChannels)
            ch.Reset(false);
        
        // Update if the effects should still be active.
        UpdateActiveGSEffects();
    }

    public void Process(
        Span<short> output,
        int startIndex = 0,
        int? sampleCount = null)
    {
        Debug.Assert(output.Length % 2 == 0);
        var len = output.Length / 2;
        var buffer = Util.Rent<float>(output.Length);

        try
        {
            buffer.AsSpan().Clear();
            var left = buffer[..len];
            var right = buffer[len..];

            Process(left, right, startIndex, sampleCount);
            AudioUtil.Interleave(left, right, output);
        }
        finally
        {
            Util.Return(buffer);
        }
    }
    
    /// <summary>
    /// The main rendering pipeline, renders all voices and processes the effects
    /// </summary>
    /// <remarks>
    /// <code>
    /// <![CDATA[
    ///                   ┌────────────────────────────────┐
    ///                   │        Voice Processor         │
    ///                   └───────────────┬────────────────┘
    ///                                   │
    ///                   ┌───────────────┴────────────────┐
    ///                   │      Insertion Processor       │
    ///                   │      (Bypass or Process)       │
    ///                   └───────────────┬────────────────┘
    ///                                   │
    ///              ┌──────────┬─────────┼────────────────────────┐
    ///              │          │         │                        │
    ///              │          │         v                        │
    ///              │          │ ┌───────┴───────┐                │
    ///              │          │ │    Chorus     │                │
    ///              │          │ │   Processor   ├──────────┐     │
    ///              │          │ └─┬──────────┬──┘          │     │
    ///              │          │   │          │             │     │
    ///              │          │   │          │             │     │
    ///              │          │   │          │             │     │
    ///              │          │   │          │             │     │
    ///              │          │   │          v             v     v
    ///              │          │   │ ┌────────┴───────┐   ┌─┴─────┴────────┐
    ///              │          └───┼>┤     Delay      ├─>>┤     Reverb     │
    ///              │              │ │   Processor    │   │   Processor    │
    ///              │              │ └────────┬───────┘   └───────┬────────┘
    ///              │              │          │                   │
    ///              │              │          │                   │
    ///              │              │          │                   │
    ///              │              │          │                   │
    ///              v              v          v                   v
    ///    ┌─────────┴──────────────┴──────────┴───────────────────┴────┐
    ///    │                        Stereo Output                       │
    ///    └────────────────────────────────────────────────────────────┘
    /// ]]>
    /// </code>
    /// Each channel's dry signal is also copied to the optional channelOutputs pairs for visualization only.
    /// The pipeline is quite similar to the one on SC-8850 manual page 78.
    /// All output arrays must be the same length, the method will crash otherwise.
    /// </remarks>
    /// <param name="left">The left output channel.</param>
    /// <param name="right">The right output channel.</param>
    /// <param name="startIndex">The index to start writing at into the output buffer.</param>
    /// <param name="samples">The amount of samples to write.</param>
    /// <param name="channelOutputs">Optional stereo channel outputs for visualization _only_. These shouldn't be added to the direct outputs.</param>
    public void Process(
        ArraySegment<float> left,
        ArraySegment<float> right,
        int startIndex = 0,
        int? samples = null,
        ArraySegment<ArraySegment<ArraySegment<float>>>? channelOutputs = null)
    {
        // Process event queue
        if (_eventQueue.Count > 0) 
        {
            var span = (Span<byte>)stackalloc byte[3];
            var time = CurrentTime;

            while (_eventQueue.TryPeek(out var ev, out var t) &&
                t <= time)
            {
                _eventQueue.Dequeue();
                
                if (ev.Message.AsSegment is {} segment)
                {
                    ProcessMessageInternal(segment, ev.ChannelOffset);
                    continue;
                }

                var data = ev.Message.AsInline!.Value;
                var len = 0;

                span[len++] = data.A;
                if (data.B is { } b) span[len++] = b;
                if (data.C is { } c) span[len++] = c;
                    
                ProcessMessageInternal(span[..len], ev.ChannelOffset);
            }
        }

        // Validate
        startIndex = Math.Max(startIndex, 0);
        var sampleCount = samples ?? left.Count - startIndex;

        if (sampleCount > MaxBufferSize)
            throw SpessaException.Invalid(
                $"Requested {sampleCount
                } samples, but maxBufferSize is {MaxBufferSize}");

        var isXG = MidiParameters.System == Midi.System.XG;
        var fx = SystemParameters.EffectsEnabled;
        
        // For XG, renderVoice always checks if insertion is assigned to bypass effect sends
        // Cache it here
        if (isXG && fx)
        {
            foreach (var channel in MidiChannels)
                channel.XGInsertionAssigned = false;
            foreach (var insertion in XGInsertionBlocks)
            {
                if (Util.InRange(MidiChannels, insertion.PartNumber))
                {
                    var channel = MidiChannels[insertion.PartNumber];
                    channel.XGInsertionAssigned = true;
                }
            }

            if (XGVariationBlock.InsertionMode && 
                Util.InRange(MidiChannels, XGVariationBlock.PartNumber))
            {
                var channel = MidiChannels[XGVariationBlock.PartNumber];
                channel.XGInsertionAssigned = true;
            }
        }

        // Clear the buffers
        if (isXG)
        {
            XGReverbInputL.AsSpan().Clear();
            XGReverbInputR.AsSpan().Clear();
            XGChorusInputL.AsSpan().Clear();
            XGChorusInputR.AsSpan().Clear();
            XGVariationInputL.AsSpan().Clear();
            XGVariationInputR.AsSpan().Clear();
        }
        else
        {
            GSReverbInput.AsSpan().Clear();
            GSChorusInput.AsSpan().Clear();
            if (GSDelayActive) GSDelayInput.AsSpan().Clear();
        }

        if (GSInsertionActive) 
        {
            InsertionInputL.AsSpan().Clear();
            InsertionInputR.AsSpan().Clear();
        }

        foreach (var c in MidiChannels)
        {
            c.OutputLeft.AsSpan().Clear();
            c.OutputRight.AsSpan().Clear();
        }
        
        // Process voices
        var outputCount = channelOutputs?.Count ?? 0;
        var cTime = (float)CurrentTime;
        
        for (var i = Voices.Count - 1; i >= 0; i--) 
        {
            var v = Voices[i];
            Debug.Assert(v.GlobalIndex != -1);
            Debug.Assert(v.LocalIndex != -1);
            Debug.Assert(v.Channel != null);
            
            var ch = v.Channel!;
            ch.RenderVoice(v, cTime, sampleCount);
        }

        // Process insertion effects
        if (isXG && fx)
        {
            foreach (var insertion in XGInsertionBlocks) 
            {
                if (!Util.InRange(MidiChannels, insertion.PartNumber))
                    continue;
                
                var channel = MidiChannels[insertion.PartNumber];
                insertion.ProcessInsertion(
                    channel.OutputLeft,
                    channel.OutputRight,
                    sampleCount);
            }
            
            // Variation is processed last
            // See MU128 manual, page 154
            if (XGVariationBlock.InsertionMode &&
                Util.InRange(MidiChannels, XGVariationBlock.PartNumber)) 
            {
                var channel = MidiChannels[XGVariationBlock.PartNumber];
                XGVariationBlock.ProcessInsertion(
                    channel.OutputLeft,
                    channel.OutputRight,
                    sampleCount);
            }
            
            // If a channel has insertion assigned (or variation in insertion mode) then its sends are routed globally (not in renderVoice)
            // If effectsEnabled is false it does not matter, so the code here can also be skipped.
            foreach (var ch in MidiChannels) 
            {
                if (!ch.XGInsertionAssigned) continue;

                var revSend = ch[Midi.CC.ReverbDepth] / 127f;
                var choSend = ch[Midi.CC.ChorusDepth] / 127f;
                if (revSend <= 0 && choSend <= 0) continue;

                var outputLeft = ch.OutputLeft.AsSpan(0, sampleCount); 
                var outputRight = ch.OutputRight.AsSpan(0, sampleCount);
                
                var revLeft = XGReverbInputL.AsSpan(0, sampleCount);
                var revRight = XGReverbInputR.AsSpan(0, sampleCount);

                var choLeft = XGChorusInputL.AsSpan(0, sampleCount);
                var choRight = XGChorusInputR.AsSpan(0, sampleCount);

                TensorPrimitives.MultiplyAdd(
                    outputLeft, revSend, revLeft, revLeft);
                TensorPrimitives.MultiplyAdd(
                    outputRight, revSend, revRight, revRight);
                
                TensorPrimitives.MultiplyAdd(
                    outputLeft, choSend, choLeft, choLeft);
                TensorPrimitives.MultiplyAdd(
                    outputRight, choSend, choRight, choRight);
            }
        }
        
        // Mix channel data
        for (var channel = 0; channel < MidiChannels.Count; channel++)
        {
            var chan = MidiChannels[channel];
            var outputLeft = chan.OutputLeft.AsSpan();
            var outputRight = chan.OutputRight.AsSpan();
            var midiParams = chan.MidiParameters;
            
            // Mix visualization first
            if (outputCount > 0)
            {
                var vOut = channelOutputs!.Value[channel % outputCount];
                var outL = vOut[0].AsSpan(startIndex, sampleCount);
                var outR = vOut[1].AsSpan(startIndex, sampleCount);

                TensorPrimitives.Add(outL, outputLeft[..sampleCount], outL);
                TensorPrimitives.Add(outR, outputRight[..sampleCount], outR);
            }
            
            // Straight into the insertion EFX, but only if it is active
            if (midiParams.EfxAssign && fx && GSInsertionActive)
            {
                var insertionL = InsertionInputL.AsSpan(0, sampleCount);
                var insertionR = InsertionInputR.AsSpan(0, sampleCount);
                // Index is 0-based here as it's internal
                TensorPrimitives.Add
                    (insertionL, outputLeft[..sampleCount], insertionL);
                TensorPrimitives.Add(
                    insertionR, outputRight[..sampleCount], insertionR);
                continue;
            }
            
            // Mix down normally
            {
                var outL = left.AsSpan(startIndex, sampleCount);
                var outR = right.AsSpan(startIndex, sampleCount);
                TensorPrimitives.Add(outL, outputLeft[..sampleCount], outL);
                TensorPrimitives.Add(outR, outputRight[..sampleCount], outR);                
            }
        }
        
        // Process effects
        if (fx) 
        {
            if (isXG)
            {
                // Variation system first, feeds the chorus and reverb
                if (!XGVariationBlock.InsertionMode) 
                {
                    XGVariationBlock.Process(
                        XGVariationInputL,
                        XGVariationInputR,
                        left,
                        right,
                        XGChorusInputL,
                        XGChorusInputR,
                        XGReverbInputL,
                        XGReverbInputR,
                        startIndex,
                        sampleCount);
                    
                    // Chorus feeds reverb, reverb goes straight to the output
                    XGChorusBlock.Process(
                        XGChorusInputL,
                        XGChorusInputR,
                        left,
                        right,
                        XGReverbInputL,
                        XGReverbInputR,
                        startIndex,
                        sampleCount);
                }
            }
            else
            {
                // Insertion first
                if (GSInsertionActive) 
                {
                    GSInsertionProcessor.Process(
                        InsertionInputL,
                        InsertionInputR,
                        left,
                        right,
                        GSReverbInput,
                        GSChorusInput,
                        GSDelayInput,
                        startIndex,
                        sampleCount);
                }

                // Chorus first, it feeds to reverb and delay
                GSChorusProcessor.Process(
                    GSChorusInput,
                    left,
                    right,
                    GSReverbInput,
                    GSDelayInput,
                    startIndex,
                    sampleCount);
                
                if (GSDelayActive)
                {
                    // Process delay
                    GSDelayProcessor.Process(
                        GSDelayInput,
                        left,
                        right,
                        GSReverbInput,
                        startIndex,
                        sampleCount);
                }

                // Finally process the reverb processor (it goes directly into the output buffer)
                GSReverbProcessor.Process(
                    GSReverbInput,
                    left,
                    right,
                    startIndex,
                    sampleCount);
            }
        }

        // Advance the time appropriately
        CurrentTime += sampleCount * _sampleTime;
    }

    /// <summary>Return voice to the unused voice pool</summary>
    /// <param name="voice">The unused voice</param>
    internal void Free(Voice voice)
    {
        Debug.Assert(voice.GlobalIndex != -1);
        Debug.Assert(voice.LocalIndex != -1);
        Debug.Assert(voice.Channel != null);

        var i = voice.GlobalIndex;
        FreeVoices.Add(voice);
        (Voices[^1], Voices[i]) = (Voices[i], Voices[^1]);
        Voices.RemoveAt(Voices.Count - 1);

        voice.Channel!.Free(voice);
        voice.GlobalIndex = -1;

        if (Voices.Count > 0 && i != Voices.Count)
            Voices[i].GlobalIndex = i;
    }
    
    /// <summary>Gets voices for a preset.</summary>
    /// <param name="preset">The preset to get voices for.</param>
    /// <param name="midiNote">The MIDI note to use.</param>
    /// <param name="velocity">The velocity to use.</param>
    /// <returns>Output is an array of voices.</returns>
    internal CachedVoiceList GetVoicesForPreset(
        SynthPatch preset, byte midiNote, byte velocity)
    {
        // If cached, return it!
        if (_cachedVoices.TryGetValue(
            (preset.Patch, midiNote, velocity),
            out var cached)) return cached;

        // Not cached...
        // Create the voices
        var voiceParamsList = preset.GetVoiceParameters(
            _cvbCache, midiNote, velocity);
        var voiceParamCount = voiceParamsList.Count;
        var voiceList = new CachedVoiceList(
            Single: null, Multi: ArraySegment<CachedVoice>.Empty);

        if (voiceParamCount == 0) return SetupVoiceList();

        var v = 0;
            
        foreach (var (key, voiceParams) in voiceParamsList)
        {
            var sample = voiceParams.Sample;

            if (voiceParams.Sample.GetAudioData().AsSpan().IsEmpty)
            {
                SpessaLog.Warn($"Discarding invalid sample: {sample.Name}");
                continue;
            }

            var cachedVoice = new CachedVoice(
                _cvbCache.TryGetBase(key)!,
                midiNote,
                velocity);

            switch (v)
            {
                case 0:
                    voiceList = new CachedVoiceList(
                        Single: cachedVoice, Multi: null);
                    break;
                case 1:
                    var zero = voiceList.Single!.Value;
                    voiceList = new CachedVoiceList(
                        Single: null, Multi: new CachedVoice[voiceParamCount]);
                    voiceList.Multi!.Value.AsSpan()[0] = zero;
                    goto default;
                default:
                    voiceList.Multi!.Value.AsSpan()[v] = cachedVoice;
                    break;
            }

            v++;
        }
        
        if (voiceList.Multi is {} multi)
            voiceList = voiceList with { Multi = multi[..v], };

        // Cache the voice
        return SetupVoiceList();

        CachedVoiceList SetupVoiceList()
        {
            Util.Return(voiceParamsList);
            _cachedVoices[(preset.Patch, midiNote, velocity)] = voiceList;
            return voiceList;
        }
    }
    
    public void ClearCache()
    {
        _cachedVoices.Clear();
        _cvbCache.Clear();
    }

    internal GSEffect.GSInsertionProcessorSnapshot GetInsertionSnapshot() =>
        new()
        {
            Type = GSInsertionProcessor.Type,
            Params = GSInsertionParams,
        };

    /// <summary>Copied callback so MIDI channels can call it.</summary>
    /// <param name="ev"></param>
    public void CallEvent(Event ev) => EventCallbackHandler(ev);
    
    /// <summary>
    /// Checks if we can disable insertion and delay effects.
    /// </summary>
    internal void UpdateActiveGSEffects()
    {
        if (!SystemParameters.GSInsertionLock) 
            GSInsertionActive = MidiChannels.Any(
                c => c.MidiParameters.EfxAssign);

        if (!SystemParameters.GSDelayLock)
        {
            GSDelayActive = 
                MidiParameters.System != Midi.System.XG && 
                (GSChorusProcessor.SendLevelToDelay > 0 ||
                 GSInsertionProcessor.SendLevelToDelay > 0 ||
                 MidiChannels.Any(c => c[Midi.CC.VariationDepth] > 0));
        }
    }

    /// <summary>Bad code... make sure to call only when necessary!!!</summary>
    public void PurgeCachedPatch(MidiPatch patch)
    {
        for (byte midiNote = 0; midiNote < 128; midiNote++)
            for (byte velocity = 0; velocity < 128; velocity++)
                _cachedVoices.Remove((patch, midiNote, velocity));
    }
    
    internal void SetUserDrumSetParam(
        int drumSet,
        int midiNote,
        UserDrumSetParameter.Entry entry)
    {
        var set = SoundBankManager.UserDrumSets[drumSet];
        if (set.IsSet(midiNote, entry)) return;
        // Optimization for bulk dump
        // Testcase FADED88.mid
        set.Set(midiNote, entry);
        CallEvent(new Event.CbUserDrumSetChange(
            midiNote, drumSet, entry));
        SpessaLog.GSInfo(
            $"User Drum Set {drumSet} {entry.Type}, key {midiNote}",
            entry.ValueToString());
    }
    
    internal void ResetGSInsertionParams() 
    {
        // No change
        GSInsertionParams.AsSpan().Fill(255);
        GSInsertionParams[20] = 40; // Reverb
        GSInsertionParams[21] = 0; // Chorus
        GSInsertionParams[22] = 0; // Delay
    }

    internal void ResetInsertion() 
    {
        if (SystemParameters.GSInsertionLock) 
            return;

        GSInsertionActive = false;
        GSInsertionProcessor = GSInsertionFallback;
        GSInsertionProcessor.Reset();
        ResetGSInsertionParams();
        GSInsertionProcessor.SendLevelToReverb =
            (40 / 127f) * EFX_SENDS_GAIN_CORRECTION;
        GSInsertionProcessor.SendLevelToChorus = 0;
        GSInsertionProcessor.SendLevelToDelay = 0;
        
        CallEvent(Event.CbEffectChange.OfInsertion(
            parameter: 0, 
            value: GSInsertionProcessor.Type));
    }

    internal void SetReverbMacro(int macro) =>
        Macro.SetReverb(this, macro);
    internal void SetChorusMacro(int macro) =>
        Macro.SetChorus(this, macro);
    internal void SetDelayMacro(int macro) =>
        Macro.SetDelay(this, macro);

    /// <summary>Allocates new voices.</summary>
    internal void AllocateNewVoices(int count)
    {
        for (var _ = 0; _ < count; _++)
            FreeVoices.Add(new Voice(SampleRate, MaxBufferSize));
    }
    
    private void ProcessMessageInternal(
        ReadOnlySpan<byte> message, int channelOffset) 
    {
        MidiMessage.Type status;
        var channel = 0;
        var byt = message[0];
        if (byt is >= 0x80 and < 0xf0) 
        {
            // Voice message
            status = MidiMessage.TypeOf(byt & 0xf0);
            channel = byt & 0x0f;
        } 
        else
            status = MidiMessage.TypeOf(byt);
        
        channel += channelOffset;

        // Process the event
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (status) 
        {
            case MidiMessage.Type.NoteOn: 
                var velocity = message[2];
                if (velocity > 0)
                    NoteOn(channel, message[1], velocity);
                else
                    NoteOff(channel, message[1]);
                break;

            case MidiMessage.Type.NoteOff: 
                NoteOff(channel, message[1]);
                break;

            case MidiMessage.Type.PitchWheel: 
                // LSB | (MSB << 7)
                PitchWheel(
                    channel, 
                    (short)((message[2] << 7) | message[1]));
                break;

            case MidiMessage.Type.ControllerChange: 
                ControllerChange(
                    channel,
                    (Midi.CC)message[1],
                    message[2]
                );
                break;

            case MidiMessage.Type.ProgramChange: 
                ProgramChange(channel, message[1]);
                break;

            case MidiMessage.Type.PolyPressure: 
                PolyPressure(channel, message[1], message[2]);
                break;

            case MidiMessage.Type.ChannelPressure: 
                ChannelPressure(channel, message[1]);
                break;

            case MidiMessage.Type.SystemExclusive: 
                SystemExclusive(message[1..], channelOffset);
                break;

            case MidiMessage.Type.Reset: 
                // Do not **force** stop channels (breaks seamless loops, for example th06)
                StopAllChannels(false);
                Reset();
                break;

            default: break;
        }
    }
    
    /// <summary>Assigns priorities to the voices. Gets the priority of a voice based on its channel and state. Higher priority means the voice is more important and should be kept longer.</summary>
    private void AssignVoicePriorities() 
    {
        if (Math.Abs(_lastPriorityAssignmentTime - CurrentTime) < .0001f) 
            return;

        SpessaLog.Info("[WARN] Polyphony exceeded, stealing voices");
        
        _lastPriorityAssignmentTime = CurrentTime;

        foreach (var voice in Voices) 
        {
            voice.Priority = 0;
            if (voice.Channel!.DrumChannel)
                // Important
                voice.Priority += 5;

            if (voice.IsInRelease)
                // Not important
                voice.Priority -= 5;

            // Less velocity = less important
            voice.Priority += voice.Velocity / 25; // Map to 0-5
            // The newer, more important
            voice.Priority -= (int)voice.VolEnv.State;
            if (voice.IsInRelease) voice.Priority -= 5;
            voice.Priority -= Util.Round(voice.VolEnv.AttenuationCb / 200);
        }
    }

    private void UpdatePresetList()
    {
        var mainFont = SoundBankManager.PresetList;
        ClearCache();
        CallEvent(new Event.CbPresetListChange(mainFont));
        GetDefaultPresets();
        // Update presets
        foreach (var c in MidiChannels)
        {
            var locked = c.SystemParameters.PresetLock;
            
            // Unlock and set
            c.Set((ChannelSystemParameter.Type.PresetLock, false));
            c.ProgramChange(c.Patch.Program);
            // Restore
            c.Set((ChannelSystemParameter.Type.PresetLock, locked));
        }

        Reset();
    }
    
    private void GetDefaultPresets() 
    {
        // Override this to XG, to set the default preset to NOT be XG drums!
        DefaultPreset = SoundBankManager.GetPreset(
            new MidiPatch(), Midi.System.XG);

        DrumPreset = SoundBankManager.GetPreset(
            new MidiPatch { IsGMGSDrum = true }, Midi.System.GS);
    }
}