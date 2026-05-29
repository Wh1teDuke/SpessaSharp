using System.Diagnostics;
using SpessaSharp.MIDI;
using SpessaSharp.MIDI.Utils;
using SpessaSharp.SoundBank;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.Synthesizer.Engine.Channel;

internal static class DataEntry
{
    /// <summary>RPN NULL per MIDI spec.</summary>
    public const int DEFAULT_RPN = 0x7f;
    /// <summary>No NRPN is bound to 0 0, while 0x7f MSB is AWE32!</summary>
    public const int DEFAULT_NRPN = 0;
    
    [Conditional("DEBUG")]
    private static void CoolInfo(
        int chanNum, string what, string value, string type)
    {
        if (type.Length > 0) type = " " + type;
        Debug.WriteLine(
            $"{what} for {chanNum} is now set to {value} {type}.");
    }
    
    [Conditional("DEBUG")]
    private static void CoolInfo(
        int chanNum, string what, int value, string type) =>
        CoolInfo(chanNum, what, value.ToString(), type);

    /// <summary>Executes a data entry change for the current channel.</summary>
    /// <param name="chan"></param>
    public static void Execute(MidiChannel chan)
    {
        // Stored in cc tabled as 14-bit
        var dataValue = chan.MidiControllers[
            (int)Midi.CC.DataEntryMSB];

        /*
        A note on this vibrato.
        This is a completely custom vibrato, with its own oscillator and parameters.
        It is disabled by default,
        only being enabled when one of the NPRN messages changing it is received
        and stays on until the next system-reset.
        It was implemented very early in SpessaSynth's development,
        because I wanted support for Touhou MIDIs :-)
         */
        
        // RPN Handling
        if (chan.LastParameterIsRegistered)
        {
            var rpnValue  =
                (ushort)chan.MidiControllers[(int)Midi.CC.RegisteredParameterMSB] |
                (chan.MidiControllers[(int)Midi.CC.RegisteredParameterLSB] >> 7);

            // Pitch wheel range
            switch (rpnValue)
            {
                case ExtendedParameters.RPN.PitchWheelRange:
                {
                    // Pitch wheel range may be a floating point number!
                    // Therefore, something like "64" won't work,
                    // So we divide it by 128 which is essentially the same here
                    // But it allows for fractional pitch wheel range!
                    var range = dataValue / 128f;
                    chan.Set((ChannelMidiParameter.Type.PitchWheelRange, range));
                    SpessaLog.CoolInfo(
                        $"Pitch Wheel Range for {chan.Channel}",
                        range,
                        "semitones");
                    break;
                }
                // Coarse tuning
                case ExtendedParameters.RPN.CoarseTuning:
                {
                    // Semitones, discard LSB
                    var semitones = (dataValue >> 7) - 64;
                    chan.Set((ChannelMidiParameter.Type.KeyShift, semitones));
                    SpessaLog.CoolInfo($"Key shift for {chan.Channel}", semitones);
                    break;
                }
                // Fine-tuning
                case ExtendedParameters.RPN.FineTuning:
                {
                    var finalTuning = dataValue - 8_192;
                    // Resolution is 100/8192 cents
                    var cents = finalTuning / 81.92f;
                    chan.Set((ChannelMidiParameter.Type.FineTune, cents));
                    SpessaLog.CoolInfo(
                        $"Fine tuning for {chan.Channel}",
                        Util.Round(cents),
                        "cents");
                    break;
                }
                // Modulation depth
                case ExtendedParameters.RPN.ModulationDepth:
                {
                    // Cents, so data / 128 * 100 is data / 1.28
                    var cents = dataValue / 1.28f;
                    chan.Set((ChannelMidiParameter.Type.ModulationDepth, cents));
                    SpessaLog.CoolInfo(
                        $"Modulation depth for {chan.Channel}",
                        Util.Round(cents),
                        "cents");
                    break;
                }
                case ExtendedParameters.RPN.ResetParameters:
                {
                    // Ignore
                    break;
                }
                default:
                {
                    Debug.WriteLine($"[WARN] Unrecognized RPN for {chan.Channel}: (0x{rpnValue:X}) data value: {dataValue}");
                    break;
                }
            }
            return;
        }
        
        // NRPN Handling
        var paramCoarse = chan.MidiControllers[
            (int)Midi.CC.NonRegisteredParameterMSB] >> 7;
        var paramFine = chan.MidiControllers[
            (int)Midi.CC.NonRegisteredParameterLSB] >> 7;
        var dataCoarse = dataValue >> 7;

        // Skip drums early
        if (chan.SynthCore.SystemParameters.DrumLock &&
            paramCoarse >= ExtendedParameters.NRPN.MSB.DrumPitch &&
            paramCoarse <= ExtendedParameters.NRPN.MSB.DrumDelay)
            return;

        switch (paramCoarse)
        {
            // Part parameters
            case ExtendedParameters.NRPN.MSB.PartParameter:
            {
                var paramLock =
                    chan.SystemParameters.NprnParamLock ??
                    chan.SynthCore.SystemParameters.NprnParamLock;

                switch (paramFine)
                {
                    // Vibrato rate
                    case ExtendedParameters.NRPN.LSB.VibratoRate:
                    {
                        chan.ControllerChange(Midi.CC.VibratoRate, dataCoarse);
                        break;
                    }
                    // Vibrato depth
                    case ExtendedParameters.NRPN.LSB.VibratoDepth:
                    {
                        chan.ControllerChange(Midi.CC.VibratoDepth, dataCoarse);
                        break;
                    }
                    // Vibrato delay
                    case ExtendedParameters.NRPN.LSB.VibratoDelay:
                    {
                        chan.ControllerChange(Midi.CC.VibratoDelay, dataCoarse);
                        break;
                    }
                    // Filter cutoff
                    case ExtendedParameters.NRPN.LSB.TVFCutoffFrequency:
                    {
                        if (paramLock) return;
                        // Affect the "brightness" controller as we have a default modulator that controls it
                        chan.ControllerChange(
                            Midi.CC.Brightness, dataCoarse);
                        CoolInfo(
                            chan.Channel,
                            "Filter cutoff",
                            dataCoarse,
                            "");
                        break;
                    }
                    case ExtendedParameters.NRPN.LSB.TVFResonance:
                    {
                        if (paramLock) return;
                        // Affect the "resonance" controller as we have a default modulator that controls it
                        chan.ControllerChange(
                            Midi.CC.FilterResonance, dataCoarse);
                        CoolInfo(
                            chan.Channel,
                            "Filter resonance",
                            dataCoarse,
                            "");
                        break;
                    }
                    // Attack time
                    case ExtendedParameters.NRPN.LSB.EnvelopeAttackTime:
                    {
                        if (paramLock) return;
                        // Affect the "attack time" controller as we have a default modulator that controls it
                        chan.ControllerChange(
                            Midi.CC.AttackTime, dataCoarse);
                        CoolInfo(
                            chan.Channel,
                            "EG attack time",
                            dataCoarse,
                            "");
                        break;
                    }
                    // Decay time
                    case ExtendedParameters.NRPN.LSB.EnvelopeDecayTime:
                    {
                        if (paramLock) return;
                        // Affect the "decay time" controller as we have a default modulator that controls it
                        chan.ControllerChange(
                            Midi.CC.DecayTime, dataCoarse);
                        CoolInfo(
                            chan.Channel,
                            "EG decay time",
                            dataCoarse,
                            "");
                        break;
                    }
                    // Release time
                    case ExtendedParameters.NRPN.LSB.EnvelopeReleaseTime:
                    {
                        if (paramLock) return;
                        // Affect the "release time" controller as we have a default modulator that controls it
                        chan.ControllerChange(
                            Midi.CC.ReleaseTime,
                            dataCoarse
                        );
                        CoolInfo(
                            chan.Channel,
                            "EG release time",
                            dataCoarse,
                            "");
                        break;
                    }
                    default:
                    {
                        Debug.WriteLine($"[WARN] Unrecognized NRPN for {chan.Channel
                        }: (0x{paramCoarse:X} 0x{
                            paramFine:X}) data value: {dataCoarse}");
                        break;
                    }
                }

                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumPitch:
            {
                /*
                 * https://github.com/spessasus/spessasynth_core/pull/58#issuecomment-3893343073
                 * it's actually 50 cents! (not for XG though)
                 * also if SC-55 preset is explicitly requested (MAP1 - LSB 1), it's 100 cents as well!
                 */
                var pitch =
                    chan.ChannelSystem == Midi.System.XG ||
                    chan.Patch.BankLSB == 1
                        ? (dataCoarse - 64) * 100
                        : (dataCoarse - 64) * 50;
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { Pitch = pitch, };
                CoolInfo(
                    chan.Channel,
                    $"Drum ${paramFine} pitch",
                    pitch,
                    "cents");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumPitchFine:
            {
                var pitch = dataCoarse - 64;
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { Pitch = param.Pitch + pitch, };

                CoolInfo(
                    chan.Channel,
                    $"Drum ${paramFine} pitch fine",
                    chan.DrumParams[paramFine].Pitch,
                    "cents");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumLevel:
            {
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { Gain = dataCoarse / 120f, };
                SpessaLog.CoolInfo(
                    $"Drum {paramFine} level for {chan.Channel}",
                    dataCoarse,
                    "");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumPan:
            {
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { Pan = dataCoarse, };

                SpessaLog.CoolInfo(
                    $"Drum {paramFine} pan for {chan.Channel}",
                    dataCoarse, "");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumReverb:
            {
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { ReverbGain = dataCoarse / 127f, };

                CoolInfo(
                    chan.Channel,
                    $"Drum ${paramFine} reverb level",
                    dataCoarse,
                    "");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumChorus:
            {
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { ChorusGain = dataCoarse / 127f, };

                CoolInfo(
                    chan.Channel,
                    $"Drum ${paramFine} chorus level",
                    dataCoarse,
                    "");
                break;
            }
            case ExtendedParameters.NRPN.MSB.DrumDelay:
            {
                ref var param = ref chan.DrumParams[paramFine];
                param = param with { DelayGain = dataCoarse / 127f, };

                CoolInfo(
                    chan.Channel,
                    $"Drum ${paramFine} delay level",
                    dataValue,
                    "");
                break;
            }
            case ExtendedParameters.NRPN.MSB.awe32:
            {
                Awe32NRPN.Handle(chan, paramFine, dataValue);
                break;
            }
            // SF2 NRPN
            case ExtendedParameters.NRPN.MSB.SF2:
            {
                if (paramFine > 100)
                {
                    // Sf spec:
                    // Note that NRPN Select LSB greater than 100 are for setup only, and should not be used on their own to select a
                    // Generator parameter.
                }
                else
                {
                    var gen = (Generator.Type)chan.Sf2NRPNGeneratorLSB;
                    var offset = (short)(dataValue - 8_192);
                    chan.SetGeneratorOffset(gen, offset);
                }
                break;
            }
        }
    }
}