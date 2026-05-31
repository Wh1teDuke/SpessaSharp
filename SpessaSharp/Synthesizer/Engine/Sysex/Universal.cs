using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Sysex;

internal static class Universal
{
    /// <summary> Calculates frequency for MIDI Tuning Standard. </summary>
    /// <param name="byte1">The first byte (midi note).</param>
    /// <param name="byte2">The second byte (most significant bits).</param>
    /// <param name="byte3">The third byte (the least significant bits).</param>
    /// <returns>An object containing the MIDI note and the cent tuning value.</returns>
    private static float GetTuning(int byte1, int byte2, int byte3)
    {
        var midiNote = byte1;
        var fraction = (byte2 << 7) | byte3; // Combine byte2 and byte3 into a 14-bit number

        // No change
        if (byte1 == 0x7f && byte2 == 0x7f && byte3 == 0x7f)
            return -1;

        // Calculate cent tuning (divide cents by 100 so it works in semitones)
        return midiNote + fraction * 0.000_061f;
    }
    
    /// <summary> Handles a Universal system exclusive (realtime/non-realtime) </summary>
    /// <param name="synth"></param>
    /// <param name="syx"></param>
    /// <param name="channelOffset"></param>
    public static void SystemExclusive(
        Synthesizer synth, ReadOnlySpan<byte> syx, int channelOffset = 0)
    {
        if (syx.Length <= 2) return;
        
        switch (syx[2]) 
        {
            // Device control
            case 0x04: 
                switch (syx[3]) 
                {
                    default: 
                        Debug.WriteLine(
                            $"[WARN] Unrecognized MIDI Device Control real-time message: {
                                Util.ToHexString(syx)}");
                        break;
                    
                    case 0x01: 
                    {
                        // Master volume
                        var vol = (syx[5] << 7) | syx[4];
                        // It corresponds to CC volume, so volume is squared.
                        var gain = float.Pow(vol / 16_383f, 2);
                        synth.Set((
                            GlobalMidiParameter.Type.Gain, gain));
                        SpessaLog.GMInfo("Master Volume", vol);
                        break;
                    }

                    case 0x02: 
                    {
                        // Master balance
                        // Complete MIDI 1.0 Detailed Specification page 57
                        // This is not specified in GM2 spec for some reason
                        var balance = (syx[5] << 7) | syx[4];
                        var pan = (balance - 8_192) / 8_192f;
                        synth.Set((
                            GlobalMidiParameter.Type.Pan, pan));
                        SpessaLog.GMInfo("Master Balance", pan);
                        break;
                    }

                    case 0x03: 
                    {
                        // Fine-tuning
                        var tuningValue = ((syx[5] << 7) | syx[4]) - 8_192;
                        var cents = tuningValue / 81.92f; // [-100;+99] cents range
                        synth.Set((
                            GlobalMidiParameter.Type.FineTune, cents));
                        SpessaLog.GMInfo("Master Fine Tuning", cents);
                        break;
                    }

                    case 0x04: 
                    {
                        // Coarse tuning
                        // Lsb is ignored
                        var keyShift = syx[5] - 64;
                        synth.Set((
                            GlobalMidiParameter.Type.KeyShift, keyShift));
                        Debug.WriteLine(
                            $"Master Coarse Tuning. Key shift: {keyShift}");
                        break;
                    }
                    
                    case 0x05: 
                    {
                        // Global Parameter control
                        if (
                            syx[4] != 0x01 || // Slot Path Length
                            syx[5] != 0x01 || // Parameter ID Width
                            syx[6] != 0x01 || // Value Width
                            syx[7] != 0x01 // Slot Path MSB
                        )
                        {
                            Unsupported("General MIDI Global Parameter Control", syx);
                            break;
                        }
                        // Slot Path LSB
                        switch (syx[8]) {
                            default: 
                            {
                                Unsupported("General MIDI Global Parameter Control", syx);
                                break;
                            }

                            case 0x01: 
                            {
                                // Reverb
                                var value = syx[10];
                                // Parameter
                                switch (syx[9]) 
                                {
                                    default: 
                                    {
                                        Unsupported(
                                            "General MIDI Reverb Parameter Control", syx);
                                        break;
                                    }

                                    case 0x00: 
                                    {
                                        // Reverb type
                                        // Match 8850 manual, page 231
                                        // All match except for plate which is 8 in GM and 5 in GS
                                        var macro = value == 0x08 ? 0x05 : value;
                                        synth.SetReverbMacro(macro);
                                        SpessaLog.GMInfo("Reverb Type", macro);
                                        break;
                                    }

                                    case 0x01: 
                                    {
                                        // Reverb time
                                        synth.ReverbProcessor.Time = value;
                                        SpessaLog.GMInfo("Reverb Time", value);
                                        break;
                                    }
                                }
                                break;
                            }

                            case 0x02: 
                            {
                                // Chorus
                                var value = syx[10];
                                // Parameter
                                switch (syx[9]) 
                                {
                                    default: 
                                    {
                                        Unsupported(
                                            "General MIDI Chorus Parameter Control", syx);
                                        break;
                                    }

                                    case 0x00: 
                                    {
                                        // Chorus type
                                        // Match 8850 manual, page 231
                                        // All match
                                        synth.SetChorusMacro(value);
                                        SpessaLog.GMInfo("Chorus Type", value);
                                        break;
                                    }

                                    case 0x01: 
                                    {
                                        // Mod rate
                                        synth.ChorusProcessor.Rate = value;
                                        SpessaLog.GMInfo("Chorus Mod Rate",value);
                                        break;
                                    }

                                    case 0x02: 
                                    {
                                        // Mod depth
                                        synth.ChorusProcessor.Depth = value;
                                        SpessaLog.GMInfo("Chorus Mod Depth", value);
                                        break;
                                    }

                                    case 0x03: {
                                        // Mod feedback
                                        synth.ChorusProcessor.Feedback = value;
                                        SpessaLog.GMInfo("Chorus Mod Feedback", value);
                                        break;
                                    }

                                    case 0x04: {
                                        // Mod send to reverb
                                        synth.ChorusProcessor.SendLevelToReverb =
                                            value;
                                        SpessaLog.GMInfo("Chorus Send to Reverb", value);
                                        break;
                                    }
                                }

                                break;
                            }
                        }

                        break;
                    }
                }
                break;
        
            // General MIDI
            case 0x09: 
                // Gm system related
                if (syx[3] == 0x01) 
                {
                    Debug.WriteLine("GM1 system on");
                    synth.Reset(Midi.System.GM);
                } 
                else if (syx[3] == 0x03) 
                {
                    Debug.WriteLine("GM2 system on");
                    synth.Reset(Midi.System.GM2);
                } 
                else 
                {
                    Debug.WriteLine(
                        $"[WARN] Unrecognized General MIDI message: {
                            Util.ToHexString(syx)}");
                }
                break;
            
            case 0x01: 
                Debug.WriteLine("GM1 system on");
                synth.Reset(Midi.System.GM);
                break;

            case 0x02: 
                Debug.WriteLine("GM system off, switching to GS");
                synth.Reset(Midi.System.GS);
                break;

            case 0x03: 
                Debug.WriteLine("GM2 system on");
                synth.Reset(Midi.System.GM2);
                break;
        
            // MIDI Tuning standard
            // https://midi.org/midi-tuning-updated-specification
            case 0x08: 
                var currentMessageIndex = 4;
                switch (syx[3]) 
                {
                    // Bulk tuning dump: all 128 notes
                    case 0x01: 
                    {
                        var program = syx[currentMessageIndex++];
                        // Read the name
                        var tnSlice = syx.Slice(
                            currentMessageIndex, 16);
                        currentMessageIndex += 16;
                        if (syx.Length < 384) 
                        {
                            Debug.WriteLine(
                                $"[WARN] The Bulk Tuning Dump is too short! ({syx.Length
                                } bytes, at least 384 are expected)");
                            return;
                        }
                        // 128 frequencies follow
                        for (var midiNote = 0; midiNote < 128; midiNote++) 
                        {
                            // Set the given tuning to the program
                            synth.Tunings[program * 128 + midiNote] = GetTuning(
                                syx[currentMessageIndex++],
                                syx[currentMessageIndex++],
                                syx[currentMessageIndex++]);
                        }
                        
                        Debug.WriteLine(
                            $"Bulk Tuning Dump {Util.ToString(
                                Util.ReadBinaryString(tnSlice))
                            } Program: {program}");
                        break;
                    }

                    // Single note change
                    // Single note change bank
                    case 0x02:
                    case 0x07: 
                    {
                        if (syx[3] == 0x07) 
                        {
                            // Skip the bank
                            currentMessageIndex++;
                        }
                        // Get program and number of changes
                        var tuningProgram = syx[currentMessageIndex++];
                        var numberOfChanges = syx[currentMessageIndex++];
                        for (var i = 0; i < numberOfChanges; i++) 
                        {
                            var midiNote = syx[currentMessageIndex++];
                            // Set the given tuning to the program
                            synth.Tunings[tuningProgram * 128 + midiNote] =
                                GetTuning(
                                    syx[currentMessageIndex++],
                                    syx[currentMessageIndex++],
                                    syx[currentMessageIndex++]);
                        }
                        Debug.WriteLine(
                            $"Single Note Tuning. Program: {tuningProgram
                            } Keys affected: {numberOfChanges}");
                        break;
                    }

                    // Octave tuning (1 byte)
                    // And octave tuning (2 bytes)
                    case 0x09:
                    case 0x08: 
                    {
                        // Get tuning:
                        var newOctaveTuning = (Span<byte>)stackalloc byte[12];
                        
                        // Start from bit 7
                        if (syx[3] == 0x08) 
                        {
                            // 1 byte tuning: 0 is -64 cents, 64 is 0, 127 is +63
                            for (var i = 0; i < 12; i++) 
                                newOctaveTuning[i] = (byte)(syx[7 + i] - 64);
                        } 
                        else 
                        {
                            // 2 byte tuning. Like fine tune: 0 is -100 cents, 8192 is 0 cents, 16,383 is +100 cents
                            for (var i = 0; i < 24; i += 2) 
                            {
                                var tuning =
                                    ((syx[7 + i] << 7) | syx[8 + i]) - 8_192;
                                newOctaveTuning[i / 2] = (byte)float.Floor(
                                    tuning / 81.92f); // Map to [-100;+99] cents
                            }
                        }
                        // Apply to channels (ordered from 0)
                        // Bit 1: 14 and 15
                        if ((syx[4] & 1) == 1)
                            synth.MidiChannels[14 + channelOffset]
                                .SetOctaveTuning(newOctaveTuning);
                        if (((syx[4] >> 1) & 1) == 1)
                            synth.MidiChannels[15 + channelOffset]
                                .SetOctaveTuning(newOctaveTuning);

                        // Bit 2: channels 7 to 13
                        for (var i = 0; i < 7; i++) 
                        {
                            var bit = (syx[5] >> i) & 1;
                            if (bit == 1)
                                synth.MidiChannels[7 + i + channelOffset]
                                    .SetOctaveTuning(newOctaveTuning);
                        }

                        // Bit 3: channels 0 to 16
                        for (var i = 0; i < 7; i++) 
                        {
                            var bit = (syx[6] >> i) & 1;
                            if (bit == 1)
                                synth.MidiChannels[i + channelOffset]
                                    .SetOctaveTuning(newOctaveTuning);
                        }

                        Debug.WriteLine(
                            $"MIDI Octave Scale {
                                (syx[3] == 0x08 ? "(1 byte)" : "(2 bytes)")
                            } tuning via Tuning: {
                                string.Join(" ", newOctaveTuning.ToArray())}");
                        break;
                    }

                    default: 
                        Engine.SystemExclusive.NotRecognized(syx, "MIDI Tuning Standard");
                        break;
                }
                break;

            default: 
                Engine.SystemExclusive.NotRecognized(syx, "Universal System Exclusive");
                break;
        }
    }

    [Conditional("DEBUG")]
    private static void Unsupported(
        string what, ReadOnlySpan<byte> syx, string reason = "")
    {
        Debug.WriteLine(
            $"Unsupported {what} message: {Util.ToHexString(syx)}. {reason}");
    }
}