using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class ControllerChange
{
    /// <summary>Handles MIDI controller changes for a channel.</summary>
    /// <remarks>
    /// This function processes MIDI controller changes, updating the channel's
    /// midiControllers table and handling special cases like bank select,
    /// data entry, and sustain pedal. It also computes modulators for all voices
    /// in the channel based on the controller change.
    /// to allow changes.
    /// </remarks>
    /// <param name="chan"></param>
    /// <param name="controller">The MIDI controller number (0-127).</param>
    /// <param name="value">The value of the controller (0-127).</param>
    /// <param name="sendEvent">If an event should be emitted.</param>
    /// <exception cref="InvalidOperationException">Invalid controller</exception>
    public static void Send(
        MidiChannel chan, Midi.CC controller, int value, bool sendEvent = true)
    {
        if ((int)controller is > 127 or < 0)
            throw SpessaException.Invalid($"Invalid MIDI Controller on channel {chan.Channel}: {(int)controller:X2}");
        
        // Lsb controller values: append them as the lower nibble of the 14-bit value
        // Excluding bank select as it's handled separately
        if (controller is 
            >= Midi.CC.ModulationWheelLSB and
            <= Midi.CC.EffectControl2LSB)
        {
            var actualCCNum = controller - 32;
            if (chan.LockedControllers[(int)actualCCNum])
                return;

            // Append the lower nibble to the main controller
            chan.MidiControllers[(int)actualCCNum] =
                (short)((chan.MidiControllers[(int)actualCCNum] & 0x3f_80) |
                        (value & 0x7f));

            chan.ComputeModulatorsAll(
                1, 
                (int)actualCCNum);
        }
        if (chan.LockedControllers[(int)controller]) return;

        // Apply the cc to the table (top 7 bits only, to not override LSB)
        // For consistency we also technically apply this to the LSB controllers directly,
        // But they are unused (except Parameter Numbers)
        chan.MidiControllers[(int)controller] = 
            (short)((value << 7)  | 
                    (chan.MidiControllers[(int)controller] & 0x7f));

        var synth = chan.SynthCore;
        
        // Interpret special CCs
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (controller)
        {
            // Channel mode messages
            case Midi.CC.OmniModeOff:
            case Midi.CC.OmniModeOn:
            case Midi.CC.AllNotesOff:
                chan.StopAllNotes();
                break;
            
            case Midi.CC.AllSoundOff:
                chan.StopAllNotes(true);
                break;
            
            case Midi.CC.PolyModeOn:
                chan.StopAllNotes(true);
                chan.Set((ChannelMidiParameter.Type.PolyMode, true));
                break;
            
            case Midi.CC.MonoModeOn:
                chan.StopAllNotes(true);
                chan.Set((ChannelMidiParameter.Type.PolyMode, false));
                break;

            // Special case: bank select
            case Midi.CC.BankSelect:
                chan.SetBankMSB(value);
                // Ensure that for XG, drum channels always are 127
                // Testcase
                // Dave-Rodgers-D-j-Vu-Anonymous-20200419154845-nonstop2k.com.mid
                if (chan.Channel % 16 == Synthesizer.DEFAULT_PERCUSSION &&
                    BankSelectHacks.IsSystemXG(chan.ChannelSystem)) 
                {
                    chan.SetBankMSB(127);
                }
                break;
            
            
            case Midi.CC.BankSelectLSB:
                chan.SetBankLSB(value);
                break;

            case Midi.CC.VariationDepth: 
                synth.DelayActive = true;
                break;
            
            case Midi.CC.RegisteredParameterLSB:
            case Midi.CC.RegisteredParameterMSB:
                // Clear and set state.
                // This is technically not a MIDI behavior,
                // But some MIDI files only send MSB data:
                // https://github.com/spessasus/spessasynth_core/pull/78#discussion_r3233413622
                chan.MidiControllers[(int)Midi.CC.DataEntryMSB] = 0;
                chan.LastParameterIsRegistered = true;
                break;
            
            case Midi.CC.NonRegisteredParameterMSB: 
                // Sf spec section 9.6.2
                chan.Sf2NRPNGeneratorLSB = 0;
                
                // Clear and set state.
                // This is technically not a MIDI behavior,
                // But some MIDI files only send MSB data:
                // https://github.com/spessasus/spessasynth_core/pull/78#discussion_r3233413622
                chan.MidiControllers[(int)Midi.CC.DataEntryMSB] = 0;
                chan.LastParameterIsRegistered = false;
                break;
            
            case Midi.CC.NonRegisteredParameterLSB: 
                if (chan.MidiControllers[(int)Midi.CC
                        .NonRegisteredParameterMSB] >> 7 ==
                    ExtendedParameters.NRPN.MSB.SF2) 
                {
                    // If a <100 value has already been sent, reset!
                    if (chan.Sf2NRPNGeneratorLSB % 100 != 0)
                        chan.Sf2NRPNGeneratorLSB = 0;

                    switch (value) 
                    {
                        case 100: 
                            chan.Sf2NRPNGeneratorLSB += 100;
                            break;

                        case 101: 
                            chan.Sf2NRPNGeneratorLSB += 1_000;
                            break;

                        case 102: 
                            chan.Sf2NRPNGeneratorLSB += 10_000;
                            break;

                        default: 
                            if (value < 100)
                                chan.Sf2NRPNGeneratorLSB += value;
                            break;
                    }
                }
                
                // Clear and set state.
                // This is technically not a MIDI behavior,
                // But some MIDI files only send MSB data:
                // https://github.com/spessasus/spessasynth_core/pull/78#discussion_r3233413622
                chan.MidiControllers[(int)Midi.CC.DataEntryMSB] = 0;
                chan.LastParameterIsRegistered = false;
                break;

            case Midi.CC.DataEntryMSB: 
            case Midi.CC.DataEntryLSB: 
                chan.DataEntry();
                break;

            case Midi.CC.ResetAllControllers: 
                chan.ResetRP15();
                break;
            
            case Midi.CC.SustainPedal: 
                if (value < 64) 
                {
                    var vc = 0;
                    if (chan.VoiceCount > 0)
                    {
                        var cTime = (float)chan.SynthCore.CurrentTime;
                        foreach (var v in chan.SynthCore.Voices) 
                        {
                            if (v.Channel == chan.Channel &&
                                v is { IsActive: true, IsHeld: true }) 
                            {
                                v.IsHeld = false;
                                v.ReleaseVoice(cTime);
                                if (++vc >= chan.VoiceCount) break; // We already checked all the voices
                            }
                        }
                    }
                }
                break;

            case Midi.CC.PortamentoControl:
                // Force portamento (MIDI 1.0 specification, page 16)
                // Even if portamento on/off (cc#65) is off
                chan.LastPortamentoNote = value;
                chan.PortamentoForce = true;
                break;
            
            // Default: just compute modulators
            default: 
                chan.ComputeModulatorsAll(1, (int)controller);
                break;
        }
        
        if (!sendEvent) return;
        
        synth.CallEvent(new Event.CbControllerChange(
            chan.Channel, controller, value));
    }
}