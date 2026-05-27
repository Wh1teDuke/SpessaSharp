using System.Runtime.CompilerServices;
using SpessaSharp.SoundBank;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>
/// Calculates the modulation envelope for the given voice
/// </summary>
public sealed class ModulationEnvelope
{
    private const float MODENV_PEAK = 1;
    // 1000 should be precise enough
    private static readonly float[] CONVEX_ATTACK = new float[1_000];

    static ModulationEnvelope()
    {
        for (var i = 0; i < CONVEX_ATTACK.Length; i++) 
        {
            // This makes the db linear (I think)
            CONVEX_ATTACK[i] = ModulatorCurve.GetValue(
                0,
                ModulatorCurve.Type.Convex,
                i / 1_000f);
        }
    }
    
    /// <summary>The attack duration, in seconds.</summary>
    private float _attackDuration = 0;
    /// <summary>The decay duration, in seconds.</summary>
    private float _decayDuration = 0;
    /// <summary>The hold duration, in seconds.</summary>
    private float _holdDuration = 0;
    /// <summary>Release duration, in seconds.</summary>
    private float _releaseDuration = 0;
    /// <summary>The sustain level 0-1.</summary>
    private float _sustainLevel = 0;
    /// <summary>Delay phase end time in seconds, absolute (audio context time).</summary>
    private float _delayEnd = 0;
    /// <summary>Attack phase end time in seconds, absolute (audio context time).</summary>
    private float _attackEnd;
    /// <summary>Hold phase end time in seconds, absolute (audio context time).</summary>
    private float _holdEnd = 0;
    /// <summary>The level of the envelope when the release phase starts.</summary>
    private float _releaseStartLevel = 0;
    /// <summary>The current modulation envelope value.</summary>
    private float _currentValue = 0;
    /// <summary>If the modulation envelope has ever entered the release phase.</summary>
    private bool _enteredRelease = false;
    /// <summary>Decay phase end time in seconds, absolute (audio context time).</summary>
    private float _decayEnd = 0;

    /// <summary>
    /// Calculates the current modulation envelope value for the given time and voice.
    /// </summary>
    /// <param name="voice">The voice we are working on.</param>
    /// <param name="currentTime">In seconds.</param>
    /// <returns>Mod env value, from 0 to 1.</returns>
    public float Process(Voice voice, float currentTime)
    {
        if (_enteredRelease) 
        {
            // If the voice is still in the delay phase,
            // Start level will be 0 that will result in divide by zero
            if (_releaseStartLevel == 0) return 0;

            return float.Max(
                0,
                (1 -
                (currentTime - voice.ReleaseStartTime) /
                 _releaseDuration) * _releaseStartLevel);
        }
        
        if (currentTime < _delayEnd)
            _currentValue = 0; // Delay
        else if (currentTime < _attackEnd) 
        {
            // Modulation envelope uses convex curve for attack
            _currentValue =
                CONVEX_ATTACK[Math.Clamp((int)((1 - 
                    (_attackEnd - currentTime) /
                    _attackDuration) * 1_000), 0, CONVEX_ATTACK.Length - 1)];
        }
        else if (currentTime < _holdEnd)
            // Hold: stay at 1
            _currentValue = MODENV_PEAK;
        else if (currentTime < _decayEnd) 
        {
            // Decay: linear ramp from 1 to sustain level
            _currentValue =
                (1f - (_decayEnd - currentTime) / _decayDuration) *
                (_sustainLevel - MODENV_PEAK) + MODENV_PEAK;
        } 
        else
            // Sustain: stay at sustain level
            _currentValue = _sustainLevel;

        return _currentValue;
    }
    
    /// <summary> Starts the release phase in the envelope. </summary>
    /// <param name="voice">The voice this envelope belongs to.</param>
    public void StartRelease(Voice voice) 
    {
        _releaseStartLevel = _currentValue;
        _enteredRelease = true;

        // Min is set to -7200 to prevent lowpass clicks
        var releaseTime = Tc2Sec(int.Max(voice
            .GetModulatedGenerator(Generator.Type.ReleaseModEnv), -7_200));

        // Release time is from the full level to 0%
        // To get the actual time, multiply by the release start level
        _releaseDuration = releaseTime * _releaseStartLevel;
    }
    
    /// <summary> Initializes the modulation envelope. </summary>
    /// <param name="voice">The voice this envelope belongs to.</param>
    public void Init(Voice voice) 
    {
        _enteredRelease = false;
        _sustainLevel = 1 - voice.ModulatedGenerators[
            (int)Generator.Type.SustainModEnv] / 1_000f;

        _attackDuration = Tc2Sec(voice.ModulatedGenerators[
            (int)Generator.Type.AttackModEnv]);

        var decayKeyExcursionCents =
            (60 - voice.MidiNote) *
            voice.ModulatedGenerators[(int)Generator.Type.KeyNumToModEnvDecay];
        
        var decayTime = Tc2Sec(
            voice.ModulatedGenerators[(int)Generator.Type.DecayModEnv] +
            decayKeyExcursionCents);

        // According to the specification, the decay time is the time it takes to reach 0% from 100%.
        // Calculate the time to reach actual sustain level,
        // For example, sustain 0.6 will be 0.4 of the decay time
        _decayDuration = decayTime * (1 - _sustainLevel);

        var holdKeyExcursionCents =
            (60 - voice.MidiNote) *
            voice.ModulatedGenerators[(int)Generator.Type.KeyNumToModEnvHold];

        _holdDuration = Tc2Sec(
            holdKeyExcursionCents +
            voice.ModulatedGenerators[(int)Generator.Type.HoldModEnv]);

        _delayEnd =
            voice.StartTime + Tc2Sec(voice.ModulatedGenerators[
                (int)Generator.Type.DelayModEnv]);

        _attackEnd = _delayEnd + _attackDuration;
        _holdEnd = _attackEnd + _holdDuration;
        _decayEnd = _holdEnd + _decayDuration;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Tc2Sec(int timecents) =>
        // At such low values, buffer size of 128 may cause clicks in the lowpass filter
        // -10114 is the lowest for it to fit at least twice in 128 samples@44.1kHz
        // Testcase: MS_Basic-v0.2.1.sf2 Bass & Lead
        timecents <= -10_114
            ? 0 
            : UnitConverter.TimecentsToSeconds(timecents);
}