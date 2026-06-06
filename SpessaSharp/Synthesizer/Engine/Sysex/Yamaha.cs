using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Sysex;

internal static class Yamaha
{
    /// <summary>
    /// Handles a Yamha XG system exclusive<br/>
    /// http://www.studio4all.de/htmle/main91.html
    /// </summary>
    /// <param name="synth"></param>
    /// <param name="syx"></param>
    /// <param name="channelOffset"></param>
    public static void SystemExclusive(
        Synthesizer synth, ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        // XG sysex
        if (syx.Length >= 3 && syx[2] == 0x4c) 
        {
            var a1 = syx[3]; // Address 1
            var a2 = syx[4]; // Address 2
            var a3 = syx[5]; // Address 3
            var data = syx[6];
            // XG system parameter
            if (a1 == 0x00 && a2 == 0x00) 
            {
                switch (a3) 
                {
                    // Master tune
                    case 0x00: 
                    {
                        {
                            var tune =
                                ((syx[6] & 15) << 12) |
                                ((syx[7] & 15) << 8) |
                                ((syx[8] & 15) << 4) |
                                (syx[9] & 15);
                            var cents = (tune - 1_024) / 10f;
                            synth.Set((
                                GlobalMidiParameter.Type.FineTune,
                                cents));
                            SpessaLog.XGInfo("Master Tune", cents);
                        }
                        break;
                    }

                    // Master volume
                    case 0x04: 
                        synth.Set((
                            GlobalMidiParameter.Type.Volume,
                            data / 127f));
                        SpessaLog.XGInfo("Master Volume", data);
                        break;

                    // Master attenuation
                    case 0x05: 
                        var vol = 127 - data;
                        synth.Set((
                            GlobalMidiParameter.Type.Volume,
                            vol / 127f));
                        SpessaLog.XGInfo("Master Attenuation", data);
                        break;

                    // Master transpose
                    case 0x06: 
                        var transpose = data - 64;
                        synth.Set((
                            GlobalMidiParameter.Type.KeyShift,
                            transpose));
                        SpessaLog.CoolInfo("Master Transpose", transpose);
                        break;

                    // XG Reset
                    // XG on
                    case 0x7f:
                    case 0x7e: 
                        Debug.WriteLine("XG system on");
                        synth.Reset(Midi.System.XG);
                        break;
                }
                return;
            }
        
            if (a1 == 0x02 && a2 == 0x01) 
            {
                var effect = a3;
                var effectType = effect switch
                {
                    <= 0x15 => "Reverb",
                    <= 0x35 => "Chorus",
                    _ => "Variation",
                };

                Debug.WriteLine(
                    $"[WARN] Unsupported XG {effectType} Parameter: {effect:X}");
                return;
            }

            if (a1 == 0x08/* A2 is the channel number*/) 
            {
                // XG part parameter
                if (!BankSelectHacks.IsSystemXG(
                        synth.MidiParameters.MidiSystem))
                    return;

                var channel = a2 + channelOffset;
                if (channel >= synth.MidiChannels.Count)
                {
                    // Invalid channel
                    Debug.WriteLine(
                        $"[WARN] Discarding XG SysEx with invalid part number: {channel}");
                    return;
                }

                var ch = synth.MidiChannels[channel];
                switch (a3) 
                {
                    default: 
                        Debug.WriteLine(
                            $"[WARN] Unsupported Yamaha XG Part Setup: {
                                syx[5]:X} for channel {channel}");
                        break;
                    
                    // Bank-select MSB
                    case 0x01: 
                        ch.ControllerChange(
                            Midi.CC.BankSelect, data);
                        break;

                    // Bank-select LSB
                    case 0x02: 
                        ch.ControllerChange(
                            Midi.CC.BankSelectLSB, data);
                        break;

                    // Program change
                    case 0x03: 
                        ch.ProgramChange(data);
                        break;

                    // Rev. channel
                    case 0x04: 
                        var rxChannel = data + channelOffset;
                        ch.Set((
                            ChannelMidiParameter.Type.RxChannel, rxChannel));
                        
                        synth.CustomChannelNumbers = 
                            synth.CustomChannelNumbers ||
                            (rxChannel != ch.Channel);
                        SpessaLog.XGInfo(
                            $"Rev. Channel on {channel}",
                            rxChannel);
                        break;

                    // Poly/mono
                    case 0x05:
                        var poly = data == 1;
                        ch.Set((ChannelMidiParameter.Type.PolyMode, poly));
                        SpessaLog.XGInfo(
                            $"Mono/poly on {channel}",
                            poly ? "POLY" : "MONO");
                        break;
                    
                    // Same note number key on assign
                    case 0x06:
                        ch.Set((MidiChannel.Assign)data);
                        SpessaLog.XGInfo(
                            $"Same Note Number Key On Assign on {channel}",
                            data);
                        break;

                    // Part mode
                    case 0x07:
                        var drums = data != 0;
                        ch.SetDrums(drums);
                        SpessaLog.XGInfo(
                            $"Part Mode on {channel}",
                            drums ? "DRUM" : "MELODIC");
                        break;

                    // Note shift
                    case 0x08:
                        var keyShift = data - 64;
                        ch.Set((ChannelMidiParameter.Type.KeyShift, keyShift));
                        SpessaLog.XGInfo($"Key Shift onf {channel}", keyShift);
                        break;

                    // Volume
                    case 0x0b: 
                        ch.ControllerChange(
                            Midi.CC.MainVolume, data);
                        break;
                    
                    // Velocity Sense Depth
                    case 0x0c:
                        ch.Set((
                            ChannelMidiParameter.Type.VelocitySenseDepth,
                            data));
                        SpessaLog.XGInfo(
                            $"Velocity Sense Depth on {channel}", data);
                        return;

                    // Velocity Sense Offset
                    case 0x0d:
                        ch.Set((
                            ChannelMidiParameter.Type.VelocitySenseOffset,
                            data));
                        SpessaLog.XGInfo(
                            $"Velocity Sense Offset on {channel}", data);
                        return;

                    // Pan position
                    case 0x0e: 
                    {
                        var pan = data;
                        var randomPan = pan == 0;
                        ch.Set(
                            (ChannelMidiParameter.Type.RandomPan, randomPan));
                        
                        if (randomPan) 
                        {
                            // 0 means random
                            SpessaLog.XGInfo($"Random Pan for {channel}", "ON");
                        } 
                        else
                            ch.ControllerChange(
                                Midi.CC.Pan, pan);

                        break;
                    }

                    // Chorus
                    case 0x12: 
                        ch.ControllerChange(
                            Midi.CC.ChorusDepth, data);
                        break;

                    // Reverb
                    case 0x13: 
                        ch.ControllerChange(
                            Midi.CC.ReverbDepth, data);
                        break;

                    // Vibrato rate
                    case 0x15: 
                        ch.ControllerChange(
                            Midi.CC.VibratoRate, data);
                        break;

                    // Vibrato depth
                    case 0x16: 
                        ch.ControllerChange(
                            Midi.CC.VibratoDepth, data);
                        break;

                    // Vibrato delay
                    case 0x17: 
                        ch.ControllerChange(
                            Midi.CC.VibratoDelay, data);
                        break;

                    // Filter cutoff
                    case 0x18: 
                        ch.ControllerChange(
                            Midi.CC.Brightness, data);
                        break;

                    // Filter resonance
                    case 0x19: 
                        ch.ControllerChange(
                            Midi.CC.FilterResonance, data);
                        break;

                    // Attack time
                    case 0x1a: 
                        ch.ControllerChange(
                            Midi.CC.AttackTime, data);
                        break;

                    // Decay time
                    case 0x1b: 
                        ch.ControllerChange(
                            Midi.CC.DecayTime, data);
                        break;

                    // Release time
                    case 0x1c: 
                        ch.ControllerChange(
                            Midi.CC.ReleaseTime, data);
                        break;

                    case 0x23:
                    {
                        // Bend pitch control (pitch wheel range)
                        var centeredValue = data - 64;
                        ch.Set((ChannelMidiParameter.Type.PitchWheelRange, centeredValue));
                        SpessaLog.XGInfo(
                            $"Pitch Wheel Range for {channel}",
                            centeredValue,
                        "semitones");
                        break;
                    }
                }
                return;
            }

            if (a1 >> 4 == 3) 
            {
                // Drum part setup
                if (synth.SystemParameters.DrumLock) return;
                var drumKey = a2;
                switch (a3) 
                {
                    default: 
                        Engine.SystemExclusive.NotRecognized(
                            [a3], "Yamaha XG Drum Setup");
                        return;

                    case 0x00: 
                    {
                        // Drum pitch coarse
                        var pitch = (data - 64) * 100;
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { Pitch = pitch };
                        }
                        SpessaLog.XGInfo(
                            $"Drum Pitch for key {drumKey}",
                            pitch,
                        "semitones");
                        break;
                    }

                    case 0x01: 
                    {
                        // Drum pitch fine
                        var pitch = data - 64;
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            var newPitch = param.Pitch + pitch;
                            param = param with { Pitch = newPitch };
                            SpessaLog.XGInfo(
                                $"Drum Pitch for key {drumKey}",
                                ch.DrumParams[drumKey].Pitch,
                            "semitones");
                        }
                        break;
                    }

                    case 0x02: 
                        // Drum Level
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { Gain = data / 120f };
                        }
                        SpessaLog.XGInfo($"Drum Level for key {drumKey}", data);
                        break;

                    case 0x03: 
                        // Drum Alternate Group (exclusive class)
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { ExclusiveClass = data };
                        }
                        SpessaLog.XGInfo($"Drum Alternate Group for key {drumKey}", data);
                        break;

