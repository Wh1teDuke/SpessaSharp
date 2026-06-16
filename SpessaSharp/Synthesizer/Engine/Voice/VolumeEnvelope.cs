using System.Runtime.CompilerServices;
using SpessaSharp.SoundBank;

namespace SpessaSharp.Synthesizer.Engine.Voice;

/// <summary>Applies a volume envelope for a given voice. For performance reasons, cbAttenuationToGain is inlined here.</summary>
public struct VolumeEnvelope
{
    // Per SF2 definition
    private const int CB_SILENCE = 960;
    private const int PERCEIVED_CB_SILENCE = 900;

    /**
     * VOL ENV STATES:
     * 0 - delay
     * 1 - attack
     * 2 - hold/peak
     * 3 - decay
     * 4 - sustain
     * release indicates by isInRelease property
     */
    public enum VEState { Delay, Attack, Hold, Decay, Sustain }

    /// <summary> The target gain for the current rendering block. </summary>
    public float OutputGain;
    /// <summary> The current attenuation of the envelope in cB. </summary>
    public float AttenuationCb = CB_SILENCE;
    /// <summary> The current stage of the volume envelope. </summary>
    public VEState State = VEState.Delay;
    /// <summary> The envelope's current time in samples. </summary>
    private int _sampleTime;
    /// <summary> The dB attenuation of the envelope when it entered the release stage. </summary>
    private float _releaseStartCb = CB_SILENCE;
    /// <summary> The time in samples relative to the start of the envelope. </summary>
    private int _releaseStartTimeSamples;
    /// <summary> The attack duration in samples. </summary>
    private int _attackDuration;
    /// <summary> The decay duration in samples. </summary>
    private int _decayDuration;
    /// <summary> The release duration in samples. </summary>
    private int _releaseDuration;
    /// <summary> The voice's sustain amount in cB. </summary>
    private int _sustainCb;
    /// <summary> The time in samples to the end of delay stage, relative to the start of the envelope. </summary>
    private int _delayEnd;
    /// <summary> The time in samples to the end of attack stage, relative to the start of the envelope. </summary>
    private int _attackEnd;
    /// <summary> The time in samples to the end of hold stage, relative to the start of the envelope. </summary>
    private int _holdEnd;
    /// <summary> The time in samples to the end of decay stage, relative to the start of the envelope. </summary>
    private int _decayEnd;
    /// <summary> If the volume envelope has ever entered the release phase. </summary>
    private bool _enteredRelease;
    /// <summary> The sample rate in Hz. </summary>
    private readonly int _sampleRate;
    /// <summary>
    /// The sample count between updates of the volume envelope.
    /// Since the volume envelope calculation runs once per rendering quantum,
    /// this effectively the buffer size.
    /// </summary>
    private readonly int _updateInterval;
    
    private float _invAttackDuration;
    private float _invDecayDuration;
    private float _invReleaseDuration;
    
    /// <summary>
    /// If sustain stage is silent, then we can turn off the voice when it is silent. We can't do that with modulated as it can silence the volume and then raise it again, and the voice must keep playing.
    /// </summary>
    private bool _canEndOnSilentSustain;

    /// <summary>
    /// </summary>
    /// <param name="sampleRate">Hz</param>
    /// <param name="bufferSize">samples</param>
    public VolumeEnvelope(int sampleRate, int bufferSize)
    {
        _sampleRate = sampleRate;
        _updateInterval = bufferSize;
    }

