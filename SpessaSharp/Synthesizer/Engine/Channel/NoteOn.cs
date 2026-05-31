using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.MIDI;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Synthesizer.Engine.Voice;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class NoteOn
{
    [ThreadStatic] private static Random? _rng;

    private static Random Rng => _rng ??= new Random();

    /// <summary>Sends a "MIDI Note on" message and starts a note. </summary>
    /// <param name="chan"></param>
    /// <param name="midiNote">The MIDI note number (0-127).</param>
    /// <param name="velocity">velocity The velocity of the note (0-127). If less than 1, it will send a note off instead.</param>
    /// <param name="emit">If the note on should be updated and emitted (non-internal)</param>
    public static void Send(
        MidiChannel chan, int midiNote, int velocity, bool emit = true)
    {
        if (velocity < 1) 
        {
            chan.NoteOff(midiNote);
            return;
        }
        
        var synth = chan.SynthCore;
        var black = synth.SystemParameters.BlackMIDIMode;
        
        if (
            // If black MIDI conditions are met...
            (black && chan.SynthCore.VoiceCount > 200 && velocity < 40) ||
            (black && velocity < 10) ||
            // Or channel is muted ...
            chan.SystemParameters.IsMuted ||
            // Or channel has no preset ...
            chan.Preset == null)
            return;

        var transformed =
            velocity * (chan.MidiParameters.VelocitySenseDepth / 64f) +
            (chan.MidiParameters.VelocitySenseOffset - 64) * 2;
        
        // Apply Velocity Sense and clamp
        var realVelocity = (int)Math.Clamp(transformed, 0, 127);

        // Note which we should grab presets from (strictly internal)
        var soundBankNote = midiNote + chan.CurrentKeyShift;

        // Sanity check
        if (midiNote is > 127 or < 0) return;
        
        // MIDI Tuning Standard
        var program = chan.Preset.Program;
        var tune = synth.Tunings[program * 128 + midiNote];
        if (tune >= 0)
            // Overwrite the note with MIDI tuning standard!
            soundBankNote = (int)tune;
        
        // Monophonic retrigger
        if ((chan.SystemParameters.MonophonicRetrigger ??
             synth.SystemParameters.MonophonicRetrigger) ||
            chan.MidiParamArray.AssignMode == MidiChannel.Assign.Single)
            chan.KillNote(midiNote);

        // Key velocity override
        if (synth.KeyModifierManager.GetVelocity(
            chan.Channel, midiNote) is {} keyVel)
            realVelocity = keyVel;
        
        // Gain
        var voiceGain = synth.KeyModifierManager.GetGain(
            chan.Channel, midiNote);

        // Portamento
        var previousNote = chan.LastPortamentoNote;
        var portamentoEnabled =
            chan.PortamentoForce ||
            chan.MidiControllers[(int)Midi.CC.PortamentoOnOff] >= 8_192;

        // 14-bit MIDI CC -> 7-bit value
        var portamentoTime =
            chan.MidiControllers[(int)Midi.CC.PortamentoTime] >> 7;
        
        var canApplyPortamento =
            portamentoEnabled && // Enabled?
            !chan.DrumChannel && // Not a drum channel?
            previousNote >= 0 && // Valid note?
            previousNote != midiNote && // Not the same note?
            portamentoTime > 0; // Non-instant time?
        
        var portaFromKey = -1;
        var portaTime = 0f;

        if (canApplyPortamento) 
        {
            var keyDistance = Math.Abs(midiNote - previousNote);

            portaFromKey = previousNote;
            portaTime = PortamentoTime.ToSeconds(portamentoTime, keyDistance);

            chan.PortamentoForce = false;
        }
        
        // Always track the last note, even if portamento isn't applied.
        // See: https://github.com/spessasus/spessasynth_core/issues/77
        chan.LastPortamentoNote = midiNote;
        
        chan.PlayingNotes[midiNote] = true;
        
        // Mono mode
        if (!chan.MidiParamArray.PolyMode) 
        {
            if (chan.LastMonoNote >= 0 && chan.LastMonoNote != midiNote)
                chan.KillNote(chan.LastMonoNote);
            chan.LastMonoNote = midiNote;
            chan.LastMonoNote = velocity;
        }
        
        // Get voices
        var voiceList = synth.GetVoices(
            chan.Channel, soundBankNote, realVelocity);
        var voices = ReadOnlySpan<CachedVoice>.Empty;
        
        if (voiceList.Single is {} single)
            voices = MemoryMarshal.CreateSpan(ref single, 1);
        else if (voiceList.Multi is {} multi)
            voices = multi;

        // Overrides
        // Zero means disabled
        var panOverride = 0;
        var exclusiveOverride = 0;
        var pitchOffset = 0;
        var reverbSend = 1f;
        var chorusSend = 1f;
        var delaySend = 1f;

        if (chan.MidiParameters.RandomPan)
            // The range is -500 to 500
            panOverride = Util.Round(Rng.NextSingle() * 1_000 - 500);

        // Drum parameters
        if (chan.DrumChannel) 
        {
            var p = chan.DrumParams[soundBankNote];
            if (!p.RxNoteOn) return;

            var drumPan = p.Pan - 64;
            // If pan is different from default then it's overridden
            if (drumPan != 0)
            {
                if (drumPan == -64)
                {
                    // Random pan
                    panOverride =
                        Util.Round(Rng.NextSingle() * 1_000 - 500);
                }
                else
                {
                    var channelPan = (chan.MidiControllers[
                        (int)Midi.CC.Pan] >> 7) - 64;
                    var targetPan = Math.Clamp(
                        drumPan + channelPan, -63, 63);

                    // Ensure that override is applied, even for zero
                    panOverride = Util.Round((targetPan / 63f) * 500);
                    if (panOverride == 0) panOverride = 1;
                }
            }

            pitchOffset = p.Pitch;
            exclusiveOverride = p.ExclusiveClass;
            reverbSend = p.ReverbGain;
            chorusSend = p.ChorusGain;
            delaySend = p.DelayGain;
            // 1 is no override
            if (voiceGain >= 1) voiceGain = p.Gain;
        }
        
        var noteID = emit 
            ? chan.NoteOnID[midiNote]++
            : chan.NoteOnID[midiNote];

        // Add voices
        foreach (var cached in voices) 
        {
            var voice = synth.AssignVoice();
            var now = (float)synth.CurrentTime;
            voice.Setup(now, chan.Channel, midiNote, noteID);

            // Select the correct oscillator
            voice.WaveTable.Type =
                chan.SystemParameters.InterpolationType ??
                synth.SystemParameters.InterpolationType;

            // Set cached data
            voice.TargetKey = cached.TargetKey;
            voice.Velocity = cached.Velocity;
            cached.Generators.CopyTo(voice.Generators);
            voice.ExclusiveClass = exclusiveOverride;
            if (voice.ExclusiveClass == 0)
                voice.ExclusiveClass = cached.ExclusiveClass;
            
            voice.RootKey = cached.RootKey;
            voice.LoopingMode = cached.LoopingMode;
            voice.WaveTable.SampleData = cached.SampleData;
            voice.WaveTable.PlaybackStep = cached.PlaybackStep;

            // Set modulators
            if (chan.DynamicModulators.Active) 
            {
                // We have to copy them...
                voice.Modulators.Clear();
                voice.Modulators.AddRange(cached.Modulators);

                // Dynamic modulators
                foreach (var m in chan.DynamicModulators.ModulatorList)
                {
                    // Replace or add
                    var found = false;
                    foreach (ref var voiceMod in 
                        CollectionsMarshal.AsSpan(voice.Modulators))
                    {
                        if (!Modulator.IsIdentical(
                                m.Mod.Base, voiceMod.Base))
                            continue;

                        voiceMod = m.Mod;
                        found = true;
                        break;
                    }

                    if (!found)
                        voice.Modulators.Add(m.Mod);
                }
            } 
            else 
            {
                // Set directly
                voice.Modulators.Clear();
                voice.Modulators.AddRange(cached.Modulators);
            }

            if (voice.Modulators.Count > voice.ModulatorValues.Length) 
            {
                Debug.WriteLine(
                    $"[WARN] {voice.Modulators.Count
                    } modulators! Increasing modulatorValues table.");
                Array.Resize(ref voice.ModulatorValues, voice.Modulators.Count);
                voice.ModulatorValues.AsSpan().Clear();
            }

            // Apply generator override
            if (chan.Generators.OverridesEnabled)
            {
                var g = chan.Generators.Overrides;
                for (var type = 0; type < Generator.Amount; type++)
                {
                    var overrideValue = g[type];
                    if (overrideValue ==
                            Synthesizer.GENERATOR_OVERRIDE_NO_CHANGE_VALUE)
                        continue;
                    voice.Generators[type] = overrideValue;
                }
            }

            // Apply exclusive class
            // In mono mode all voices have been killed already
            if (voice.ExclusiveClass != 0 && chan.MidiParamArray.PolyMode) 
            {
                // Kill all voices with the same exclusive class
                var vc = 0;
                if (chan.VoiceCount > 0)
                {
                    var cTime = (float)synth.CurrentTime;
                    foreach (var v in synth.Voices) 
                    {
                        if (v.IsActive &&
                            v.Channel == chan.Channel &&
                            v.ExclusiveClass == voice.ExclusiveClass &&
                            // Only voices created in a different quantum
                            v.HasRendered) 
                        {
                            v.ExclusiveRelease(cTime);
                            if (++vc >= chan.VoiceCount) break; // We already checked all the voices
                        }
                    }
                }
            }
            
            // Compute all modulators
            chan.ComputeModulators(voice);

            // Initialize the volume envelope (non-realtime)
            voice.VolEnv.Init(voice);

            // Initialize the modulation envelope (non-realtime)
            voice.ModEnv.Init(voice);

            voice.Filter.Init();

            // Calculate LFO start times
            voice.VibLFOStartTime =
                now +
                UnitConverter.TimecentsToSeconds(voice.ModulatedGenerators[
                    (int)Generator.Type.DelayVibLFO]);

            voice.ModLFOStartTime =
                now +
                UnitConverter.TimecentsToSeconds(voice.ModulatedGenerators[
                    (int)Generator.Type.DelayModLFO]);

            // Modulate sample offsets (these are not real time)
            var cursorStartOffset =
                voice.ModulatedGenerators[(int)Generator.Type.StartAddrsOffset] +
                voice.ModulatedGenerators[(int)Generator.Type.StartAddrsCoarseOffset
                ] * 32_768;

            // This will be negative
            var endOffset =
                voice.ModulatedGenerators[(int)Generator.Type.EndAddrsOffset] +
                voice.ModulatedGenerators[(int)Generator.Type.EndAddrsCoarseOffset
                ] * 32_768;

            var loopStartOffset =
                voice.ModulatedGenerators[(int)Generator.Type.StartLoopAddrsOffset] +
                voice.ModulatedGenerators[(int)Generator.Type.StartLoopAddrsCoarseOffset
                ] * 32_768;

            var loopEndOffset =
                voice.ModulatedGenerators[(int)Generator.Type.EndLoopAddrsOffset] +
                voice.ModulatedGenerators[(int)Generator.Type.EndLoopAddrsCoarseOffset
                ] * 32_768;

            // Clamp the sample offsets
            // End is exclusive, not inclusive.
            // Testcase: https://github.com/spessasus/spessasynth_core/issues/90
            var endExclusive = cached.SampleData.Count;
            voice.WaveTable.Cursor = Math.Clamp(
                cursorStartOffset, 0, endExclusive - 1);
            voice.WaveTable.End = Math.Clamp(
                endExclusive + endOffset, 0, endExclusive);
            voice.WaveTable.LoopStart = Math.Clamp(
                cached.LoopStart + loopStartOffset, 0, endExclusive);
            voice.WaveTable.LoopEnd = Math.Clamp(
                cached.LoopEnd + loopEndOffset, 0, endExclusive);

            // Swap loops if needed
            if (voice.WaveTable.LoopEnd < voice.WaveTable.LoopStart) 
            {
                (voice.WaveTable.LoopStart, voice.WaveTable.LoopEnd) = 
                    (voice.WaveTable.LoopEnd, voice.WaveTable.LoopStart);
            }

            if (voice.WaveTable.LoopEnd - voice.WaveTable.LoopStart < 1 && // Disable loop if enabled
                // Don't disable on release mode. Testcase:
                // https://github.com/spessasus/SpessaSynth/issues/174
                voice.LoopingMode is 
                    Synthesizer.SampleLoopingMode.m1 or 
                    Synthesizer.SampleLoopingMode.m3) 
            {
                voice.LoopingMode = Synthesizer.SampleLoopingMode.m0;
            }
            
            voice.WaveTable.LoopLength =
                voice.WaveTable.LoopEnd - voice.WaveTable.LoopStart;
            voice.WaveTable.IsLooping =
                voice.LoopingMode is 
                    Synthesizer.SampleLoopingMode.m1 or
                    Synthesizer.SampleLoopingMode.m3;

            // Apply portamento
            voice.PortamentoFromKey = portaFromKey;
            voice.PortamentoDuration = portaTime;

            // Apply special params
            voice.OverridePan = panOverride;
            voice.GainModifier = voiceGain;
            voice.PitchOffset = pitchOffset;
            voice.ReverbSend = reverbSend;
            voice.ChorusSend = chorusSend;
            voice.DelaySend = delaySend;

            // Set initial pan to avoid split second changing from middle to the correct value
            var pOverride = panOverride;
            if (pOverride == 0) pOverride = voice.
                ModulatedGenerators[(int)Generator.Type.Pan];
            
            voice.CurrentPan = Math.Clamp(pOverride, -500, 500); // -500 to 500
        }
        
        chan.VoiceCount += voices.Length;
        
        if (emit) synth.CallEvent(
            new Event.CbNoteOn(midiNote, chan.Channel, velocity));
    }
}