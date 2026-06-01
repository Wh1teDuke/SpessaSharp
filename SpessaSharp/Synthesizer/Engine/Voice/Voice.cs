using System.Runtime.CompilerServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>
/// Voice represents a single instance of the
/// SoundFont2 synthesis model.
/// That is:<br/>
/// A wavetable oscillator (sample)<br/>
/// A volume envelope (volEnv)<br/>
/// A modulation envelope (modEnv)<br/>
/// Generators (generators and modulatedGenerators)<br/>
/// Modulators (modulators)<br/>
/// And MIDI params such as channel, MIDI note, velocity<br/>
/// </summary>
public sealed class Voice
{
    private const int EXCLUSIVE_CUTOFF_TIME = -2_320;
    
    /// <summary>
    /// </summary>
    /// <param name="Base">
    /// Indicates if the given modulator is chorus or reverb effects modulator.
    /// This is done to simulate BASSMIDI effects behavior:<br/>
    /// - defaults to 1000 transform amount rather than 200<br/>
    /// - values can be changed, but anything above 200 is 1000
    /// (except for values above 1000, they are copied directly)<br/>
    /// - all values below are multiplied by 5 (200 * 5 = 1000)<br/>
    /// - still can be disabled if the soundfont has its own modulator curve<br/>
    /// - this fixes the very low amount of reverb by default and doesn't break soundfonts<br/>
    /// </param>
    /// <param name="IsEffectModulator"></param>
    /// <param name="IsDefaultResonantModulator">
    /// The default resonant modulator does not affect the filter gain.
    /// Neither XG nor GS responded to cc #74 in that way.
    /// </param>
    /// <param name="IsModWheelModulator">If this is a modulation wheel modulator (for modulation depth range).</param>
    public readonly record struct Modulator(
        SoundBank.Modulator Base,
        bool IsEffectModulator,
        bool IsDefaultResonantModulator,
        bool IsModWheelModulator)
    {
        public readonly record struct ID(
            int Source,
            Generator.Type Destination,
            bool IsBipolar,
            bool IsNegative)
        {
            public override string ToString() =>
                $"{Source}-{Destination}-{IsBipolar}-{IsNegative}";
        }
        
        public static Modulator From(SoundBank.Modulator mod)
        {
            var s1Enum = mod.PrimarySource.ToSourceEnum();
            var s2Enum = mod.SecondarySource.ToSourceEnum();
            
            var isEffectModulator =
                s1Enum is 0x00_db or 0x00_dd &&
                s2Enum == 0x0 && mod.Destination is
                    Generator.Type.ReverbEffectsSend or
                    Generator.Type.ChorusEffectsSend;
            
            var isDefaultResonantModulator =
                s1Enum == SoundBank.Modulator.DefaultResonantModSource &&
                s2Enum == 0x0 &&
                mod.Destination == Generator.Type.InitialFilterQ;
            
            var isModWheelModulator =
                (mod.PrimarySource.Is(Midi.CC.ModulationWheel) ||
                mod.SecondarySource.Is(Midi.CC.ModulationWheel)) &&
                    mod.Destination is 
                        Generator.Type.ModLFOToPitch or
                        Generator.Type.VibLFOToPitch;

            return new Modulator(
                mod, 
                isEffectModulator, 
                isDefaultResonantModulator,
                isModWheelModulator);
        }
    }

    internal readonly record struct Parameters(
        ArraySegment<short> Generators,
        ArraySegment<SoundBank.Modulator> Modulators,
        BasicSample Sample);

    /// <summary>The oscillator currently used by this voice.</summary>
    public WaveTableOscillator WaveTable;
    
    /// <summary>Lowpass filter applied to the voice.</summary>
    public LowpassFilter Filter;
    
    /// <summary> The unmodulated (copied to) generators of the voice. </summary>
    public readonly short[] Generators = new short[Generator.Amount];
    
    /// <summary>The generators in real-time, affected by modulators. This is used during rendering.</summary>
    public readonly short[] ModulatedGenerators = new short[Generator.Amount];
    
    /// <summary>The voice's modulators.</summary>
    public readonly List<Modulator> Modulators = new(32);

    /// <summary>The current values for the respective modulators. If there are more modulators, the array must be resized.</summary>
    public short[] ModulatorValues = new short[64];
    
    /// <summary>Modulation envelope.</summary>
    public readonly ModulationEnvelope ModEnv = new();
    
    /// <summary>Volume envelope.</summary>
    public readonly VolumeEnvelope VolEnv;
    
    /// <summary>Resonance offset, it is affected by the default resonant modulator</summary>
    public float ResonanceOffset;
    
    /// <summary>Priority of the voice. Used for stealing.</summary>
    public int Priority;
    
    /// <summary>If the voice is currently active. If not, it can be used.</summary>
    public bool IsActive;
    
    /// <summary> Indicates if the voice has rendered at least one buffer. Used for exclusive class to prevent killing voices set on the same note. </summary>
    public bool HasRendered;
    
    /// <summary> Indicates if the voice is in the release phase. </summary>
    public bool IsInRelease;
    
    /// <summary> Indicates if the voice is currently held by the sustain pedal. </summary>
    public bool IsHeld;
    
    /// <summary> MIDI channel number of the voice. </summary>
    public int Channel;

