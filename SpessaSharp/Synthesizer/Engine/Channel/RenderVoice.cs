using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Voice;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class RenderVoice
{
    private const float HALF_PI = MathF.PI / 2;
    private const int MIN_PAN = -500;
    private const int MAX_PAN = 500;
    private const int PAN_RESOLUTION = MAX_PAN - MIN_PAN;
    
    // Initialize pan lookup tables
    private static readonly float[] PanTableLeft = 
        new float[PAN_RESOLUTION + 1];
    private static readonly float[] PanTableRight = 
        new float[PAN_RESOLUTION + 1];

    static RenderVoice()
    {
        for (var pan = MIN_PAN; pan <= MAX_PAN; pan++) 
        {
            // Clamp to 0-1
            var realPan = (pan - MIN_PAN) / (float)PAN_RESOLUTION;
            var tableIndex = pan - MIN_PAN;
            PanTableLeft[tableIndex] = float.Cos(HALF_PI * realPan);
            PanTableRight[tableIndex] = float.Sin(HALF_PI * realPan);
        }
    }
    
    /// <summary>Renders a voice to the stereo output buffer</summary>
    /// <param name="chan"></param>
    /// <param name="voice">The voice to render</param>
    /// <param name="timeNow">Current time in seconds</param>
    /// <param name="outputL">The left output buffer</param>
    /// <param name="outputR">The right output buffer</param>
    /// <param name="startIndex"></param>
    /// <param name="sampleCount"></param>
    public static void Execute(
        MidiChannel chan,
        Voice.Voice voice,
        float timeNow,
        Span<float> outputL,
        Span<float> outputR,
        int startIndex,
        int sampleCount)
    {
        // Check if release
        if (!voice.IsInRelease && // If not in release, check if the release time is
            timeNow >= voice.ReleaseStartTime) 
        {
            // Release the voice here
            voice.IsInRelease = true;
            voice.VolEnv.StartRelease(voice);
            voice.ModEnv.StartRelease(voice);

            // Looping mode 3
            if (voice.LoopingMode == Synthesizer.SampleLoopingMode.m3)
                voice.WaveTable.IsLooping = false;
        }

        voice.HasRendered = true;
        
        // Important sanity check, as we may disable the voice now
        // Testcase: mono mode with chords
        if (!voice.IsActive) return;

        var core = chan.SynthCore;
        var sampleRate = core.SampleRate;
        var modulated = voice.ModulatedGenerators.AsSpan();
        
        // CALCULATION START
        // TUNING
        var targetKey = voice.TargetKey;
        // Calculate tuning
        var cents =
            voice.PitchOffset + // Voice pitch offset
            modulated[(int)Generator.Type.FineTune] + // Soundfont fine tune
            chan.OctaveTuning[targetKey] + // MTS octave tuning
            chan.CurrentTuning; // Channel tuning

        var semitones = (float)modulated[
            (int)Generator.Type.CoarseTune]; // Soundfont coarse tuning
        
        // MIDI Tuning Standard
        // Use `midiNote` here since it was used for selecting the preset if tuning was active
        var tuning = core.Tunings[chan.Preset!.Program * 128 + voice.MidiNote];
        if ((int)tuning != -1) 
        {
            // Tuning is encoded as float
            // For example: 60.56 means key 60 and 56 cents (or 0.56 semitones)
            // Override key
            targetKey = (int)tuning;
            // Add microtonal tuning
            semitones += tuning - targetKey;
        }
        
        // Portamento
        if (voice.PortamentoFromKey > -1) 
        {
            // 0 to 1
            var elapsed = float.Min(
                (timeNow - voice.StartTime) / voice.PortamentoDuration, 1);

            var diff = targetKey - voice.PortamentoFromKey;
            // Zero progress means the pitch being in fromKey, full progress means the normal pitch
            semitones -= diff * (1 - elapsed);
        }

        // Calculate tuning by key using soundfont's scale tuning
        cents += (targetKey - voice.RootKey) *
                 modulated[(int)Generator.Type.ScaleTuning];
        
        // Low pass excursion with LFO and mod envelope
        var lowpassExcursion = 0;
        var volumeExcursionCentibels = 0f;
        var voiceGain = voice.GainModifier *
            (1 + modulated[(int)Generator.Type.Amplitude] / 1_000f);

        // Vibrato LFO
        if (timeNow >= voice.VibLFOStartTime) 
        {
            var vibPitchDepth = modulated[
                (int)Generator.Type.VibLFOToPitch];
            var vibFilterDepth = modulated[
                (int)Generator.Type.VibLFOToFilterFc];
            var vibAmplitudeDepth = modulated[
                (int)Generator.Type.VibLFOAmplitudeDepth];
            if (vibPitchDepth != 0 ||
                vibFilterDepth != 0 ||
                vibAmplitudeDepth != 0) 
            {
                var vibFreqHz = Math.Max(
                    0,
                    UnitConverter.AbsCentsToHz(modulated[
                        (int)Generator.Type.FreqVibLFO]) +
                    modulated[(int)Generator.Type.VibLFORate] / 100f);

                var rateInc = (vibFreqHz * sampleCount) / sampleRate;
                var vibLfoValue = 1 - 4 * Math.Abs(voice.VibLFOPhase - 0.5f);
                if ((voice.VibLFOPhase += rateInc) >= 1) voice.VibLFOPhase -= 1;
                cents += vibLfoValue * vibPitchDepth;
                // Low pass frequency
                lowpassExcursion =
                    (int)(lowpassExcursion + vibLfoValue * vibFilterDepth);

                // Amplitude depth
                voiceGain *= 1 - ((vibLfoValue + 1) / 2) *
                    (vibAmplitudeDepth / 1_000f);
            }
        }
        
        // Mod LFO
        if (timeNow >= voice.ModLFOStartTime) 
        {
            var modPitchDepth = modulated[
                (int)Generator.Type.ModLFOToPitch];
            var modVolDepth = modulated[
                (int)Generator.Type.ModLFOToVolume];
            var modFilterDepth = modulated[
                (int)Generator.Type.ModLFOToFilterFc];
            var modAmplitudeDepth = modulated[
                (int)Generator.Type.ModLFOAmplitudeDepth];
            // Don't compute mod lfo unless necessary
            if (modPitchDepth != 0 ||
                modFilterDepth != 0 ||
                modVolDepth != 0 ||
                modAmplitudeDepth != 0)
            {
                var modFreqHz = Math.Max(
                    0,
                    UnitConverter.AbsCentsToHz(
                        modulated[(int)Generator.Type.FreqModLFO]) +
                    modulated[(int)Generator.Type.ModLFORate] / 100f);

                var rateInc = (modFreqHz * sampleCount) / sampleRate;
                var modLfoValue = 1 - 4 * Math.Abs(voice.ModLFOPhase - 0.5f);
                if ((voice.ModLFOPhase += rateInc) >= 1) voice.ModLFOPhase -= 1;
                cents += modLfoValue * modPitchDepth;

                // Vol env volume offset
                // Negate the lfo value because audigy starts with increase rather than decrease
                volumeExcursionCentibels += -modLfoValue * modVolDepth;

                // Low pass frequency
                lowpassExcursion = 
                    (int)(lowpassExcursion + modLfoValue * modFilterDepth);

                // Amplitude depth
                voiceGain *=
                    1 - ((modLfoValue + 1) / 2) * (modAmplitudeDepth / 1_000f);
            }
        }
        
        // Implement proper GS vibrato. Custom vibrato used to be here.

        // Mod env
        var modEnvPitchDepth = modulated[
            (int)Generator.Type.ModEnvToPitch];
        var modEnvFilterDepth = modulated[
            (int)Generator.Type.ModEnvToFilterFc];
       
        // Don't compute mod env unless necessary
        if (modEnvFilterDepth != 0 || modEnvPitchDepth != 0) 
        {
            var modEnv = voice.ModEnv.Process(voice, timeNow);
            // Apply values
            lowpassExcursion = 
                (int)(lowpassExcursion + modEnv * modEnvFilterDepth);
            cents += modEnv * modEnvPitchDepth;
        }
        
        // Default resonant modulator: it does not affect the filter gain (neither XG nor GS did that)
        volumeExcursionCentibels -= voice.ResonanceOffset;

        // Finally, calculate the playback rate
        var centsTotal = cents + semitones * 100;
        var centsRounded = (int)centsTotal;
        if (centsRounded != voice.TuningCents) 
        {
            voice.TuningCents = centsRounded;
            voice.TuningRatio = float.Pow(2, centsTotal / 1_200f);
        }

        // Gain target
        var gainTarget =
            UnitConverter.CbAttenuationToGain(modulated[
                (int)Generator.Type.InitialAttenuation]) *
            UnitConverter.CbAttenuationToGain((int)volumeExcursionCentibels);

        // Looping mode 2: start on release. process only volEnv
        if (voice is
            {
                LoopingMode: Synthesizer.SampleLoopingMode.m2, 
                IsInRelease: false,
            })
        {
            voice.IsActive = voice.VolEnv.Process(sampleCount, gainTarget);
            return;
        }
        
        // SYNTHESIS
        var buffer = core.VoiceBuffer.AsSpan();
        
        // Wave table oscillator
        voice.IsActive = voice.WaveTable.Process(
            sampleCount, voice.TuningRatio, buffer);
        
        ref var bufferRef = ref MemoryMarshal.GetReference(buffer);

        // Vol env (output gain calculation)
        // Get the previous value
        var gain = voice.VolEnv.OutputGain;
        // Compute the new value
        var envActive = voice.VolEnv.Process(sampleCount, gainTarget);
        // Calculate increase
        var gainInc = (voice.VolEnv.OutputGain - gain) / sampleCount;
        
        // Low pass filter (inlined for performance, confirmed with node.js)
        {
            ref var f = ref voice.Filter;
            var initialFc = modulated[(int)Generator.Type.InitialFilterFc];

            if (f.Initialized) 
            {
                /* Note:
                 * We only smooth out the initialFc part,
                 * the modulation envelope and LFO excursions are not smoothed.
                 */
                f.CurrentInitialFc =
                    (int)(f.CurrentInitialFc +
                          (initialFc - f.CurrentInitialFc) *
                          chan.SynthCore.SmoothingConstant);
            }
            else 
            {
                // Filter initialization, set the current fc to target
                f.Initialized = true;
                f.CurrentInitialFc = initialFc;
            }

            // The final cutoff for this calculation
            var targetCutoff = f.CurrentInitialFc + lowpassExcursion;
            var modulatedResonance = modulated[
                (int)Generator.Type.InitialFilterQ];

            /* Note:
             * the check for initialFC is because of the filter optimization
             * (if cents are the maximum then the filter is open)
             * filter cannot use this optimization if it's dynamic (see #53), and
             * the filter can only be dynamic if the initial filter is not open
             */
            if (f.CurrentInitialFc > 13_499 &&
                targetCutoff > 13_499 &&
                modulatedResonance == 0) 
            {
                f.CurrentInitialFc = 13_500;
                // Filter is open, apply gain
                for (var i = 0; i < sampleCount; i++) 
                {
                    buffer[i] *= gain;
                    gain += gainInc;
                }
            } 
            else 
            {
                // Check if the frequency has changed. if so, calculate new coefficients
                if (Math.Abs(f.LastTargetCutoff - targetCutoff) > 1 ||
                    f.ResonanceCb != modulatedResonance) 
                {
                    f.LastTargetCutoff = targetCutoff;
                    f.ResonanceCb = modulatedResonance;
                    f.CalculateCoefficients(targetCutoff);
                }

                // Filter the input
                // Initial filtering code was ported from meltysynth created by sinshu.
                var (a0, a1, a2, a3, a4) = (
                    f.a0, f.a1, f.a2, f.a3, f.a4);
                var (x1, x2, y1, y2) = (
                    f.x1, f.x2, f.y1, f.y2);

                for (var i = 0; i < sampleCount; i++)
                {
                    ref var b = ref Unsafe.Add(ref bufferRef, i);
                    var input = b;
                    var filtered =
                        a0 * input + a1 * x1 + a2 * x2 - a3 * y1 - y2 * a4;

                    // Set buffer
                    x2 = x1;
                    x1 = input;
                    y2 = y1;
                    y1 = filtered;

                    // Apply filter and THEN gain
                    // Per SF2 spec apply order, also see
                    // https://github.com/FluidSynth/fluidsynth/issues/1427
                    b = filtered * gain;
                    gain += gainInc;
                }

                f.x1 = x1;
                f.x2 = x2;
                f.y1 = y1;
                f.y2 = y2;
            }
        }
        
        // Note, we do not use &&= as it short-circuits!
        // And we don't do = either as wavetable might've marked it as inactive (end of sample)
        voice.IsActive = voice.IsActive && envActive;
        
        // Pan and mix down the data
        var pan = 0;
        if (voice.OverridePan != 0)
            pan = voice.OverridePan;
        else 
        {
            // Smooth out pan to prevent clicking
            voice.CurrentPan += 
                (int)((modulated[(int)Generator.Type.Pan] - voice.CurrentPan) *
                core.PanSmoothingFactor);
            pan = voice.CurrentPan;
        }

        var systemParameters = core.SystemParameters;
        var outputGain = chan.CurrentGain * voiceGain;

        var index =
            Math.Clamp(pan + chan.CurrentPan, -500, +500) + 500;
        // Get voice's gain levels for each channel
        var gainLeft = PanTableLeft[index] * outputGain;
        var gainRight = PanTableRight[index] * outputGain;

        if (chan.MidiParameters.EfxAssign) 
        {
            // Straight into the insertion EFX!
            var left = core.InsertionInputL.AsSpan();
            var right = core.InsertionInputR.AsSpan();
            
            TensorPrimitives.MultiplyAdd(
                buffer[..sampleCount], gainLeft, left, left);
            TensorPrimitives.MultiplyAdd(
                buffer[..sampleCount], gainRight, right, right);
            return;
        }
        
        // Mix down the audio data
        TensorPrimitives.MultiplyAdd(
            buffer[..sampleCount], 
            gainLeft, 
            outputL.Slice(startIndex, sampleCount),
            outputL.Slice(startIndex, sampleCount));
        TensorPrimitives.MultiplyAdd(
            buffer[..sampleCount], 
            gainRight, 
            outputR.Slice(startIndex, sampleCount), 
            outputR.Slice(startIndex, sampleCount));

        if (!systemParameters.EffectsEnabled) return;
        
        // Disable reverb and chorus if necessary
        var reverbSend =
            modulated[(int)Generator.Type.ReverbEffectsSend] * voice.ReverbSend;
        if (reverbSend > 0) 
        {
            var reverbGain =
                systemParameters.ReverbGain * 
                outputGain * (reverbSend / 1_000f);
            var reverbInput = core.ReverbInput.AsSpan()[..sampleCount];
            TensorPrimitives.MultiplyAdd(
                buffer[..sampleCount], reverbGain, reverbInput, reverbInput);
        }

        var chorusSend = modulated[
            (int)Generator.Type.ChorusEffectsSend] * voice.ChorusSend;

        if (chorusSend > 0) 
        {
            var chorusGain = systemParameters.ChorusGain * 
                             (chorusSend / 1_000f) * outputGain;
            var chorusInput = core.ChorusInput.AsSpan()[..sampleCount];
            TensorPrimitives.MultiplyAdd(
                buffer[..sampleCount], chorusGain, chorusInput, chorusInput);
        }

        var delaySend = chan.MidiControllers[
            (int)Midi.CC.VariationDepth] * voice.DelaySend;
        
        if (core.DelayActive && delaySend > 0) 
        {
            var delayGain = outputGain * systemParameters.DelayGain *
                            (((int)delaySend >> 7) / 127f);
            var delayInput = core.ChorusInput.AsSpan()[..sampleCount];
            TensorPrimitives.MultiplyAdd(
                buffer[..sampleCount], delayGain, delayInput, delayInput);
        }
    }
}