    /// <summary> Starts the release phase in the envelope. </summary>
    /// <param name="voice">The voice this envelope belongs to.</param>
    /// <returns>If the voice is off.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public bool StartRelease(Voice voice)
    {
        // Set the release start time to now
        _releaseStartTimeSamples = _sampleTime;

        var timecents = 
            voice.OverrideReleaseVolEnv;
        if (timecents == 0) timecents =
            voice.GetModulatedGenerator(Generator.Type.ReleaseVolEnv);

        // Min is set to -7200 prevent clicks
        _releaseDuration = TimecentsToSamples(int.Max(-7_200, timecents));
        
        if (_enteredRelease) 
        {
            // The envelope is already in release, but we request an update
            // This can happen with exclusiveClass for example
            // Don't compute the releaseStartCb as it's tracked in attenuationCb
            _releaseStartCb = AttenuationCb;
        }
        else
        {
            // The envelope now enters the release phase from the current gain
            // Compute the current gain level in decibel attenuation
            
            var sustainCb = Math.Clamp(_sustainCb, 0, CB_SILENCE);
            var fraction = sustainCb / (float)CB_SILENCE;

            // Decay: sf spec page 35: the time is for change from attenuation to -100dB,
            // Therefore, we need to calculate the real time
            // (changing from attenuation to sustain instead of -100dB)
            var keyNumAddition =
                (60 - voice.TargetKey) * voice
                    .GetModulatedGenerator(Generator.Type.KeyNumToVolEnvDecay);

            _decayDuration = (int)(TimecentsToSamples(
                voice.GetModulatedGenerator(Generator.Type.DecayVolEnv) +
                keyNumAddition) * fraction);

            switch (State)
            {
                case VEState.Delay:
                    // Delay phase: no sound is produced
                    _releaseStartCb = CB_SILENCE;
                    break;

                case VEState.Attack:
                    // Attack phase: get linear gain of the attack phase when release started
                    // And turn it into db as we're ramping the db up linearly
                    // (to make volume go down exponentially)
                    // Attack is linear (in gain) so we need to do get db from that
                    var elapsed =
                        1 - (_attackEnd - _releaseStartTimeSamples) /
                            (float)_attackDuration;
                    // Calculate the gain that the attack would have, so
                    // Turn that into cB
                    _releaseStartCb = 200 * float.Log10(elapsed) * -1;
                    break;
                
                case VEState.Hold:
                    _releaseStartCb = 0;
                    break;
                
                case VEState.Decay:
                    _releaseStartCb =
                        (1 - (_decayEnd - _releaseStartTimeSamples) /
                         (float)_decayDuration) * sustainCb;
                    break;
                
                case VEState.Sustain:
                    _releaseStartCb = sustainCb;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            _releaseStartCb = Math.Clamp(_releaseStartCb, 0, CB_SILENCE);
            AttenuationCb = _releaseStartCb;
        }
        
        _enteredRelease = true;
        
        // Release: sf spec page 35: the time is for change from attenuation to -100dB,
        // Therefore, we need to calculate the real time
        // (changing from release start to -100dB instead of from peak to -100dB)
        var releaseFraction = (CB_SILENCE - _releaseStartCb) / CB_SILENCE;
        _releaseDuration = (int)(_releaseDuration * releaseFraction);
        _invReleaseDuration = _releaseDuration > 0 ? 1f / _releaseDuration : 0f;
        
        // Voice may be off instantly
        // Testcase: mono mode
        return _releaseStartCb >= PERCEIVED_CB_SILENCE;
    }

    /// <summary> Initialize the volume envelope </summary>
    /// <param name="voice">The voice this envelope belongs to</param>
    public void Init(Voice voice)
    {
        _enteredRelease = false;
        State = VEState.Delay;
        _sampleTime = 0;
        OutputGain = 0;
        _canEndOnSilentSustain =
            voice.GetModulatedGenerator(Generator.Type.SustainVolEnv) >=
            PERCEIVED_CB_SILENCE;
        
        // Calculate absolute times (they can change so we have to recalculate every time
        _sustainCb = int.Min(
            CB_SILENCE,
            voice.GetModulatedGenerator(Generator.Type.SustainVolEnv));

        // Calculate durations
        _attackDuration = TimecentsToSamples(
            voice.GetModulatedGenerator(Generator.Type.AttackVolEnv));
        
        // Decay: sf spec page 35: the time is for change from attenuation to -100dB,
        // Therefore, we need to calculate the real time
        // (changing from attenuation to sustain instead of -100dB)
        var keyNumAddition =
            (60 - voice.TargetKey) *
            voice.GetModulatedGenerator(Generator.Type.KeyNumToVolEnvDecay);

        var fraction = _sustainCb / (float)CB_SILENCE;
        _decayDuration = (int)(TimecentsToSamples(
            voice.GetModulatedGenerator(Generator.Type.DecayVolEnv) +
            keyNumAddition) * fraction);
        
        _invAttackDuration = _attackDuration > 0 ? 1f / _attackDuration : 0f;
        _invDecayDuration  = _decayDuration  > 0 ? 1f / _decayDuration  : 0f;
        
        // Calculate absolute end times for the values
        _delayEnd = TimecentsToSamples(
            voice.GetModulatedGenerator(Generator.Type.DelayVolEnv));

        _attackEnd = _attackDuration + _delayEnd;

        // Make sure to take keyNumToVolEnvHold into account!
        var holdExcursion =
            (60 - voice.TargetKey) *
            voice.GetModulatedGenerator(Generator.Type.KeyNumToVolEnvHold);

        _holdEnd = TimecentsToSamples(
                voice.GetModulatedGenerator(Generator.Type.HoldVolEnv) +
                holdExcursion) + _attackEnd;

        _decayEnd = _decayDuration + _holdEnd;

        // If the voice has no attack or delay time, set current db to peak
        if (_attackEnd == _updateInterval)
            // This.attenuationCb = this.attenuationTarget;
            State = VEState.Hold;
    }
    
    /// <summary>
    /// Calculates the gain value for the last sample in the block and writes it to `outputGain`. Essentially we use approach of 100dB is silence, 0dB is peak.
    /// </summary>
    /// <param name="sampleCount">The amount of samples to write</param>
    /// <param name="gainTarget">The gain to apply.</param>
    /// <returns>If the voice has finished.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public bool Process(int sampleCount, float gainTarget)
    {
        // Advance time by the entire block to calculate the last sample's gain
        var sampleTime = _sampleTime += sampleCount;

        if (_enteredRelease) 
        {
            // How much time has passed since release was started?
            var elapsedRelease = sampleTime - _releaseStartTimeSamples;
            var cbDifference = CB_SILENCE - _releaseStartCb;

            // Linearly ramp down decibels
            AttenuationCb = 
                elapsedRelease * _invReleaseDuration * cbDifference + _releaseStartCb;
            OutputGain =
                UnitConverter.CentibelLookupTable[
                    (int)(AttenuationCb - UnitConverter.MIN_CENTIBELS)
                ] * gainTarget;
            return AttenuationCb < PERCEIVED_CB_SILENCE;
        }
        
        switch (State) 
        {
            case VEState.Delay: 
                // Delay phase: no sound is produced
                if (sampleTime < _delayEnd) 
                {
                    // Silence
                    AttenuationCb = CB_SILENCE;
                    OutputGain = 0;
                    return true;
                }
                State++;
                goto case VEState.Attack;

            // Fallthrough
            case VEState.Attack: 
                // Attack phase: ramp from 0 to attenuation
                if (sampleTime < _attackEnd) 
                {
                    // Set current attenuation to peak as its invalid during this phase
                    AttenuationCb = 0;
                    // Special case: linear gain ramp instead of linear db ramp
                    var linearGain = 1f - (_attackEnd - sampleTime) * _invAttackDuration;
                    OutputGain = linearGain * gainTarget;
                    return true;
                }

                State++;
                goto case VEState.Hold;

            // Fallthrough
            case VEState.Hold: 
                // Hold/peak phase: stay at max volume
                if (sampleTime < _holdEnd) 
                {
                    // Peak, no attenuation
                    AttenuationCb = 0;
                    OutputGain = gainTarget;
                    return true;
                }
                State++;
                goto case VEState.Decay;

            // Fallthrough
            case VEState.Decay: 
                if (sampleTime < _decayEnd) 
                {
                    // Linear centibel ramp down to sustain
                    AttenuationCb = (1f - (_decayEnd - sampleTime) * _invDecayDuration) * _sustainCb;
                    OutputGain =
                        gainTarget *
                        UnitConverter.CentibelLookupTable[(int)(
                            AttenuationCb - UnitConverter.MIN_CENTIBELS)];
                    return true;
                }
                State++;
                goto case VEState.Sustain;

            // Fallthrough
            case VEState.Sustain: 
                if (_canEndOnSilentSustain &&
                    _sustainCb >= PERCEIVED_CB_SILENCE) 
                {
                    // Make sure to fill with silence
                    // https://github.com/spessasus/spessasynth_core/issues/57
                    AttenuationCb = CB_SILENCE;
                    OutputGain = 0;
                    return false;
                }

                // Sustain phase: stay at sustain
                AttenuationCb = _sustainCb;
                OutputGain =
                    gainTarget *
                    UnitConverter.CentibelLookupTable[
                        (_sustainCb - UnitConverter.MIN_CENTIBELS)];
                return true;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int TimecentsToSamples(int tc) =>
        Math.Max(0, (int)float.Floor(
            UnitConverter.TimecentsToSeconds(tc) * _sampleRate));
}