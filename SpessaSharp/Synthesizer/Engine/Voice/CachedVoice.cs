using SpessaSharp.SoundBank;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary> Represents a cached voice </summary>
internal readonly record struct CachedVoice
{
    /// <summary>Common CachedVoice properties by zone</summary>
    public sealed record Base(
        ArraySegment<float> SampleData,
        ArraySegment<short> Generators,
        Engine.Voice.Voice.Modulator[] Modulators,
        int ExclusiveClass,
        int RootKey,
        int? OverrideTargetKey,
        int? OverrideVelocity,
        int LoopStart,
        int LoopEnd,
        float PlaybackStep,
        Synthesizer.SampleLoopingMode LoopingMode)
    {
        public sealed class Cache(int? sampleRate)
        {
            private readonly Dictionary<
                (BasicZone, BasicZone),
                (Voice.Parameters, Base?)> _cache = [];

            public void Clear() => _cache.Clear();

            public void Add(
                (BasicZone, BasicZone) key, Voice.Parameters vParams)
            {
                _cache[key] = sampleRate is { } sRate
                    ? (vParams, Of(vParams, sRate))
                    : (vParams, null);
            }

            public Base? TryGetBase((BasicZone, BasicZone) key) =>
                _cache.GetValueOrDefault(key).Item2;
            
            public Voice.Parameters? TryGetParams((BasicZone, BasicZone) key) =>
                _cache.TryGetValue(key, out var value) ? value.Item1 : null;
        }

        public static Base Of(Voice.Parameters voiceParams, int sampleRate)
        {
            var sample = voiceParams.Sample;
            var generators = voiceParams.Generators;

            var modulators = new Engine.Voice.Voice.Modulator[voiceParams.Modulators.Count];
            for (var i = 0; i < voiceParams.Modulators.Count; i++) 
                modulators[i] = Voice.Modulator.From(voiceParams.Modulators[i]);

            // Root key override
            var rootKey = sample.OriginalKey;
            if (generators[(int)Generator.Type.OverridingRootKey] > -1)
                rootKey = generators[(int)Generator.Type.OverridingRootKey];
        
            // Key override
            int? targetKey = null;
            if (generators[(int)Generator.Type.KeyNum] > -1)
                targetKey = generators[(int)Generator.Type.KeyNum];

            // Velocity override
            // Note: use a separate velocity to not override the cached velocity
            // Testcase: LiveHQ Natural SoundFont GM - the Glockenspiel preset
            int? velocity = null;
            if (generators[(int)Generator.Type.Velocity] > -1)
                velocity = generators[(int)Generator.Type.Velocity];

            var exclusiveClass = generators[(int)Generator.Type.ExclusiveClass];
        
            // Create the sample for the wavetable oscillator
            // Offsets are calculated at note on time (to allow for modulation of them)
            var loopStart = sample.LoopStart;
            var loopEnd = sample.LoopEnd;
            var sampleData = sample.GetAudioData();
            var playbackStep =
                (sample.Rate / (float)sampleRate) *
                float.Pow(2, sample.PitchCorrection / 1_200f); // Cent tuning
            var loopingMode = (Synthesizer.SampleLoopingMode)generators[
                (int)Generator.Type.SampleModes];
            
            return new Base(
                sampleData,
                generators,
                modulators,
                exclusiveClass,
                rootKey,
                targetKey,
                velocity,
                loopStart,
                loopEnd,
                playbackStep,
                loopingMode);
        }
    }

    private readonly Base _base;
    
    /// <summary> Sample data of this voice. </summary>
    public ArraySegment<float> SampleData => _base.SampleData;

    /// <summary> The unmodulated (copied to) generators of the voice. </summary>
    public ArraySegment<short> Generators => _base.Generators;

    /// <summary> The voice's modulators. </summary>
    public Engine.Voice.Voice.Modulator[] Modulators => _base.Modulators;

    /// <summary> Exclusive class number for hi-hats etc. </summary>
    public int ExclusiveClass => _base.ExclusiveClass;

    /// <summary> Target key of the voice (can be overridden by generators) </summary>
    public readonly int TargetKey;

    /// <summary> Target velocity of the voice (can be overridden by generators) </summary>
    public readonly int Velocity;

    /// <summary> MIDI root key of the sample </summary>
    public int RootKey => _base.RootKey;

    /// <summary> Start position of the loop </summary>
    public int LoopStart => _base.LoopStart;
    
    /// <summary> End position of the loop </summary>
    public int LoopEnd => _base.LoopEnd;
    
    /// <summary> Playback step (rate) for sample pitch correction </summary>
    public float PlaybackStep => _base.PlaybackStep;

    public Synthesizer.SampleLoopingMode LoopingMode => _base.LoopingMode;

    public CachedVoice(Base @base, int key, int velocity) 
    {
        _base = @base;
        
        // Key override
        TargetKey = _base.OverrideTargetKey ?? key;
        // Velocity override
        Velocity = _base.OverrideVelocity ?? velocity;
    }
}