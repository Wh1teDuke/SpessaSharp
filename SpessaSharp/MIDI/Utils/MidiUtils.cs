using System.Diagnostics;
using System.Runtime.InteropServices;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary> A general purpose class for handling MIDI messages. </summary>
public static class MidiUtils
{
    public readonly struct AnalyzedMessage
    {
        public enum Type : byte
        {
            Other, XgReset, GmOn, GmOff, Gm2On, GsReset,
            ReverbParam, ChorusParam, DelayParam, VariationParam,
            InsertionParam,
            DrumsOn, DrumSetup, ProgramChange, ControllerChange,
            MasterKeyShift, KeyShift,
            MasterFineTune, FineTune,
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InternalData
        {
            [FieldOffset(0)] public (int Channel, bool IsDrum) _drumsOn;
            [FieldOffset(0)] public (int Channel, int Value) _programChange;
            [FieldOffset(0)] public (Midi.CC Controller, int Value, int Channel) _controllerChange;
            [FieldOffset(0)] public int _masterKeyShift;//Value
            [FieldOffset(0)] public (int Value, int Channel) _keyShift;
            // Value in cents
            [FieldOffset(0)] public float _masterFineTune;//Value
            // Value in cents
            [FieldOffset(0)] public (float Value, int Channel) _fineTune;
        }

        public Type MType { get; private init; }
        private InternalData Data { get; init; }

        public (int Channel, bool IsDrum)? AsDrumsOn =>
            MType == Type.DrumsOn ? Data._drumsOn : null;
        public (int Channel, int Value)? AsProgramChange =>
            MType == Type.ProgramChange ? Data._programChange : null;
        public (Midi.CC Controller, int Value, int Channel)? AsControllerChange =>
            MType == Type.ControllerChange ? Data._controllerChange : null;
        public int? AsMasterKeyShift =>
            MType == Type.MasterKeyShift ? Data._masterKeyShift : null;
        public (int Value, int Channel)? AsKeyShift =>
            MType == Type.KeyShift ? Data._keyShift : null;
        public float? AsMasterFineTune =>
            MType == Type.MasterFineTune ? Data._masterFineTune : null;
        public (float Value, int Channel)? AsFineTune =>
            MType == Type.FineTune ? Data._fineTune : null;

        public static AnalyzedMessage Of(Type type)
        {
            ReadOnlySpan<Type> notAllowed = [
                Type.DrumsOn, Type.ProgramChange, Type.ControllerChange,
                Type.MasterKeyShift, Type.KeyShift, Type.MasterFineTune,
                Type.FineTune];
            return notAllowed.Contains(type) 
                ? throw new ArgumentException("Invalid argument: " + type) 
                : new AnalyzedMessage { MType = type };
        }

        public static AnalyzedMessage OfDrumsOn(
            int channel, bool isDrum) =>
            new()
            {
                MType = Type.DrumsOn, 
                Data = new InternalData { _drumsOn = (channel, isDrum) },
            };
        
        public static AnalyzedMessage OfProgramChange(
            int channel, int value) =>
            new()
            {
                MType = Type.ProgramChange, 
                Data = new InternalData { _programChange = (channel, value) },
            };
        
        public static AnalyzedMessage OfControllerChange(
            Midi.CC controller, int value, int channel) =>
            new()
            {
                MType = Type.ControllerChange, 
                Data = new InternalData
                    { _controllerChange = (controller, value, channel) },
            };

        public static AnalyzedMessage OfMasterKeyShift(int value) =>
            new()
            {
                MType = Type.MasterKeyShift,
                Data = new InternalData { _masterKeyShift = value, },
            };
        
        public static AnalyzedMessage OfKeyShift(int value, int channel) =>
            new()
            {
                MType = Type.KeyShift,
                Data = new InternalData { _keyShift = (value, channel), },
            };
        
        public static AnalyzedMessage OfMasterFineTune(float value) =>
            new()
            {
                MType = Type.FineTune,
                Data = new InternalData { _masterFineTune = value, },
            };
        
        public static AnalyzedMessage OfFineTune(float value, int channel) =>
            new()
            {
                MType = Type.FineTune,
                Data = new InternalData { _fineTune = (value, channel), },
            };

        public static implicit operator AnalyzedMessage(
            Type type) => Of(type);
    }

    /// <summary>
    /// Analyzes a MIDI System Exclusive message and returns an identification and data for it.
    /// </summary>
    /// <param name="e">The message to analyze</param>
    /// <returns></returns>
    public static AnalyzedMessage AnalyzeSysEx(MidiMessage e) =>
        AnalyzeSysEx(e.Data);

    /// <summary>
    /// Analyzes a MIDI System Exclusive message and returns an identification and data for it.
    /// </summary>
    /// <param name="syx">The System Exclusive message, WITHOUT the first 0xF0 System Exclusive byte!</param>
    /// <returns></returns>
    public static AnalyzedMessage AnalyzeSysEx(ReadOnlySpan<byte> syx)
    {
        // At least Manufacturer ID, Device ID and XG/GS model ID
        if (syx.Length < 3) return 
            AnalyzedMessage.Type.Other;

        return syx[0] switch
        {
            // Non realtime GM
            // Realtime GM
            0x7e or 0x7f => AnalyzeGM(syx),
            // Roland
            0x41 => AnalyzeGS(syx),
            // Yamaha
            0x43 => AnalyzeXG(syx),
            _ => AnalyzedMessage.Type.Other
        };
    }
    
    /// <summary>
    /// Analyzes a MIDI Registered Parameter Number and returns an identification and data for it.
    /// </summary>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="rpn">The 14-bit RPN number.</param>
    /// <param name="value">The 14-bit value for that number.</param>
    /// <returns></returns>
    public static AnalyzedMessage AnalyzeRPN(
        int channel, int rpn, int value) =>
        rpn switch
        {
            _ when rpn == ExtendedParameters.RPN.FineTuning => 
                AnalyzedMessage.OfFineTune((value - 8_192) / 81.92f, value),
            _ when rpn == ExtendedParameters.RPN.CoarseTuning =>
                AnalyzedMessage.OfKeyShift((value >> 7) - 64, channel),
            _ => AnalyzedMessage.Type.Other
        };
    
    /// <summary>
    /// Analyzes a MIDI Non-Registered Parameter Number and returns an identification and data for it.
    /// </summary>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="nrpn">The 14-bit NRPN number.</param>
    /// <param name="value">The 14-bit value for that number.</param>
    /// <returns></returns>
    public static AnalyzedMessage AnalyzeNRPN(int channel, int nrpn, int value)
    {
        var msb = nrpn >> 7;
        var lsb = nrpn & 0x7f;
        switch (msb) 
        {
            default: 
                return AnalyzedMessage.Type.Other;

            case ExtendedParameters.NRPN.MSB.PartParameter: 
            {
                switch (lsb) 
                {
                    default: 
                        return AnalyzedMessage.Type.Other;
                        
                    case ExtendedParameters.NRPN.LSB.VibratoRate:
                        return OfCC(Midi.CC.VibratoRate);
                    
                    case ExtendedParameters.NRPN.LSB.VibratoDepth:
                        return OfCC(Midi.CC.VibratoDepth);
                    
                    case ExtendedParameters.NRPN.LSB.VibratoDelay:
                        return OfCC(Midi.CC.VibratoDelay);

                    case ExtendedParameters.NRPN.LSB.TVFCutoffFrequency: 
                        return OfCC(Midi.CC.Brightness);

                    case ExtendedParameters.NRPN.LSB.TVFResonance: 
                        return OfCC(Midi.CC.FilterResonance);

                    case ExtendedParameters.NRPN.LSB.EnvelopeAttackTime: 
                        return OfCC(Midi.CC.AttackTime);

                    case ExtendedParameters.NRPN.LSB.EnvelopeDecayTime: 
                        return OfCC(Midi.CC.DecayTime);

                    case ExtendedParameters.NRPN.LSB.EnvelopeReleaseTime: 
                        return OfCC(Midi.CC.ReleaseTime);
                    
                    AnalyzedMessage OfCC(Midi.CC cc) => 
                        AnalyzedMessage.OfControllerChange(cc, value >> 7, channel);
                }
            }

            case ExtendedParameters.NRPN.MSB.DrumPitch:
            case ExtendedParameters.NRPN.MSB.DrumPitchFine:
            case ExtendedParameters.NRPN.MSB.DrumLevel:
            case ExtendedParameters.NRPN.MSB.DrumPan:
            case ExtendedParameters.NRPN.MSB.DrumReverb:
            case ExtendedParameters.NRPN.MSB.DrumChorus:
            case ExtendedParameters.NRPN.MSB.DrumDelay:
                return AnalyzedMessage.Type.DrumSetup;
        }
    }


    /// <summary>
    /// GS/XG "part number" to channel number.
    /// </summary>
    /// <param name="part"></param>
    public static int ToChannel(int part) =>
        ((ReadOnlySpan<int>)[
            9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15])[part % 16];

    /// <summary>
    /// Channel number to GS/XG "part number"
    /// </summary>
    /// <param name="chan"></param>
    public static int FromChannel(int chan) =>
        ((ReadOnlySpan<int>)[
            1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 10, 11, 12, 13, 14, 15])[chan % 16];

    public const int GsDataMinLen = 9;

    public static byte[] GsData(int a1, int a2, int a3, byte data)
    {
        var dataArray = new byte[GsDataMinLen + 1];
        GsData(a1, a2, a3, [data], dataArray);
        return dataArray;
    }
    
    public static byte[] GsData(int a1, int a2, int a3, byte data1, byte data2)
    {
        var dataArray = new byte[GsDataMinLen + 2];
        GsData(a1, a2, a3, [data1, data2], dataArray);
        return dataArray;
    }

    /// <summary>Gets raw GS System Exclusive message, without the 0xF0 status byte.</summary>
    /// <param name="a1">Address 1</param>
    /// <param name="a2">Address 2</param>
    /// <param name="a3">Address 3</param>
    /// <param name="data">Data, can be multiple bytes.</param>
    /// <param name="result">The final output, whose len must be <b>GsDataMinLen</b> + <b>data.Length</b></param>
    /// <returns>The same result provided for convenience</returns>
    /// <exception cref="Exception"></exception>
    public static ReadOnlySpan<byte> GsData(
        int a1, int a2, int a3, ReadOnlySpan<byte> data, Span<byte> result)
    {
        if (result.Length < GsDataMinLen + data.Length)
            throw new ArgumentException(
                $"Expected result of at least {GsDataMinLen + data.Length
                } length, got {result.Length}");
        
        // Calculate checksum
        // SC 8850 manual, page 245
        var sum = a1 + a2 + a3 + Util.Sum(data, d => d);
        var checksum = (128 - (sum % 128)) & 0x7f;

        result[0] = 0x41; // Roland
        result[1] = 0x10; // Device ID (defaults to 16 on roland)
        result[2] = 0x42; // GS
        result[3] = 0x12; // Command ID (DT1)
        result[4] = (byte)a1;
        result[5] = (byte)a2;
        result[6] = (byte)a3;
        data.CopyTo(result[7..]);
        result[7 + data.Length] = (byte)checksum;
        result[8 + data.Length] = 0xf7; // End of exclusive

        return result[.. (8 + data.Length)];
    }
    
    /// <summary>Sends a GS System Exclusive address</summary>
    /// <param name="ticks"></param>
    /// <param name="a1">Address 1</param>
    /// <param name="a2">Address 2</param>
    /// <param name="a3">Address 3</param>
    /// <param name="data">Data, can be multiple bytes</param>
    /// <returns></returns>
    public static MidiMessage GsMessage(
        int ticks, int a1, int a2, int a3, ReadOnlySpan<byte> data) 
    {
        var dataArray = new byte[GsDataMinLen + data.Length];
        GsData(a1, a2, a3, data, dataArray);
        
        return new MidiMessage(
            ticks, MidiMessage.Type.SystemExclusive, dataArray);
    }

    /// <summary>
    /// Gets a GS reset message System Exclusive MIDI message.
    /// </summary>
    /// <param name="ticks">The tick time of the message.</param>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="drumMap">The drum map to use. 0 turns the channel into a melodic channel,
    /// while other values turn it into a drum channel.</param>
    /// <returns></returns>
    public static MidiMessage GsDrumChange(int ticks, int channel, int drumMap)
    {
        Debug.Assert(drumMap is >= 0 and <= 2);
        var chanAddress = 0x10 | FromChannel(channel);
        return GsMessage(ticks, 40, chanAddress, 0x15, [(byte)drumMap]);
    }
    
    /// <summary>
    /// Gets a GS reset message System Exclusive
    /// </summary>
    /// <param name="ticks"></param>
    /// <returns></returns>
    public static MidiMessage GsReset(int ticks) =>
        GsMessage(
            ticks,
            0x40,       // System parameter - Address
            0x00,       // Global mode parameter -  Address
            0x7f,       // MODE SET - Address
            [0x00]  // 00 = GS Reset - Data
        );

    private static AnalyzedMessage AnalyzeGM(ReadOnlySpan<byte> syx)
    {
        if (syx.Length < 4) 
            return AnalyzedMessage.Type.Other;

        if (syx[2] == 0x04) // Device control
        {
            switch (syx[3])
            {
                default:
                    return AnalyzedMessage.Type.Other;

                case 0x03:
                {
                    // Master Fine-Tuning
                    var tuningValue = ((syx[5] << 7) | syx[6]) - 8_192;
                    var cents = tuningValue / 81.92f; // [-100;+99] cents range
                    return AnalyzedMessage.OfMasterFineTune(cents);
                }
                
                case 0x04:
                    // Master Coarse Tuning
                    return AnalyzedMessage.OfMasterKeyShift(syx[5] - 64);

                case 0x05:
                {
                    // Global Parameter control
                    if (
                        syx[4] != 0x01 || // Slot Path Length
                        syx[5] != 0x01 || // Parameter ID Width
                        syx[6] != 0x01 || // Value Width
                        syx[7] != 0x01 // Slot Path MSB
                    ) return AnalyzedMessage.Type.Other;

                    // Slot Path LSB
                    switch (syx[8]) 
                    {
                        default: return AnalyzedMessage.Type.Other;

                        case 0x01:
                        {
                            // Reverb
                            // Parameter
                            return syx[9] switch
                            {
                                0x01 or 0x02 =>
                                    AnalyzedMessage.Type.ReverbParam,
                                _ => AnalyzedMessage.Type.Other
                            };
                        }

                        case 0x02:
                        {
                            // Chorus
                            // Parameter
                            return syx[9] switch
                            {
                                0x01 or 0x02 or 0x03 or 0x04 => 
                                    AnalyzedMessage.Type.ChorusParam,
                                _ => AnalyzedMessage.Type.Other
                            };
                        }
                    }
                }
            }
        }
        
        if (syx[2] != 0x09)
            return AnalyzedMessage.Type.Other;

        return syx[3] switch
        {
            0x01 => AnalyzedMessage.Type.GmOn,
            0x02 => AnalyzedMessage.Type.GmOff,
            0x03 => AnalyzedMessage.Type.Gm2On,
            _ => AnalyzedMessage.Type.Other
        };
    }
    
    private static AnalyzedMessage AnalyzeXG(ReadOnlySpan<byte> syx)
    {
        // Ensure XG
        if (syx[2] != 0x4c || syx.Length < 7)
            return AnalyzedMessage.Type.Other;

        var a1 = syx[3]; // Address 1
        var a2 = syx[4]; // Address 2
        var a3 = syx[5]; // Address 3
        var data = syx[6];

        if (a1 == 0x00 && a2 == 0x00)
        {
            // XG SYSTEM
            return a3 switch
            {
                0x00 =>
                    // MASTER TUNE
                    GetMasterTune(syx),
                0x06 =>
                    // TRANSPOSE
                    AnalyzedMessage.OfMasterKeyShift(data - 64),
                // XG SYSTEM ON
                0x7e or
                // ALL PARAMETER RESET
                0x7f => AnalyzedMessage.Type.XgReset,
                _ => AnalyzedMessage.Type.Other,
            };
            
            AnalyzedMessage GetMasterTune(ReadOnlySpan<byte> syx)
            {
                var tune =
                ((syx[6] & 15) << 12) |
                    ((syx[7] & 15) << 8) |
                    ((syx[8] & 15) << 4) |
                    (syx[9] & 15);
                var cents = (tune - 1_024) / 10f;
                return AnalyzedMessage.OfMasterFineTune(cents);
            }
        }

        // XG EFFECT 1
        if (a1 == 0x02 && a2 == 0x01)
            return a3 switch
            {
                <= 0x15 => AnalyzedMessage.Type.ReverbParam,
                <= 0x35 => AnalyzedMessage.Type.ChorusParam,
                _ => AnalyzedMessage.Type.VariationParam
            };
        
        // XG EFFECT 2
        if (a1 == 0x03 && a2 == 0x00)
            return AnalyzedMessage.Type.VariationParam;

        // XG MULTI PART
        if (a1 == 0x08 /* A2 is the channel number*/) 
        {
            var channel = a2;
            // Avoid invalid channels
            if (channel >= 16)
                return AnalyzedMessage.Type.Other;

            return a3 switch
            {
                0x01 =>
                    // Bank Select MSB
                    OfControllerChange(Midi.CC.BankSelect),
                0x02 =>
                    // Bank Select LSB
                    OfControllerChange(Midi.CC.BankSelectLSB),
                0x03 =>
                    // Program change
                    AnalyzedMessage.OfProgramChange(channel, data),
                0x05 =>
                    // Poly/mono
                    AnalyzedMessage.OfControllerChange(
                        data == 1 ? Midi.CC.PolyModeOn : Midi.CC.MonoModeOn, 0, channel),
                0x07 =>
                    // Part mode
                    AnalyzedMessage.OfDrumsOn(channel, data > 0),
                0x08 =>
                    // Note shift
                    AnalyzedMessage.OfKeyShift(data - 64, channel),
                0x0b =>
                    // Volume
                    OfControllerChange(Midi.CC.MainVolume),
                0x0e =>
                    // Pan
                    OfControllerChange(Midi.CC.Pan),
                0x12 =>
                    // Chorus
                    OfControllerChange(Midi.CC.ChorusDepth),
                0x13 =>
                    // Reverb
                    OfControllerChange(Midi.CC.ReverbDepth),
                0x15 =>
                    // Vibrato rate
                    OfControllerChange(Midi.CC.VibratoRate),
                0x16 =>
                    // Vibrato depth
                    OfControllerChange(Midi.CC.VibratoDepth),
                0x17 =>
                    // Vibrato delay
                    OfControllerChange(Midi.CC.VibratoDelay),
                0x18 =>
                    // Filter cutoff
                    OfControllerChange(Midi.CC.Brightness),
                0x19 =>
                    // Filter resonance
                    OfControllerChange(Midi.CC.FilterResonance),
                0x1a =>
                    // Attack time
                    OfControllerChange(Midi.CC.AttackTime),
                0x1b =>
                    // Decay time
                    OfControllerChange(Midi.CC.DecayTime),
                0x0c =>
                    // Release time
                    OfControllerChange(Midi.CC.ReleaseTime),
                _ => AnalyzedMessage.Type.Other
            };
            
            AnalyzedMessage OfControllerChange(Midi.CC cc) =>
                AnalyzedMessage.OfControllerChange(cc, data, channel);
        }

        // Drum part setup
        if (a1 >> 4 == 3)
            return AnalyzedMessage.Type.DrumSetup;

        return AnalyzedMessage.Type.Other;
    }
    
    private static AnalyzedMessage AnalyzeGS(ReadOnlySpan<byte> syx)
    {
        if (syx.Length < 10 ||
            // Model ID (GS)
            syx[2] != 0x42 ||
            // 0x12: DT1 (Device Transmit)
            syx[3] != 0x12)
            return AnalyzedMessage.Type.Other; // Something else

        // Address
        var a1 = syx[4];
        var a2 = syx[5];
        var a3 = syx[6];
        var data = syx[7];

        // GS reset check
        if (
            // Address 1 is 0x00 for SC-88 SYSTEM MODE SET and 0x40 for SC-55 MODE SET
            a1 is 0x00 or 0x40 &&
            a2 == 0x00) // System Parameter
        {
            switch (a3)
            {
                // MODE SET
                case 0x7f:
                {
                    if (data == 0x00)
                        // GS Reset/Mode-1
                        return AnalyzedMessage.Type.GsReset;
                    if (data == 0x7f)
                        // GS Off, default to gm
                        return AnalyzedMessage.Type.GmOn;
                    return AnalyzedMessage.Type.Other;
                }
                
                // Master Tune
                case 0x00:
                {
                    var tune =
                    (data << 12) | (syx[8] << 8) | (syx[9] << 4) | syx[10];
                    var cents = (tune - 1_024) / 10f;
                    return AnalyzedMessage.OfMasterFineTune(cents);
                }
            }
        }

        if (a1 == 0x41) return AnalyzedMessage.Type.DrumSetup;
        if (a1 != 0x40) return AnalyzedMessage.Type.Other;
        
        if (a2 == 0x00 && a3 == 0x05)
            return AnalyzedMessage.OfMasterKeyShift(data - 64);

        // Effects
        if (a2 == 0x01)
        {
            if (a3 is >= 0x30 and <= 0x37) return AnalyzedMessage.Type.ReverbParam;
            if (a3 is >= 0x38 and <= 0x40) return AnalyzedMessage.Type.ChorusParam;
            if (a3 is >= 0x50 and <= 0x5a) return AnalyzedMessage.Type.DelayParam;
        }

        // EFX Parameter
        if (a2 == 0x03 && a3 is >= 0x00 and <= 0x7f)
            return AnalyzedMessage.Type.InsertionParam;

        // Patch parameter
        if (a2 >> 4 == 1)
        {
            var channel = ToChannel(a2 & 0x0f);
            return a3 switch
            {
                0x00 =>
                    // Tone number
                    AnalyzedMessage.OfProgramChange(channel, data),
                0x13 =>
                    // Mono/poly
                    AnalyzedMessage.OfControllerChange(
                        data == 1 ? Midi.CC.PolyModeOn : Midi.CC.MonoModeOn, 0, channel),
                0x15 => AnalyzedMessage.OfDrumsOn(channel, data > 0),
                0x16 => AnalyzedMessage.OfKeyShift(data - 64, channel),
                0x19 =>
                    // Part level (cc#7)
                    OfControllerChange(Midi.CC.MainVolume),
                0x1c =>
                    // Pan position
                    OfControllerChange(Midi.CC.Pan),
                0x21 =>
                    // Chorus send
                    OfControllerChange(Midi.CC.ChorusDepth),
                0x22 =>
                    // Reverb send
                    OfControllerChange(Midi.CC.ReverbDepth),
                0x2a =>
                    // Fine tune
                    AnalyzedMessage.OfFineTune(
                        // 0-16384
                        (((data << 7) | syx[8]) - 8_192) / 81.92f, channel),
                    
                0x2c =>
                    // Delay send
                    OfControllerChange(Midi.CC.VariationDepth),
                0x30 =>
                    // Vibrato rate
                    OfControllerChange(Midi.CC.VibratoRate),
                0x31 =>
                    // Vibrato depth
                    OfControllerChange(Midi.CC.VibratoDepth),
                0x32 =>
                    // Filter cutoff
                    OfControllerChange(Midi.CC.Brightness),
                0x33 =>
                    // Filter resonance
                    OfControllerChange(Midi.CC.FilterResonance),
                0x34 =>
                    // Attack time
                    OfControllerChange(Midi.CC.AttackTime),
                0x35 =>
                    // Decay time
                    OfControllerChange(Midi.CC.DecayTime),
                0x36 =>
                    // Release time
                    OfControllerChange(Midi.CC.ReleaseTime),
                0x37 =>
                    // Vibrato delay
                    OfControllerChange(Midi.CC.VibratoDelay),
                _ => AnalyzedMessage.Type.Other
            };

            AnalyzedMessage OfControllerChange(Midi.CC cc) =>
                AnalyzedMessage.OfControllerChange(cc, data, channel);
        }

        // Patch Parameter Tone Map
        if (a2 >> 4 == 4)
        {
            var channel = ToChannel(a2 & 0x0f);
            return a3 switch
            {
                0x00 or 0x01 =>
                    // Tone map number (cc#32)
                    AnalyzedMessage.OfControllerChange(
                        Midi.CC.BankSelectLSB, data, channel),
                0x22 => AnalyzedMessage.Type.InsertionParam,
                _ => AnalyzedMessage.Type.Other
            };
        }

        return AnalyzedMessage.Type.Other;
    }
}