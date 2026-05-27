using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Effects.Insertion;
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
    public const int DEFAULT_PERCUSSION = 9;

    /// <summary>Used globally to identify the embedded sound bank. This is used to prevent the embedded bank from being deleted.</summary>
    internal static readonly string EMBEDDED_SOUND_BANK_ID =
        $"SPESSASHARP_EMBEDDED_BANK_{Guid.NewGuid()}_DO_NOT_DELETE";
    
    /// <summary>
    /// This is needed because effects (regular ones) are send straight from the mono signal, whereas
    /// insertion effects receive the panned audio (twice), which reduces gain by a factor of cos(pi/4) * cos(pi/4) (master pan + voice pan).
    /// This reverses it.
    /// </summary>
    public static readonly float EFX_SENDS_GAIN_CORRECTION = 
        1f / float.Pow(float.Cos(MathF.PI / 4), 2);

    /// <summary>
    /// The maximum buffer size the synthesizer can render at once.
    /// Attempting to `.Process()` more samples than this will result in an error.
    /// Defaults to 128.</summary>
    /// <param name="MaxBufferSize"></param>
    /// <param name="EventsEnabled">Indicates if the event system is enabled. This can be changed later.</param>
    /// <param name="InitialTime">The initial time of the synth, in seconds.</param>
    /// <param name="EffectsEnabled">Indicates if the effects are enabled. This can be changed later.</param>
    /// <param name="ReverbProcessor">Reverb processor for the synthesizer. Leave undefined to use the default.</param>
    /// <param name="ChorusProcessor">Chorus processor for the synthesizer. Leave undefined to use the default.</param>
    /// <param name="DelayProcessor">Delay processor for the synthesizer. Leave undefined to use the default.</param>
    public readonly record struct Options(
        int MaxBufferSize,
        bool EventsEnabled,
        float InitialTime,
        bool EffectsEnabled,
        Effect.ReverbProcessor? ReverbProcessor = null,
        Effect.ChorusProcessor? ChorusProcessor = null,
        Effect.DelayProcessor? DelayProcessor = null)
    {
        public static readonly Options Default = new()
        {
            EventsEnabled   = true,
            EffectsEnabled  = true,
            InitialTime     = 0,
            MaxBufferSize   = SPESSA_BUFSIZE,
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
    public enum InterpolationType { Linear, NearestNeighbor, Hermite, }
    
    /// <summary> Gain smoothing for rapid volume changes. Must be run EVERY SAMPLE</summary>
    private const float GAIN_SMOOTHING_FACTOR = 0.01f;

    /// <summary> Pan smoothing for rapid pan changes</summary>
    private const float PAN_SMOOTHING_FACTOR = 0.05f;
    
    /// <summary> Voices of this synthesizer, as a fixed voice pool.</summary>
    public readonly List<Voice> Voices;
    
    /// <summary> All MIDI channels of the synthesizer.</summary>
    public readonly List<MidiChannel> MidiChannels = new(64);
    
    /// <summary> The maximum allowed buffer size to render.</summary>
    public readonly int MaxBufferSize;
    
    /// <summary> The buffer to use when rendering a voice.</summary>
    public readonly float[] VoiceBuffer;
    
    /// <summary> The insertion processor's left input buffer.</summary>
    public readonly float[] InsertionInputL;

    /// <summary> The insertion processor's right input buffer.</summary>
    public readonly float[] InsertionInputR;
    
    /// <summary> The reverb processor's input buffer.</summary>
    public readonly float[] ReverbInput;
    
    /// <summary> The chorus processor's input buffer.</summary>
    public readonly float[] ChorusInput;
    
    /// <summary> The reverb processor's input buffer.</summary>
    public readonly float[] DelayInput;
    
    /// <summary> Delay is not used outside SC-88+ MIDIs, this is an optimization.</summary>
    public bool DelayActive;
    
    /// <summary> The sound bank manager, which manages all sound banks and presets.</summary>
    public readonly SoundBankManager SoundBankManager;
    
    /// <summary> Handles the custom key overrides: velocity and preset </summary>
    public readonly KeyModifier.Manager KeyModifierManager = new();

    public readonly int SampleRate;

    /// <summary>
    /// This.tunings[program * 128 + key] = midiNote,cents (fraction).
    /// All MIDI Tuning Standard tunings, 128 keys for each of 128 programs.
    /// -1 means no change.
    /// </summary>
    public readonly float[] Tunings = new float[128 * 128];
    
    /// <summary>The global MIDI parameters of the synthesizer.</summary>
    public readonly GlobalMidiParameter[] MidiParameters = 
        GlobalMidiParameters.Default.ToArray(); // Copy, not set!
    
    /// <summary>The system parameters of the synthesizer.</summary>
    public readonly GlobalSystemParameter[] SystemParameters =
        GlobalSystemParameters.Default.ToArray();// Copy, not set!
    
    /// <summary>The current time of the synthesizer, in seconds.</summary>
    public double CurrentTime;
    
    /// <summary> Synth's default (reset) preset. </summary>
    public BasicPreset? DefaultPreset;
    
    /// <summary> Synth's default (reset) drum preset. </summary>
    public BasicPreset? DrumPreset;
    
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
        CachedVoice? Single, ArraySegment<CachedVoice>? Multi);
    
    /// <summary>
    /// Cached voices for all presets for this synthesizer.
    /// Nesting is calculated in getCachedVoiceIndex, returns a list of voices for this note.
    /// </summary>
    private readonly Dictionary<(MidiPatch Patch, int Key, int Vel),
        CachedVoiceList> _cachedVoices = new (200);
    
    private readonly CachedVoice.Base.Cache _cvbCache; 

    /// <summary>Sets a system parameter of the synthesizer. </summary>
    /// <param name="param">The type and value of the system parameter to set.</param>
    public void Set(GlobalSystemParameter param) =>
        GlobalSystemParameters.Set(this, param);

    public void SystemExclusive(
        ReadOnlySpan<byte> syx,
        int channelOffset = 0) =>
            Engine.SystemExclusive.Execute(this, syx, channelOffset);
    
    /// <summary> Current total amount of voices that are currently playing. </summary>
    public int VoiceCount { get; private set; }
    
    /// <summary> The synthesizer's reverb processor. </summary>
    public readonly Effect.ReverbProcessor ReverbProcessor;
    
    /// <summary> The synthesizer's chorus processor. </summary>
    public readonly Effect.ChorusProcessor ChorusProcessor;
    
    /// <summary> The synthesizer's delay processor. </summary>
    public readonly Effect.DelayProcessor DelayProcessor;
    
    /// <summary>
    /// A sysEx may set a "Part" (channel) to receive on a different channel number.
    /// This slows down the access, so this toggle tracks if it's enabled or not.
    /// </summary>
    public bool CustomChannelNumbers { get; internal set; }
    
    /// <summary>Sets a global MIDI parameter of the synthesizer.</summary>
    /// <param name="param">The type and value of the global MIDI parameter to set.</param>
    internal void Set(GlobalMidiParameter param) =>
        GlobalMidiParameters.Set(this, param);
    
    /// <summary>
    /// Resets all global MIDI parameters to their default values.
    /// </summary>
    /// <param name="system">the MIDI system to set when resetting.</param>
    internal void ResetMidiParameters(Midi.System system) =>
        GlobalMidiParameters.Reset(this, system);
    
    /// <summary> The fallback processor when the requested insertion is not available. </summary>
    internal readonly ThruFX InsertionFallback = new();
    
    /// <summary> The current insertion processor. </summary>
    internal Effect.InsertionProcessor InsertionProcessor;
    
    /// <summary>
    /// All the insertion effects available to the processor.<br/>
    /// The key is the EFX type stored as MSB lshift 8 | LSB
    /// </summary>
    internal readonly FrozenDictionary<int, Effect.InsertionProcessor> 
        InsertionEffects;
    
    /// <summary> Insertion is not used outside SC-88Pro+ MIDIs, this is an optimization. </summary>
    internal bool InsertionActive;
    
    /// <summary> For F5 system exclusive </summary>
    internal int PortSelectChannelOffset;

    /// <summary>For insertion snapshot tracking<br/>
    /// 20 parameters (0-19) + 3 sends<br/>
    /// Index to gs is Addr3 - 3 (for example EFX PARAMETER 1 is 0x03 and here it's 0)<br/>
    /// Note: 255 means "no change"</summary>
    internal readonly byte[] InsertionParams = new byte[23];

    /// <summary>For smoothing the filter cutoff frequency.</summary>
    internal readonly float SmoothingConstant;
    
    /// <summary>Last time the priorities were assigned. Used to prevent assigning priorities multiple times when more than one voice is triggered during a quantum. </summary>
    private double _lastPriorityAssignmentTime;
    
    /// <summary>Synth's event queue from the main thread</summary>
    private readonly List<
            (ArraySegment<byte> Message, int ChannelOffset, double Time)>
        _eventQueue = [];
    
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
        InsertionParams.AsSpan().Fill(255);
        
        InsertionProcessor = InsertionFallback;

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
        // These smoothing factors were tested on 44,100 Hz, adjust them to target sample rate here
        // Volume  smoothing factor
        GainSmoothingFactor =
            GAIN_SMOOTHING_FACTOR * (44_100f / sampleRate);
        // Pan smoothing factor
        PanSmoothingFactor = PAN_SMOOTHING_FACTOR * (44_100f / sampleRate);
        
        var bufSize = MaxBufferSize;
        // Initialize effects
        ReverbProcessor =
            options.ReverbProcessor ?? new SSReverb(sampleRate, bufSize);
        ChorusProcessor =
            options.ChorusProcessor ?? new SSChorus(sampleRate, bufSize);
        DelayProcessor = 
            options.DelayProcessor ?? new SSDelay(sampleRate, bufSize);
        
        // Initialize buffers
        VoiceBuffer = new float[bufSize];
        InsertionInputL = new float[bufSize];
        InsertionInputR = new float[bufSize];
        ReverbInput = new float[bufSize];
        ChorusInput = new float[bufSize];
        DelayInput = new float[bufSize];
        
        // Register insertion
        var insertions = new Dictionary<int, Effect.InsertionProcessor>();
        foreach (var proc in Effect.InsertionProcessor.List)
        {
            var p = proc(SampleRate, MaxBufferSize);
            insertions[p.Type] = p;
        }
        InsertionEffects = insertions.ToFrozenDictionary();

        ResetInsertionParams(); // Initial setup
        
        // Initialize voices
        var voiceCap = SystemParameters.VoiceCap;
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
        var voiceCap = SystemParameters.VoiceCap;
        for (var i = 0; i < voiceCap; i++) 
        {
            var v = Voices[i];
            if (!v.IsActive) 
            {
                // Prevent this voice from being stolen
                v.Priority = int.MaxValue;
                return v;
            }
        }
        
        // No match, assign priorities
        if (SystemParameters.AutoAllocateVoices)
        {
            SpessaLog.Info($"Allocating a new voice, total count {
                SystemParameters.VoiceCap + 1}.");
            
            // Allocate a new voice and return it
            AllocateNewVoices(1);
            var v = Voices[^1];

            Set((GlobalSystemParameter.Type.VoiceCap, ++voiceCap));
            // Prevent this voice from being stolen
            v.Priority = int.MaxValue;
            return v;
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
        Debug.WriteLine("Stop all received!");
        foreach (var channel in MidiChannels)
            channel.StopAllNotes(force);
    }
    
    /// <summary>Processes a raw MIDI message.</summary>
    /// <param name="message">The message to process.</param>
    /// <param name="channelOffset">The channel offset for the message.</param>
    /// <param name="time">The audio context time when the event should execute, in seconds.</param>
    public void ProcessMessage(
        ArraySegment<byte> message, int channelOffset = 0, double? time = null)
    {
        if (time > CurrentTime) 
        {
            _eventQueue.Add((message, channelOffset, time.Value));
            _eventQueue.Sort((e1, e2) =>
                e1.Time.CompareTo(e2.Time));
        } 
        else ProcessMessageInternal(message, channelOffset);
    }
    
    /// <summary>Processes a raw MIDI message.</summary>
    /// <param name="message">The message to process.</param>
    /// <param name="channelOffset">The channel offset for the message.</param>
    public void ProcessMessage(
        ReadOnlySpan<byte> message, int channelOffset = 0) =>
        ProcessMessageInternal(message, channelOffset);

    public void Destroy() 
    {
        Voices.Clear();

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

        // Override patch
        var overridePatch = KeyModifierManager.HasOverridePatch(
            channel, midiNote);

        var preset = channelObject.Preset;
        if (overridePatch) 
        {
            var patch = KeyModifierManager.GetPatch(channel, midiNote);
            preset = SoundBankManager.GetPreset(
                patch, MidiParameters.MidiSystem);
        }

        // Warning is handled in program change
        return preset == null
            ? new CachedVoiceList(null, ArraySegment<CachedVoice>.Empty) 
            : GetVoicesForPreset(preset, midiNote, velocity);
    }
    
    public void CreateMIDIChannel(bool sendEvent) 
    {
        var channel = new MidiChannel(
            this, DefaultPreset, MidiChannels.Count);

        MidiChannels.Add(channel);
        if (sendEvent) 
        {
            CallEvent(Event.OfChannelAdded());
            channel.SetDrums(true);
        }
    }
    
    /// <summary>Executes a full system reset of the synthesizer. This will reset all controllers to their default values, except for the locked controllers.</summary>
    /// <param name="system"></param>
    public void Reset(Midi.System system = DefaultMode) 
    {
        // Call here because there are returns in this function.
        CallEvent(new Event.CbReset(system));
        ResetMidiParameters(system);

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
        if (!SystemParameters.DelayLock)
            DelayActive = false;

        ResetInsertion();

        // Avoid crashing
        if (DrumPreset == null || DefaultPreset == null) return;
        
        // Reset channels
        // Do not send CC changes as we call reset
        foreach (var ch in MidiChannels)
            ch.Reset(false);
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
    
    public void Process(
        ArraySegment<float> left,
        ArraySegment<float> right,
        int startIndex = 0,
        int? sampleCount = null) =>
        ProcessSplit(
            [(left, right)],
            left,
            right,
            startIndex,
            sampleCount);
    
    /// <summary>
    /// The main rendering pipeline, renders all voices the processes the effects
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
    ///    ┌─────────┴──────────┐ ┌─┴──────────┴───────────────────┴────┐
    ///    │  Dry Output Pairs  │ │        Stereo Effects Output        │
    ///    └────────────────────┘ └─────────────────────────────────────┘
    /// ]]>
    /// </code>
    /// The pipeline is quite similar to the one on SC-8850 manual page 78.
    /// All output arrays must be the same length, the method will crash otherwise.
    /// </remarks>
    /// <param name="outputs">The stereo pairs for each MIDI channel's dry output, will be wrapped if less.</param>
    /// <param name="effectsLeft">The left stereo effect output buffer.</param>
    /// <param name="effectsRight">The right stereo effect output buffer.</param>
    /// <param name="startIndex">The index to start writing at into the output buffer.</param>
    /// <param name="samples">The amount of samples to write.</param>
    public void ProcessSplit(
        ReadOnlySpan<(
            ArraySegment<float> Left,
            ArraySegment<float> Right)> outputs,
        Span<float> effectsLeft,
        Span<float> effectsRight,
        int startIndex = 0,
        int? samples = null) 
    {
        // Process event queue
        if (_eventQueue.Count > 0) 
        {
            var time = CurrentTime;
            while (_eventQueue.Count > 0 && _eventQueue[0].Time <= time) 
            {
                var q = _eventQueue[0];
                _eventQueue.RemoveAt(0);
                ProcessMessageInternal(q.Message, q.ChannelOffset);
            }
        }

        // Validate
        startIndex = Math.Max(startIndex, 0);
        var sampleCount = samples ?? outputs[0].Left.Count - startIndex;

        if (sampleCount > MaxBufferSize)
            throw SpessaException.Invalid(
                $"Requested {sampleCount
                } samples, but maxBufferSize is {MaxBufferSize}");
        
        // Clear the buffers
        ReverbInput.AsSpan().Clear();
        ChorusInput.AsSpan().Clear();
        if (DelayActive) DelayInput.AsSpan().Clear();

        if (InsertionActive) 
        {
            InsertionInputL.AsSpan().Clear();
            InsertionInputR.AsSpan().Clear();
        }

        // Clear voice count
        foreach (var c in MidiChannels)
            c.ClearVoiceCount();
        
        VoiceCount = 0;
        
        // Process voices
        var cap = SystemParameters.VoiceCap;
        var outputCount = outputs.Length;
        var cTime = (float)CurrentTime;
        
        for (var i = 0; i < cap; i++) 
        {
            var v = Voices[i];
            var ch = MidiChannels[v.Channel];
            if (!v.IsActive) continue;

            // Send the voice to appropriate output
            var outputIndex = v.Channel % outputCount;
            ch.RenderVoice(
                v,
                cTime,
                outputs[outputIndex].Left,
                outputs[outputIndex].Right,
                startIndex,
                sampleCount);

            // Update voice count
            ch.VoiceCount++;
            VoiceCount++;
        }
        
        // Process effects
        if (SystemParameters.EffectsEnabled) 
        {
            // Insertion first
            if (InsertionActive) 
            {
                InsertionProcessor.Process(
                    InsertionInputL,
                    InsertionInputR,
                    effectsLeft,
                    effectsRight,
                    ReverbInput,
                    ChorusInput,
                    DelayInput,
                    startIndex,
                    sampleCount);
            }

            // Chorus first, it feeds to reverb and delay
            ChorusProcessor.Process(
                ChorusInput,
                effectsLeft,
                effectsRight,
                ReverbInput,
                DelayInput,
                startIndex,
                sampleCount);

            // CC#94 in XG is variation, not delay
            if (DelayActive && 
                MidiParameters.MidiSystem != Midi.System.XG)
            {
                // Process delay
                DelayProcessor.Process(
                    DelayInput,
                    effectsLeft,
                    effectsRight,
                    ReverbInput,
                    startIndex,
                    sampleCount);
            }

            // Finally process the reverb processor (it goes directly into the output buffer)
            ReverbProcessor.Process(
                ReverbInput,
                effectsLeft,
                effectsRight,
                startIndex,
                sampleCount);
        }

        // Advance the time appropriately
        CurrentTime += sampleCount * _sampleTime;
    }
    
    /// <summary>Gets voices for a preset.</summary>
    /// <param name="preset">The preset to get voices for.</param>
    /// <param name="midiNote">The MIDI note to use.</param>
    /// <param name="velocity">The velocity to use.</param>
    /// <returns>Output is an array of voices.</returns>
    internal CachedVoiceList GetVoicesForPreset(
        BasicPreset preset, int midiNote, int velocity)
    {
        // If cached, return it!
        if (_cachedVoices.TryGetValue(
                (preset.Patch.Data, midiNote, velocity),
                out var cached)) return cached;

        // Not cached...
        // Create the voices
        var voiceParamsList = preset.GetVoiceParameters(
            _cvbCache, midiNote, velocity);
        var voiceParamsCount = voiceParamsList.Count;
        var voiceList = new CachedVoiceList(
            Single: null, 
            Multi: ArraySegment<CachedVoice>.Empty);

        if (voiceParamsCount == 0)
        {
            _cachedVoices[(preset.Patch.Data, midiNote, velocity)] = voiceList;
            return voiceList;
        }

        var v = 0;
            
        foreach (var voiceParams in voiceParamsList)
        {
            var sample = voiceParams.Sample;

            if (voiceParams.Sample.GetAudioData().AsSpan().IsEmpty) 
            {
                Debug.WriteLine(
                    $"[WARN] Discarding invalid sample: {sample.Name}");
                continue;
            }

            var cachedVoice = new CachedVoice(
                _cvbCache.TryGetBase(voiceParams.Zone)!,
                midiNote,
                velocity);

            switch (v)
            {
                case 0:
                    voiceList = new CachedVoiceList(cachedVoice, null);
                    break;
                case 1:
                    var zero = voiceList.Single!.Value;
                    voiceList = new CachedVoiceList(
                        null, new CachedVoice[voiceParamsCount]);
                    voiceList.Multi!.Value.AsSpan()[0] = zero;
                    goto default;
                default:
                    voiceList.Multi!.Value.AsSpan()[v] = cachedVoice;
                    break;
            }

            v++;
        }

        // Cache the voice
        _cachedVoices[(preset.Patch.Data, midiNote, velocity)] = voiceList;
        return voiceList;
    }
    
    public void ClearCache()
    {
        _cachedVoices.Clear();
        _cvbCache.Clear();
    }

    public Effect.InsertionProcessorSnapshot GetInsertionSnapshot() =>
        new()
        {
            Type = InsertionProcessor.Type,
            Params = InsertionParams,
            Channels = MidiChannels.Select(
                c => c.MidiParamArray.EfxAssign).ToArray(),
        };

    /// <summary>Copied callback so MIDI channels can call it.</summary>
    /// <param name="ev"></param>
    public void CallEvent(Event ev) => EventCallbackHandler(ev);
    
    internal void ResetInsertionParams() 
    {
        // No change
        InsertionParams.AsSpan().Fill(255);
        InsertionParams[20] = 40; // Reverb
        InsertionParams[21] = 0; // Chorus
        InsertionParams[22] = 0; // Delay
    }

    internal void ResetInsertion() 
    {
        if (SystemParameters.InsertionEffectLock) 
            return;

        InsertionActive = false;
        InsertionProcessor = InsertionFallback;
        InsertionProcessor.Reset();
        ResetInsertionParams();
        InsertionProcessor.SendLevelToReverb =
            (40 / 127f) * EFX_SENDS_GAIN_CORRECTION;
        InsertionProcessor.SendLevelToChorus = 0;
        InsertionProcessor.SendLevelToDelay = 0;
        
        CallEvent(Event.CbEffectChange.OfInsertion(
            parameter: 0, 
            value: InsertionProcessor.Type));
    }

    internal void SetReverbMacro(int macro) =>
        Macro.SetReverb(this, macro);
    internal void SetChorusMacro(int macro) =>
        Macro.SetChorus(this, macro);
    internal void SetDelayMacro(int macro) =>
        Macro.SetDelay(this, macro);

    /// <summary> Allocates new voices. </summary>
    internal void AllocateNewVoices(int count)
    {
        for (var i = 0; i < count; i++)
            Voices.Add(new Voice(SampleRate, MaxBufferSize));
    }
    
    private void ProcessMessageInternal(
        ReadOnlySpan<byte> message,
        int channelOffset) 
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

        Debug.WriteLine("[WARN] Polyphony exceeded, stealing voices");
        
        _lastPriorityAssignmentTime = CurrentTime;
        var cap = SystemParameters.VoiceCap;

        for (var i = 0; i < cap; i++) 
        {
            var voice = Voices[i];
            voice.Priority = 0;
            if (MidiChannels[voice.Channel].DrumChannel)
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