                    case 0x04: 
                        // Drum Pan
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { Pan = data };
                        }
                        SpessaLog.XGInfo($"Drum Pan for key {drumKey}", data);
                        break;

                    case 0x05: 
                        // Drum Reverb
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { ReverbGain = data / 127f };
                        }
                        SpessaLog.XGInfo($"Drum Reverb for key {drumKey}", data);
                        break;

                    case 0x06: 
                        // Drum Chorus
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { ChorusGain = data / 127f };
                        }
                        SpessaLog.XGInfo($"Drum Chorus for key {drumKey}", data);
                        break;

                    case 0x09: 
                        // Receive note off
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { RxNoteOff = data == 1 };
                        }
                        SpessaLog.XGInfo($"Drum Note Off, key {drumKey}", data == 1);
                        break;

                    case 0x0a: 
                        // Receive note on
                        foreach (var ch in synth.MidiChannels) 
                        {
                            if (!ch.DrumChannel) continue;
                            ref var param = ref ch.DrumParams[drumKey];
                            param = param with { RxNoteOn = data == 1 };
                        }
                        SpessaLog.XGInfo($"Drum Note On, key {drumKey}", data == 1);
                        break;
                }
                return;
            }

            if (a1 == 0x06 || // Display letters
                a1 == 0x07) // Display bitmap
                // Displayed letters
                synth.CallEvent(new Event.CbDisplayMessage(
                    syx.ToArray()));
            else
                SpessaLog.XGFail("System Exclusive", syx, "Unknown address");
        } 
        else
        {
            SpessaLog.XGFail("System Exclusive", syx);
        }
    }
}