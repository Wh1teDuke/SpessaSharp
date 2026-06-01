using System.Collections.Frozen;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class Reset
{
    public const int CONTROLLER_TABLE_SIZE = 128;

    /// <summary> An array with the default MIDI controller values. Note that these are 14-bit, not 7-bit. </summary>
    public static readonly short[] DefaultMidiControllers =
        new short[CONTROLLER_TABLE_SIZE];

    public static readonly byte[] DefaultDrumReverb = new byte[128];

    /// <summary>
    /// Reset all controllers for channel. This will reset all controllers to their default values, except for the locked controllers.
    /// </summary>
    /// <param name="chan"></param>
    /// <param name="sendCCEvents"></param>
    public static void Channel(MidiChannel chan, bool sendCCEvents = true)
    {
        var defVals = DefaultMidiControllers.AsSpan();
        
        // Reset MIDI controllers
        for (var cc = 0; cc < CONTROLLER_TABLE_SIZE; cc++)
        {
            if (chan.LockedControllers[cc])
            {
                // Was not reset so restore the value
                chan.SynthCore.CallEvent(
                    new Event.CbControllerChange(
                        chan.Channel,
                        (Midi.CC)cc,
                        chan.MidiControllers[cc] >> 7));
                continue;
            }
            
            var resetValue = defVals[cc];

            if (chan.MidiControllers[cc] == resetValue || cc >= 127) continue;

            if (cc != (int)Midi.CC.PortamentoControl &&
                cc != (int)Midi.CC.DataEntryMSB &&
                cc != (int)Midi.CC.RegisteredParameterMSB &&
                cc != (int)Midi.CC.RegisteredParameterLSB &&
                cc != (int)Midi.CC.NonRegisteredParameterMSB &&
                cc != (int)Midi.CC.NonRegisteredParameterLSB) 
            {
                chan.ControllerChange(
                    (Midi.CC)cc,
                    resetValue >> 7,
                    sendCCEvents);
            }
        }
        
        // Reset MIDI parameters (locked will remain in place)
        chan.Set((ChannelMidiParameter.Type.Pressure, 0));
        chan.Set((ChannelMidiParameter.Type.PitchWheelRange, 2f));
        chan.Set((ChannelMidiParameter.Type.ModulationDepth, 50f));
        chan.Set((ChannelMidiParameter.Type.RxChannel, chan.Channel));
        chan.Set((ChannelMidiParameter.Type.EfxAssign, false));
        chan.Set((ChannelMidiParameter.Type.PolyMode, true));
        chan.Set((ChannelMidiParameter.Type.KeyShift, 0));
        chan.Set((ChannelMidiParameter.Type.FineTune, 0f));
        chan.Set(MidiChannel.Assign.FullMulti);
        chan.Set((ChannelMidiParameter.Type.RandomPan, false));
        chan.Set(new ChannelMidiParameter(
            ChannelMidiParameter.Type.CC1, (Midi.CC)0x10));
        chan.Set(new ChannelMidiParameter(
            ChannelMidiParameter.Type.CC2, (Midi.CC)0x11));
        chan.Set((
            ChannelMidiParameter.Type.DrumMap, 
            chan.Channel % 16 == Synthesizer.DEFAULT_PERCUSSION ? 1 : 0));
        chan.Set((ChannelMidiParameter.Type.VelocitySenseOffset, 64));
        chan.Set((ChannelMidiParameter.Type.VelocitySenseDepth, 64));
        // This one has a wrapper, for per-note pitch wheel
        chan.PitchWheel(8_192);
        
        // Reset various other things
        chan.OctaveTuning.AsSpan().Clear();
        
        // Portamento has a quirk:
        // For XG, control is set to 60
        // For others, it's set to nothing (no portamento on first note-on)
        chan.LastPortamentoNote = chan.ChannelSystem == Midi.System.XG ? 60 : -1;
        
        chan.ResetDrumParams();
        chan.ResetGeneratorOverrides();
        chan.ResetGeneratorOffsets();
        chan.DynamicModulators.ResetModulators();
        chan.Sf2NRPNGeneratorLSB = 0;
        chan.PlayingNotes.SetAll(false);
        
        // Reset Parameters (do not emit controller change)
        // We reset them here since in the loop, the data entries would come before params
        chan.LastParameterIsRegistered = true;
        chan.MidiControllers[(int)Midi.CC.NonRegisteredParameterLSB] = 
            DataEntry.DEFAULT_NRPN << 7;
        chan.MidiControllers[(int)Midi.CC.NonRegisteredParameterMSB] =
            DataEntry.DEFAULT_NRPN << 7;
        chan.MidiControllers[(int)Midi.CC.RegisteredParameterLSB] =
            DataEntry.DEFAULT_RPN << 7;
        chan.MidiControllers[(int)Midi.CC.RegisteredParameterMSB] = 
            DataEntry.DEFAULT_RPN << 7;
        chan.MidiControllers[(int)Midi.CC.DataEntryMSB] = 0;
        chan.MidiControllers[(int)Midi.CC.DataEntryLSB] = 0;
        
        // Reset program
        chan.SetBankMSB(BankSelectHacks.GetDefaultBank(chan.ChannelSystem));
        chan.SetBankLSB(0);
        chan.SetGSDrums(false);
        
        chan.SetDrums(chan.Channel % 16 == Synthesizer.DEFAULT_PERCUSSION);
        chan.ProgramChange(0);
    }

    public static readonly FrozenSet<Midi.CC> Rp15ResetCCNums = [
        Midi.CC.ModulationWheel,
        Midi.CC.Expression,
        Midi.CC.SustainPedal,
        Midi.CC.PortamentoOnOff,
        Midi.CC.SostenutoPedal,
        Midi.CC.SoftPedal,
        Midi.CC.RegisteredParameterMSB,
        Midi.CC.RegisteredParameterLSB,
    ];
    
    /// <summary>
    /// https://amei.or.jp/midistandardcommittee/Recommended_Practice/e/rp15.pdf<br/>
    /// Reset controllers according to RP-15 Recommended Practice.
    /// </summary>
    /// <param name="chan"></param>
    public static void RP15(MidiChannel chan) 
    {
        var defVals = DefaultMidiControllers.AsSpan();
        
        chan.PerNotePitch = false;
        chan.PitchWheel(8_192);

        foreach (var resetCC in Rp15ResetCCNums)
        {
            var resetValue = defVals[(int)resetCC];
            if (resetValue != chan.MidiControllers[(int)resetCC])
                chan.ControllerChange(resetCC, resetValue >> 7);
        }
    }

    static Reset()
    {
        // Values come from Falcosoft MIDI Player
        SetResetValue(Midi.CC.MainVolume, 100);
        SetResetValue(Midi.CC.Balance, 64);
        SetResetValue(Midi.CC.Expression, 127);
        SetResetValue(Midi.CC.Pan, 64);

        SetResetValue(Midi.CC.FilterResonance, 64);
        SetResetValue(Midi.CC.ReleaseTime, 64);
        SetResetValue(Midi.CC.AttackTime, 64);
        SetResetValue(Midi.CC.Brightness, 64);

        SetResetValue(Midi.CC.DecayTime, 64);
        SetResetValue(Midi.CC.VibratoRate, 64);
        SetResetValue(Midi.CC.VibratoDepth, 64);
        SetResetValue(Midi.CC.VibratoDelay, 64);
        SetResetValue(Midi.CC.GeneralPurposeController6, 64);
        SetResetValue(Midi.CC.GeneralPurposeController8, 64);

        SetResetValue(Midi.CC.RegisteredParameterLSB, DataEntry.DEFAULT_RPN);
        SetResetValue(Midi.CC.RegisteredParameterMSB, DataEntry.DEFAULT_RPN);
        SetResetValue(Midi.CC.NonRegisteredParameterLSB, DataEntry.DEFAULT_NRPN);
        SetResetValue(Midi.CC.NonRegisteredParameterMSB, DataEntry.DEFAULT_NRPN);
            
        //
        DefaultDrumReverb.AsSpan().Fill(127);
        // Kicks have no reverb
        DefaultDrumReverb[35] = 0;
        DefaultDrumReverb[36] = 0;

        return;
        
        static void SetResetValue(Midi.CC c, int v) =>
            DefaultMidiControllers[(int)c] = (short)(v << 7);
    }
}