    /// <summary>
    /// Grouping voices for specific Note On messages. Used for overlapping Note Ons.
    /// </summary>
    public int NoteID;
    
    /// <summary>
    /// MIDI note number of the voice.
    /// Direct number from the Note On message and is
    /// used for Note Off and external parameters:
    /// MTS and Per note Pitch Wheel.
    /// </summary>
    public int MidiNote;
        
    /// <summary>
    /// Target key of the voice.
    /// This is the effective MIDI note number,
    /// used to calculate scale tuning and envelope times,
    /// and can be overridden by generators.
    /// It is also used
    /// </summary>
    public int TargetKey;
    
    /// <summary>
    /// MIDI Velocity of the voice.
    /// This can be overridden by generators and is the effective velocity.
    /// MIDI Note On velocity is only used for zone filtering.
    /// </summary>
    public int Velocity;
    
    /// <summary>The root key of the voice.</summary>
    public int RootKey;
    
    /// <summary> The pressure of the voice </summary>
    public int Pressure;
    
    /// <summary> Linear gain of the voice. Used with Key Modifiers. </summary>
    public float GainModifier = 1;

    public Synthesizer.SampleLoopingMode LoopingMode = 
        Synthesizer.SampleLoopingMode.m0;
    
    /// <summary> Start time of the voice, absolute. </summary>
    public float StartTime;
    
    /// <summary> Start time of the release phase, absolute. </summary>
    public float ReleaseStartTime = float.PositiveInfinity;
    
    /// <summary> Current tuning in cents.</summary>
    public int TuningCents;
    
    /// <summary> Current calculated tuning. (as in ratio)</summary>
    public float TuningRatio = 1;
    
    /// <summary> From -500 to 500. Used for smoothing.</summary>
    public int CurrentPan;
    
    /// <summary> Initial key to glide from, MIDI Note number. If -1, the portamento is OFF.</summary>
    public int PortamentoFromKey = -1;
    
    /// <summary> uration of the linear glide, in seconds.</summary>
    public float PortamentoDuration;
    
    /// <summary> From -500 to 500, where zero means disabled (use the channel pan). Used for random pan.</summary>
    public int OverridePan;
    
    /// <summary> In cents.</summary>
    public int PitchOffset;
    
    /// <summary> Reverb send of the voice, used for drum parts, otherwise 1.</summary>
    public float ReverbSend = 1;
    
    /// <summary> Chorus send of the voice, used for drum parts, otherwise 1.</summary>
    public float ChorusSend = 1;
    
    /// <summary> Delay send of the voice, used for drum parts, otherwise 1.</summary>
    public float DelaySend = 1;
    
    /// <summary> Exclusive class number for hi-hats etc.</summary>
    public int ExclusiveClass;

    /// <summary> In timecents, where zero means disabled (use the modulatedGenerators table). Used for exclusive notes and killing notes. </summary>
    public int OverrideReleaseVolEnv;
    
    // Vibrato LFO data
    public float VibLFOPhase;
    public float VibLFOStartTime;
    // Mod LFO data
    public float ModLFOPhase;
    public float ModLFOStartTime;

    public Voice(int sampleRate, int bufferSize)
    {
        WaveTable = new WaveTableOscillator
        {
            Type = GlobalSystemParameters.Default.InterpolationType,
        };
        
        VolEnv = new VolumeEnvelope(sampleRate, bufferSize);
        Filter = new LowpassFilter(sampleRate);
    }

    /// <summary> Releases the voice as exclusiveClass.</summary>
    public void ExclusiveRelease(
        float currentTime,
        float minExclusiveLength = Synthesizer.MIN_EXCLUSIVE_LENGTH) 
    {
        OverrideReleaseVolEnv = EXCLUSIVE_CUTOFF_TIME; // Make the release nearly instant
        IsInRelease = false;
        ReleaseVoice(currentTime, minExclusiveLength);
    }
    
    /// <summary> Stops the voice </summary>
    /// <param name="currentTime"></param>
    /// <param name="minNoteLength">minimum note length in seconds</param>
    public void ReleaseVoice(
        float currentTime, 
        float minNoteLength = Synthesizer.MIN_NOTE_LENGTH) 
    {
        ReleaseStartTime = currentTime;
        // Check if the note is shorter than the min note time, if so, extend it
        if (ReleaseStartTime - StartTime < minNoteLength)
            ReleaseStartTime = StartTime + minNoteLength;
    }
    
    public void Setup(float currentTime, int channel, int midiNote, int noteID) 
    {
        // Remember to add new values here!!!
        // Clear state
        IsActive = true;
        IsInRelease = false;
        HasRendered = false;
        IsHeld = false;
        ReleaseStartTime = float.PositiveInfinity;
        Pressure = 0;
        OverrideReleaseVolEnv = 0;
        PortamentoDuration = 0;
        PortamentoFromKey = -1;
        // Important, these start at 1/4 way there!
        VibLFOPhase = 0.25f;
        ModLFOPhase = 0.25f;
        
        // Set parameters
        StartTime = currentTime;
        Channel = channel;
        MidiNote = midiNote;
        NoteID = noteID;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short GetModulatedGenerator(Generator.Type type) =>
        ModulatedGenerators[(int)type];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetModulatedGenerator(Generator.Type type, short value) =>
        ModulatedGenerators[(int)type] = value;
}