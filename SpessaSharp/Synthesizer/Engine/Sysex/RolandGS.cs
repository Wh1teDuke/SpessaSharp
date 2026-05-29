using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Sysex;

internal static class RolandGS
{
    /// <summary>
    /// Handles a GS system exclusive<br/>
    /// http://www.bandtrax.com.au/sysex.htm<br/>
    /// https://cdn.roland.com/assets/media/pdf/SC-8850_OM.pdf
    /// </summary>
    /// <param name="synth"></param>
    /// <param name="syx"></param>
    /// <param name="channelOffset"></param>
    public static void SystemExclusive(
        Synthesizer synth, ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        // 0x12: DT1 (Device Transmit)
        if (syx.Length >= 4 && syx[3] == 0x12) 
        {
            // Model ID
            switch (syx[2]) 
            {
                // GS
                case 0x42: 
                {
                    {
                        // This is a GS sysex
                        var a1 = syx[4];
                        var a2 = syx[5];
                        var a3 = syx[6];

                        // Sanity check
                        var data = byte.Min(syx[7], 127);
                        // SYSTEM MODE SET
                        if (a1 == 0 && a2 == 0 && a3 == 0x7f && data == 0x00) 
                        {
                            // This is a GS reset
                            Debug.WriteLine("GS Reset received!");
                            synth.Reset(Midi.System.GS);
                            return;
                        }

                        // Patch Parameter
                        if (a1 == 0x40) 
                        {
                            // System Parameter
                            if (a2 == 0x00) 
                            {
                                switch (a3) 
                                {
                                    case 0x00: 
                                    {
                                        // Roland GS master tune
                                        var tune =
                                            (data << 12) |
                                            (syx[8] << 8) |
                                            (syx[9] << 4) |
                                            syx[10];
                                        var cents = (tune - 1_024) / 10f;
                                        synth.Set((
                                            GlobalMidiParameter.Type.FineTune, 
                                            cents));
                                        CoolInfo("Master Tune", cents);
                                        break;
                                    }

                                    case 0x04: 
                                        // Roland GS master volume
                                        CoolInfo("Master Volume", data);
                                        break;

                                    case 0x05: 
                                        // Roland master key shift
                                        var transpose = data - 64;
                                        CoolInfo("Master Key-Shift", transpose);
                                        synth.Set((
                                            GlobalMidiParameter.Type.KeyShift,
                                            transpose));
                                        break;

                                    case 0x06: 
                                        // Roland master pan
                                        CoolInfo("Master Pan", data);
                                        synth.Set((
                                            GlobalMidiParameter.Type.Pan, 
                                            (data - 64) / 64f));
                                        break;

                                    case 0x7f: 
                                        // Roland mode set
                                        // GS mode set
                                        if (data == 0x00) 
                                        {
                                            // This is a GS reset
                                            Debug.WriteLine("GS Reset received!");
                                            synth.Reset(Midi.System.GS);
                                        } 
                                        else if (data == 0x7f) 
                                        {
                                            // GS mode off
                                            Debug.WriteLine("GS system off, switching to GM");
                                            synth.Reset(Midi.System.GM);
                                        }
                                        break;

                                    default: 
                                        Engine.SystemExclusive.NotRecognized(syx, "Roland GS");
                                        break;
                                }
                                return;
                            }

                            // Part Parameter, Patch Common (Effects)
                            if (a2 == 0x01) 
                            {
                                var isReverb = a3 is >= 0x30 and <= 0x37;
                                var isChorus = a3 is >= 0x38 and <= 0x40;
                                var isDelay = a3 is >= 0x50 and <= 0x5a;
                                // Disable effect editing if locked
                                if (isReverb && synth.SystemParameters.ReverbLock)
                                    return;
                                if (isChorus && synth.SystemParameters.ChorusLock)
                                    return;
                                if (isDelay && synth.SystemParameters.DelayLock)
                                    return;
                                /*
                                0x40 - chorus to delay
                                enable delay that way
                                 */
                                synth.DelayActive |= a3 == 0x40 || isDelay;

                                switch (a3) 
                                {
                                    default: 
                                        Debug.WriteLine(
                                            $"[WARN] Unsupported Patch Common parameter: {a3:X}");
                                        break;

                                    case 0x00: 
                                    {
                                        // Patch name. cool!
                                        // Not sure what to do with it, but let's log it!
                                        var patchName = Util.ReadBinaryString(
                                            syx.Slice(7, 16));
                                        CoolInfo(
                                            $"Patch Name for {a3 & 0x0f}",
                                            Util.ToString(patchName));
                                        break;
                                    }
                                    // Reverb
                                    case 0x30: 
                                        // Reverb macro
                                        synth.ReverbProcessor.Macro = data;
                                        synth.SetReverbMacro(data);
                                        CoolInfo("Reverb Macro", data);
                                        // Event called in setMacro
                                        break;
                                    case 0x31: 
                                        // Reverb character
                                        synth.ReverbProcessor.Character = data;
                                        CoolInfo("Reverb Character", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.Character,
                                                data));
                                        break;
                                    case 0x32:
                                        // Reverb pre-PLF
                                        synth.ReverbProcessor.PreLowPass = data;
                                        CoolInfo("Reverb Pre-LPF", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.PreLowPass,
                                                data));
                                        break;
                                    case 0x33: 
                                        // Reverb level
                                        synth.ReverbProcessor.Level = data;
                                        CoolInfo("Reverb Level", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.Level,
                                                data));
                                        break;
                                    case 0x34: 
                                        // Reverb time
                                        synth.ReverbProcessor.Time = data;
                                        CoolInfo("Reverb Time", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.Time,
                                                data));
                                        break;
                                    case 0x35: 
                                        // Reverb delay feedback
                                        synth.ReverbProcessor.DelayFeedback = data;
                                        CoolInfo("Reverb Delay Feedback", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.DelayFeedback,
                                                data));
                                        break;

                                    case 0x36: 
                                        // Reverb send to chorus, legacy SC-55 that's recognized by later models and unsupported.
                                        break;

                                    case 0x37:
                                        // Reverb predelay time
                                        synth.ReverbProcessor.PreDelayTime = data;
                                        CoolInfo("Reverb Predelay Time", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfReverb(
                                                Effect.FxReverbType.PreDelayTime,
                                                data));
                                        break;

                                    // Chorus
                                    case 0x38: 
                                        // Chorus macro
                                        synth.ChorusProcessor.Macro = data;
                                        synth.SetChorusMacro(data);
                                        CoolInfo("Chorus Macro", data);
                                        // Event called in setMacro
                                        break;
                                    case 0x39: 
                                        // Chorus pre-LPF
                                        synth.ChorusProcessor.PreLowPass = data;
                                        CoolInfo("Pre-LPF", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.PreLowPass,
                                                data));
                                        break;
                                    case 0x3a: 
                                        // Chorus level
                                        synth.ChorusProcessor.Level = data;
                                        CoolInfo("Chorus Level", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.Level,
                                                data));
                                        break;
                                    case 0x3b: 
                                        // Chorus feedback
                                        synth.ChorusProcessor.Feedback = data;
                                        CoolInfo("Chorus Feedback", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.Feedback,
                                                data));
                                        break;
                                    case 0x3c: 
                                        // Chorus delay
                                        synth.ChorusProcessor.Delay = data;
                                        CoolInfo("Chorus Delay", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.Delay,
                                                data));
                                        break;
                                    case 0x3d: 
                                        // Chorus rate
                                        synth.ChorusProcessor.Rate = data;
                                        CoolInfo("Chorus Rate", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.Rate,
                                                data));
                                        break;
                                    case 0x3e: 
                                        // Chorus depth
                                        synth.ChorusProcessor.Depth = data;
                                        CoolInfo("Chorus Depth", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.Depth,
                                                data));
                                        break;
                                    case 0x3f: 
                                        // Chorus send level to reverb
                                        synth.ChorusProcessor.SendLevelToReverb =
                                            data;
                                        CoolInfo(
                                            "Chorus Send Level To Reverb", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.SendLevelToReverb,
                                                data));
                                        break;
                                    case 0x40: 
                                        // Chorus send level to delay
                                        synth.ChorusProcessor.SendLevelToDelay =
                                            data;
                                        CoolInfo(
                                            "Chorus Send Level To Delay", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfChorus(
                                                Effect.FxChorusType.SendLevelToDelay,
                                                data));
                                        break;

                                    // Delay
                                    case 0x50: 
                                        // Delay macro
                                        synth.DelayProcessor.Macro = data;
                                        synth.SetDelayMacro(data);
                                        CoolInfo("Delay Macro", data);
                                        // Event called in setMacro
                                        break;
                                    case 0x51: 
                                        // Delay pre-PLF
                                        synth.DelayProcessor.PreLowPass = data;
                                        CoolInfo("Delay Pre-LPF", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.PreLowPass,
                                                data));
                                        break;
                                    case 0x52: 
                                        // Delay time center
                                        synth.DelayProcessor.TimeCenter = data;
                                        CoolInfo("Delay Time Center", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.TimeCenter,
                                                data));
                                        break;
                                    case 0x53: 
                                        // Delay time ratio left
                                        synth.DelayProcessor.TimeRatioLeft = data;
                                        CoolInfo("Delay Time Ratio Left", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.TimeRatioLeft,
                                                data));
                                        break;
                                    case 0x54: 
                                        // Delay time ratio right
                                        synth.DelayProcessor.TimeRatioRight = data;
                                        CoolInfo("Delay Time Ratio Right", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.TimeRatioRight,
                                                data));
                                        break;
                                    case 0x55: 
                                        // Delay level center
                                        synth.DelayProcessor.LevelCenter = data;
                                        CoolInfo("Delay Level Center", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.LevelCenter,
                                                data));
                                        break;
                                    case 0x56: 
                                        // Delay level left
                                        synth.DelayProcessor.LevelLeft = data;
                                        CoolInfo("Delay Level Left", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.LevelLeft,
                                                data));
                                        break;
                                    case 0x57: 
                                        // Delay level right
                                        synth.DelayProcessor.LevelRight = data;
                                        CoolInfo("Delay Level Right", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.LevelRight,
                                                data));
                                        break;
                                    case 0x58: 
                                        // Delay level
                                        synth.DelayProcessor.Level = data;
                                        CoolInfo("Delay Level", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.Level,
                                                data));
                                        break;
                                    case 0x59: 
                                        // Delay feedback
                                        synth.DelayProcessor.Feedback = data;
                                        CoolInfo("Delay Feedback", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.Feedback,
                                                data));
                                        break;
                                    case 0x5a: 
                                        // Delay send level to reverb
                                        synth.DelayProcessor.SendLevelToReverb =
                                            data;
                                        CoolInfo(
                                            "Delay Send Level To Reverb", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfDelay(
                                                Effect.FxDelayType.SendLevelToReverb,
                                                data));
                                        break;
                                }
                                break;
                            }

                            // EFX Parameter
                            if (a2 == 0x03) 
                            {
                                if (synth.SystemParameters.InsertionEffectLock)
                                    return;

                                // Write parameters
                                if (a3 is >= 0x03 and <= 0x19)
                                    synth.InsertionParams[a3 - 3] = data;

                                if (a3 is >= 0x03 and <= 0x16) 
                                {
                                    synth.InsertionProcessor.SetParameter(a3, data);
                                    CoolInfo($"EFX Parameter {a3 - 2}", data);
                                    synth.CallEvent(
                                        Event.CbEffectChange.OfInsertion(
                                            a3, data));
                                    return;
                                }
                                switch (a3) 
                                {
                                    default: 
                                        Engine.SystemExclusive.NotRecognized(
                                            syx, "Roland GS EFX");
                                        return;

                                    case 0x00: 
                                    {
                                        // EFX Type
                                        var type = (data << 8) | syx[8];
                                        
                                        if (synth.InsertionEffects.TryGetValue(
                                            type, out var proc)) 
                                        {
                                            CoolInfo("EFX Type", 
                                                type.ToString("X"));
                                            synth.InsertionProcessor = proc;
                                        } 
                                        else 
                                        {
                                            synth.InsertionProcessor =
                                                synth.InsertionFallback;
                                            Debug.WriteLine(
                                                $"[WARN] Unsupported EFX processor: {type:X}, using Thru.");
                                        }
                                        synth.ResetInsertionParams();
                                        synth.InsertionProcessor.Reset();
                                        // Special case: 16-bit value
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfInsertion(
                                                0, type));
                                        return;
                                    }

                                    case 0x17: 
                                        // To reverb
                                        // Divide, insertions use 0-1
                                        synth.InsertionProcessor.SendLevelToReverb =
                                            (data / 127f) *
                                            Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                        CoolInfo("EFX Send Level to Reverb", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfInsertion(
                                                a3, data));
                                        return;

                                    case 0x18: 
                                        // To chorus
                                        // Divide, insertions use 0-1
                                        synth.InsertionProcessor.SendLevelToChorus =
                                            (data / 127f) *
                                            Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                        CoolInfo("EFX Send Level to Chorus", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfInsertion(
                                                a3, data));
                                        return;

                                    case 0x19: 
                                        // To delay
                                        // Divide, insertions use 0-1
                                        synth.InsertionProcessor.SendLevelToDelay =
                                            (data / 127f) *
                                            Synthesizer.EFX_SENDS_GAIN_CORRECTION;
                                        synth.DelayActive = true;
                                        CoolInfo("EFX Send Level to Delay", data);
                                        synth.CallEvent(
                                            Event.CbEffectChange.OfInsertion(
                                                a3, data));
                                        return;
                                }
                            }

                            // Patch Parameters
                            if (a2 >> 4 == 1) 
                            {
                                // This is an individual part (channel) parameter
                                // Determine the channel
                                // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                                // SC-8850 manual, page 237
                                var channel =
                                    MidiUtils.ToChannel(a2 & 0x0f) + channelOffset;
                                // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)
                                var ch = synth.MidiChannels[channel];
                                switch (a3) 
                                {
                                    default: 
                                        // This is some other GS sysex...
                                        Engine.SystemExclusive.NotRecognized(syx, "Roland GS");
                                        return;

                                    case 0x00: 
                                        // Tone number (program change)
                                        ch.ControllerChange(
                                            Midi.CC.BankSelect, data);
                                        ch.ProgramChange(syx[8]);
                                        break;

                                    case 0x02: 
                                        // Rx. channel (0x10 is OFF)
                                        var rxChannel =
                                            data == 0x10
                                                ? -1
                                                : data + channelOffset;
                                        ch.Set((
                                            ChannelMidiParameter.Type.RxChannel,
                                            rxChannel));
                                        synth.CustomChannelNumbers |=
                                            rxChannel != ch.Channel;
                                        CoolInfo(
                                            $"Rx. Channel on {channel}", rxChannel);
                                        break;

                                    case 0x13: 
                                        // Mono/poly
                                        var poly = data == 1;
                                        ch.Set((
                                            ChannelMidiParameter.Type.PolyMode,
                                            poly));
                                        CoolInfo(
                                            $"Mono/poly on {channel}",
                                            poly ? "POLY" : "MONO");
                                        break;

                                    case 0x14:
                                        ch.Set((MidiChannel.Assign)data);
                                        CoolInfo($"MidiChannel.Assign mode on {channel}", data);
                                        break;

                                    case 0x15: 
                                        // This is the Use for Drum Part sysex (multiple drums)
                                        ch.Set((
                                            ChannelMidiParameter.Type.DrumMap, 
                                            data));
                                        var isDrums = data > 0; // If set to other than 0, is a drum channel
                                        ch.SetGSDrums(isDrums);
                                        CoolInfo($"Drums on {channel}", isDrums);
                                        return;

                                    case 0x16: 
                                    {
                                        // This is the pitch key shift sysex
                                        var keyShift = data - 64;
                                        ch.KeyShift(keyShift);
                                        return;
                                    }

                                    // Pitch offset fine in Hz is not supported so far
                                    case 0x19: 
                                        // Part level (cc#7)
                                        ch.ControllerChange(
                                            Midi.CC.MainVolume, data);
                                        return;
                                    
                                    case 0x1a:
                                        // Velocity Sense Depth
                                        ch.Set((
                                            ChannelMidiParameter.Type.VelocitySenseDepth,
                                            data));
                                        SpessaLog.GSInfo(
                                            "Velocity Sense Depth", data);
                                        return;

                                    case 0x1b: 
                                        // Velocity Sense Offset
                                        ch.Set((
                                            ChannelMidiParameter.Type.VelocitySenseOffset,
                                            data));
                                        SpessaLog.GSInfo(
                                            "Velocity Sense Offset", data);
                                        return;

                                    // Pan position
                                    case 0x1c: 
                                    {
                                        // 0 is random
                                        var panPosition = data;
                                        var randomPan = panPosition == 0;
                                        ch.Set((
                                            ChannelMidiParameter.Type.RandomPan,
                                            randomPan));
                                        
                                        if (randomPan) 
                                        {
                                            CoolInfo(
                                                $"Random pan on {channel}", "ON");
                                        } 
                                        else 
                                        {
                                            ch.ControllerChange(
                                                Midi.CC.Pan, panPosition);
                                        }
                                        break;
                                    }

                                    case 0x1f: 
                                        // CC1 controller number
                                        ch.Set(new ChannelMidiParameter(
                                            ChannelMidiParameter.Type.CC1, 
                                            (Midi.CC)data));
                                        CoolInfo("CC1 Controller Number", data);
                                        break;

                                    case 0x20: 
                                        // CC2controller number
                                        ch.Set(new ChannelMidiParameter(
                                            ChannelMidiParameter.Type.CC2, 
                                            (Midi.CC)data));
                                        CoolInfo("CC2 Controller Number", data);
                                        break;

                                    // Chorus send
                                    case 0x21: 
                                        ch.ControllerChange(
                                            Midi.CC.ChorusDepth, data);
                                        break;

                                    // Reverb send
                                    case 0x22: 
                                        ch.ControllerChange(
                                            Midi.CC.ReverbDepth, data);
                                        break;

                                    case 0x2a: 
                                    {
                                        // Fine tune
                                        // 0-16384
                                        var tune = (data << 7) | syx[8];
                                        var tuneCents = (tune - 8_192) / 81.92f;
                                        ch.FineTune(tuneCents);
                                        break;
                                    }

                                    // Delay send
                                    case 0x2c: 
                                        ch.ControllerChange(
                                            Midi.CC.VariationDepth, data);
                                        break;

                                    case 0x30: 
                                        // Vibrato rate
                                        ch.ControllerChange(
                                            Midi.CC.VibratoRate, data);
                                        break;

                                    case 0x31: 
                                        // Vibrato depth
                                        ch.ControllerChange(
                                            Midi.CC.VibratoDepth, data);
                                        break;

                                    case 0x32: 
                                        // Filter cutoff
                                        // It's so out of order, Roland...
                                        ch.ControllerChange(
                                            Midi.CC.Brightness, data);
                                        break;

                                    case 0x33: 
                                        // Filter resonance
                                        ch.ControllerChange(
                                            Midi.CC.FilterResonance, data);
                                        break;

                                    case 0x34: 
                                        // Attack time
                                        ch.ControllerChange(
                                            Midi.CC.AttackTime, data);
                                        break;

                                    case 0x35: 
                                        // Decay time
                                        ch.ControllerChange(
                                            Midi.CC.DecayTime, data);
                                        break;

                                    case 0x36: 
                                        // Release time
                                        ch.ControllerChange(
                                            Midi.CC.ReleaseTime, data);
                                        break;

                                    case 0x37: 
                                        // Vibrato delay
                                        // It seems that they forgot about it and put it last...
                                        ch.ControllerChange(
                                            Midi.CC.VibratoDelay, data);
                                        break;

                                    case 0x40: 
                                    {
                                        // Scale tuning: up to 12 bytes
                                        var tuningBytes = syx.Length - 9; // Data starts at 7, minus checksum and f7
                                        // Read em bytes
                                        var newTuning = (Span<byte>)stackalloc byte[12];
                                        for (var i = 0; i < tuningBytes; i++) 
                                            newTuning[i] = (byte)(syx[i + 7] - 64);
                                        ch.SetOctaveTuning(newTuning);
                                        var cents = data - 64;
                                        CoolInfo(
                                            $"Octave Scale Tuning on {channel}",
                                            string.Join(", ", newTuning.ToArray()));
                                        ch.FineTune(cents);
                                        break;
                                    }
                                }
                                
                                return;
                            }

                            // Patch Parameter controllers
                            if (a2 >> 4 == 2) 
                            {
                                // This is an individual part (channel) parameter
                                // Determine the channel
                                // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                                // SC-8850 manual, page 237
                                var channel =
                                    MidiUtils.ToChannel(a2 & 0x0f) + channelOffset;
                                // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)
                                var ch = synth.MidiChannels[channel];
                                switch (a3 & 0xf0) 
                                {
                                    default: 
                                        // This is some other GS sysex...
                                        Engine.SystemExclusive.NotRecognized(
                                            syx, 
                                            "Roland GS Patch Parameter Controller");
                                        break;

                                    case 0x00: 
                                        // Modulation wheel
                                        if ((a3 & 0x0f) == 0x04) {
                                            // LFO1 Pitch depth
                                            // Special case:
                                            // If the source is a mod wheel, it's a strange way of setting the modulation depth
                                            // Testcase: J-Cycle.mid (it affects gm.dls which uses LFO1 for modulation)
                                            var cents = (data / 127f) * 600;
                                            ch.ModulationDepth(cents);
                                            break;
                                        }
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            (int)Midi.CC.ModulationWheel,
                                            true,
                                            "mod wheel");
                                        break;

                                    case 0x10: 
                                        // Pitch wheel
                                        if ((a3 & 0x0f) == 0x00) 
                                        {
                                            // See https://github.com/spessasus/SpessaSynth/issues/154
                                            // Pitch control
                                            // Special case:
                                            // If the source is a pitch wheel, it's a strange way of setting the pitch wheel range
                                            // Testcase: th07_03.mid
                                            var centeredValue = data - 64;
                                            ch.PitchWheelRange(centeredValue);
                                            break;
                                        }
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            Modulator.Source.ID(Modulator.Source.ControllerSource.PitchWheel),
                                            false,
                                            "pitch wheel",
                                            true);
                                        break;

                                    case 0x20: 
                                        // Channel pressure
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            Modulator.Source.ID(Modulator.Source.ControllerSource.ChannelPressure),
                                            false,
                                            "channel pressure");
                                        break;

                                    case 0x30: 
                                        // Poly pressure
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            Modulator.Source.ID(Modulator.Source.ControllerSource.PolyPressure),
                                            false,
                                            "poly pressure");
                                        break;

                                    case 0x40: 
                                        // CC1
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            (int)ch.MidiParamArray.CC1,
                                            true,
                                            "CC1");
                                        break;

                                    case 0x50: 
                                        // CC2
                                        ch.DynamicModulators.SetupReceiver(
                                            a3,
                                            data,
                                            (int)ch.MidiParamArray.CC2,
                                            true,
                                            "CC2");
                                        break;
                                }
                                return;
                            }

                            // Patch Parameter Tone Map
                            if (a2 >> 4 == 4) 
                            {
                                // This is an individual part (channel) parameter
                                // Determine the channel
                                // Note that: 0 means channel 9 (drums), and only then 1 means channel 0, 2 channel 1, etc.
                                // SC-8850 manual, page 237
                                var channel =
                                    MidiUtils.ToChannel(a2 & 0x0f) + channelOffset;
                                // For example, 0x1A means A = 11, which corresponds to channel 12 (counting from 1)
                                var ch = synth.MidiChannels[channel];

                                switch (a3) 
                                {
                                    default: 
                                        // This is some other GS sysex...
                                        Engine.SystemExclusive.NotRecognized(
                                            syx,
                                            "Roland GS Patch Part Parameter");
                                        break;

                                    case 0x00:
                                    case 0x01: 
                                        // Tone map number (cc#32)
                                        ch.ControllerChange(
                                            Midi.CC.BankSelectLSB,
                                            data);
                                        break;

                                    case 0x22: 
                                    {
                                        if (synth.SystemParameters.InsertionEffectLock)
                                            return;
                                        // EFX assign
                                        var efx = data == 1;
                                        ch.Set((
                                            ChannelMidiParameter.Type.EfxAssign, 
                                            efx));
                                        synth.InsertionActive |= efx;
                                        CoolInfo(
                                            $"SynthEvent.CbEffectChange.Insertion for {channel}",
                                            efx ? "ON" : "OFF");
                                        break;
                                    }
                                }
                                return;
                            }
                            Engine.SystemExclusive.NotRecognized(syx, "Roland GS Patch Parameter");
                            return;
                        }
                        // Drum setup
                        if (a1 == 0x41) 
                        {
                            if (synth.SystemParameters.DrumLock) 
                                return;
                            var map = (a2 >> 4) + 1;
                            var drumKey = a3;
                            var param = a2 & 0xf;
                            switch (param) 
                            {
                                default: 
                                    Engine.SystemExclusive.NotRecognized(syx, "Roland GS Drum Setup");
                                    return;

                                case 0x0: 
                                {
                                    // Drum map name. cool!
                                    // Not sure what to do with it, but let's log it!
                                    var patchName = 
                                        Util.ReadBinaryString(syx.Slice(7, 12));
                                    CoolInfo(
                                        $"Patch Name for MAP{map}", 
                                        Util.ToString(patchName));
                                    break;
                                }

                                case 0x1: 
                                {
                                    // Here it's relative to 60, not 64 like NRPN. For some reason...

                                    var pitch = data - 60;
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        // Apply same thing: SC-55 uses 100 cents, SC-88 and above is 50
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { Pitch = pitch *
                                            (ch.Patch.BankLSB == 1 ? 100 : 50) };
                                    }
                                    CoolInfo(
                                        $"Drum Pitch for MAP{map}, key {drumKey}",
                                        pitch);
                                    break;
                                }

                                case 0x2: 
                                    // Drum Level
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { Gain = data / 120f };
                                    }
                                    CoolInfo(
                                        $"Drum Level for MAP{map}, key {drumKey}",
                                        data);
                                    break;

                                case 0x3: 
                                    // Drum Assign Group (exclusive class)
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { ExclusiveClass = data };
                                    }
                                    CoolInfo(
                                        $"Drum Assign Group for MAP{map}, key {drumKey}",
                                        data);
                                    break;

                                case 0x4: 
                                    // Pan
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { Pan = data };
                                    }
                                    CoolInfo(
                                        $"Drum Pan for MAP{map}, key {drumKey}",
                                        data);
                                    break;

                                case 0x5: 
                                    // Reverb
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { ReverbGain = data / 127f };
                                    }
                                    CoolInfo(
                                        $"Drum Reverb for MAP{map}, key {drumKey}",
                                        data);
                                    break;

                                case 0x6: 
                                    // Chorus
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { ChorusGain = data / 127f };
                                    }
                                    CoolInfo(
                                        $"Drum Chorus for MAP{map}, key {drumKey}",
                                        data);
                                    break;

                                case 0x7: 
                                    // Receive Note Off
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { RxNoteOff = data == 1 };
                                    }
                                    CoolInfo(
                                        $"Drum Note Off for MAP{map}, key {drumKey}",
                                        data == 1);
                                    break;

                                case 0x8: 
                                    // Receive Note On
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { RxNoteOn = data == 1 };
                                    }
                                    CoolInfo(
                                        $"Drum Note On for MAP{map}, key {drumKey}",
                                        data == 1);
                                    break;

                                case 0x9: 
                                    // Delay
                                    foreach (var ch in synth.MidiChannels) 
                                    {
                                        if (ch.MidiParamArray.DrumMap != map)
                                            continue;
                                        ref var dp = ref ch.DrumParams[drumKey];
                                        dp = dp with { DelayGain = data / 127f };
                                    }
                                    CoolInfo(
                                        $"Drum Delay for MAP{map}, key {drumKey}",
                                        data
                                    );
                                    break;
                            }
                            return;
                        }
                        // This is some other GS sysex...
                        Engine.SystemExclusive.NotRecognized(syx, "Roland GS");
                        return;
                    }
                }

                // GS Display
                case 0x45: 
                    // 0x45: GS Display Data
                    // Check for embedded copyright
                    // (Roland SC display sysex) http://www.bandtrax.com.au/sysex.htm
                    if (syx[4] == 0x10) // Sound Canvas Display
                    {
                        if (syx[5] == 0x00)
                        {
                            // Display letters
                            synth.CallEvent(new Event.CbDisplayMessage(
                            syx.ToArray()));
                        } 
                        else if (syx[5] == 0x01) 
                        {
                            // Matrix display
                            synth.CallEvent(new Event.CbDisplayMessage(
                            syx.ToArray()));
                        } 
                        else 
                        {
                            // This is some other GS sysex...
                            Engine.SystemExclusive.NotRecognized(syx, "Roland GS Display");
                        }
                    }
                    return;

                // Some Roland
                case 0x16: 
                    if (syx[4] == 0x10) 
                    {
                        // This is a roland master volume message
                        synth.Set((
                            GlobalMidiParameter.Type.Gain,
                            syx[7] / 100f));
                        Debug.WriteLine(
                            $"Roland Master Volume control set to: {syx[7]}");
                        return;
                    } 
                    else 
                    {
                        Engine.SystemExclusive.NotRecognized(syx, "Roland");
                    }

                    break;
            }
        } 
        else 
        {
            // This is something else...
            Engine.SystemExclusive.NotRecognized(syx, "Roland");
            return;
        }
    }
    
    [Conditional("DEBUG")]
    private static void CoolInfo(string what, string value) =>
        Debug.WriteLine($"Roland GS {what} for is now set to {value}.");
    
    [Conditional("DEBUG")]
    private static void CoolInfo(string what, bool value) =>
        Debug.WriteLine($"Roland GS {what} for is now set to {value}.");
    
    [Conditional("DEBUG")]
    private static void CoolInfo(string what, float value) =>
        Debug.WriteLine($"Roland GS {what} for is now set to {value}.");
}