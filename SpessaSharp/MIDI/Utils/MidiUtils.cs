using System.Runtime.InteropServices;
using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary> A general purpose class for handling MIDI messages. </summary>
public static class MidiUtils
{
    public readonly struct AnalyzedParameter
    {
        public enum Type : byte
        {
            Other, ControllerChange, ChannelMidiParameter, DrumSetup,
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InternalData
        {
            [FieldOffset(0)] public (Midi.CC Controller, int Value, int Channel) _controllerChange;
            [FieldOffset(0)] public (ChannelMidiParameter Param, int Channel) _channelMidiParam;
        }
        
        public Type MType { get; private init; }
        private InternalData Data { get; init; }

        public (Midi.CC Controller, int Value, int Channel)? AsControllerChange =>
            MType == Type.ControllerChange ? Data._controllerChange : null;
        public (ChannelMidiParameter Param, int Channel)? AsChannelMidiParameter =>
            MType == Type.ChannelMidiParameter ? Data._channelMidiParam : null;
        
        public static AnalyzedParameter Of(Type type)
        {
            ReadOnlySpan<Type> notAllowed = [
                Type.ControllerChange, Type.ChannelMidiParameter];
            return notAllowed.Contains(type) 
                ? throw new ArgumentException("Invalid argument: " + type) 
                : new AnalyzedParameter { MType = type };
        }
 
        public static AnalyzedParameter OfControllerChange(
            Midi.CC controller, int value, int channel) =>
            new()
            {
                MType = Type.ControllerChange, 
                Data = new InternalData
                    { _controllerChange = (controller, value, channel) },
            };

        public static AnalyzedParameter Of(
            ChannelMidiParameter parameter, int channel) =>
            new()
            {
                MType = Type.ChannelMidiParameter, 
                Data = new InternalData
                    { _channelMidiParam = (parameter, channel) },
            };
        
        public static implicit operator AnalyzedParameter(Type type) =>
            Of(type);
    }
    
    public readonly struct AnalyzedMessage
    {
        public enum Type : byte
        {
            AnalyzedParameter,
            ReverbParam, ChorusParam, DelayParam, VariationParam,
            InsertionParam,
            DrumsOn, ProgramChange,
            DisplayData,
            GlobalMidiParameter,
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InternalData
        {
            [FieldOffset(0)] public AnalyzedParameter _analyzedParameter;
            [FieldOffset(0)] public (int Channel, bool IsDrum) _drumsOn;
            [FieldOffset(0)] public (int Channel, int Value) _programChange;
            [FieldOffset(0)] public GlobalMidiParameter _globalMidiParam;
        }

        public Type MType { get; private init; }
        private InternalData Data { get; init; }

        public (int Channel, bool IsDrum)? AsDrumsOn =>
            MType == Type.DrumsOn ? Data._drumsOn : null;
        public AnalyzedParameter? AsAnalyzedParameter =>
            MType == Type.AnalyzedParameter ? Data._analyzedParameter : null;
        public (int Channel, int Value)? AsProgramChange =>
            MType == Type.ProgramChange ? Data._programChange : null;
        public GlobalMidiParameter? AsGlobalMidiParameter =>
            MType == Type.GlobalMidiParameter ? Data._globalMidiParam : null;

        public static AnalyzedMessage Of(Type type)
        {
            ReadOnlySpan<Type> notAllowed = [
                Type.DrumsOn, Type.ProgramChange, Type.AnalyzedParameter,
                Type.GlobalMidiParameter,];
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
        
        public static AnalyzedMessage Of(
            AnalyzedParameter analyzedParameter) =>
            new()
            {
                MType = Type.AnalyzedParameter, 
                Data = new InternalData { _analyzedParameter = analyzedParameter },
            };
        
        public static AnalyzedMessage OfProgramChange(
            int channel, int value) =>
            new()
            {
                MType = Type.ProgramChange, 
                Data = new InternalData { _programChange = (channel, value) },
            };
        
        public static AnalyzedMessage Of(GlobalMidiParameter parameter) =>
            new()
            {
                MType = Type.GlobalMidiParameter, 
                Data = new InternalData { _globalMidiParam = parameter },
            };

        public static implicit operator AnalyzedMessage(
            Type type) => Of(type);
        
        public static implicit operator AnalyzedMessage(
            AnalyzedParameter.Type type) => Of(AnalyzedParameter.Of(type));
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
            AnalyzedParameter.Type.Other;

        return syx[0] switch
        {
            // Non realtime GM
            // Realtime GM
            0x7e or 0x7f => AnalyzeGM(syx),
            // Roland
            0x41 => AnalyzeGS(syx),
            // Yamaha
            0x43 => AnalyzeXG(syx),
            _ => AnalyzedParameter.Type.Other
        };
    }
    
    /// <summary>
    /// Analyzes a MIDI Registered Parameter Number and returns an identification and data for it.
    /// </summary>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="rpn">The 14-bit RPN number.</param>
    /// <param name="value">The 14-bit value for that number.</param>
    /// <returns></returns>
    public static AnalyzedParameter AnalyzeRPN(
        int channel, int rpn, int value) =>
        rpn switch
        {
            ExtendedParameters.RPN.PitchWheelRange =>
                AnalyzedParameter.Of(
                    (ChannelMidiParameter.Type.PitchWheelRange,
                        value / 128f), channel),
            ExtendedParameters.RPN.FineTuning =>
                AnalyzedParameter.Of(
                    (ChannelMidiParameter.Type.FineTune,
                        (value - 8_192) / 81.92f), channel),
            ExtendedParameters.RPN.CoarseTuning =>
                AnalyzedParameter.Of(
                    (ChannelMidiParameter.Type.KeyShift,
                        (value >> 7) - 64), channel),
            ExtendedParameters.RPN.ModulationDepth =>
                AnalyzedParameter.Of(
                    (ChannelMidiParameter.Type.ModulationDepth,
                        // Cents, so data / 128 * 100 is data / 1.28
                        value / 1.28f), channel),
            _ => AnalyzedParameter.Type.Other,
        };
    
    /// <summary>
    /// Analyzes a MIDI Non-Registered Parameter Number and returns an identification and data for it.
    /// </summary>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="nrpn">The 14-bit NRPN number.</param>
    /// <param name="value">The 14-bit value for that number.</param>
    /// <returns></returns>
    public static AnalyzedParameter AnalyzeNRPN(int channel, int nrpn, int value)
    {
        var msb = nrpn >> 7;
        var lsb = nrpn & 0x7f;
        switch (msb) 
        {
            default: 
                return AnalyzedParameter.Type.Other;

            case ExtendedParameters.NRPN.MSB.PartParameter: 
            {
                switch (lsb) 
                {
                    default: 
                        return AnalyzedParameter.Type.Other;
                        
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
                    
                    AnalyzedParameter OfCC(Midi.CC cc) => 
                        AnalyzedParameter.OfControllerChange(cc, value >> 7, channel);
                }
            }

            case ExtendedParameters.NRPN.MSB.DrumPitch:
            case ExtendedParameters.NRPN.MSB.DrumPitchFine:
            case ExtendedParameters.NRPN.MSB.DrumLevel:
            case ExtendedParameters.NRPN.MSB.DrumPan:
            case ExtendedParameters.NRPN.MSB.DrumReverb:
            case ExtendedParameters.NRPN.MSB.DrumChorus:
            case ExtendedParameters.NRPN.MSB.DrumDelay:
                return AnalyzedParameter.Type.DrumSetup;
        }
    }

    /// <summary>
    /// Returns a list of MIDI events needed to set the given parameter.
    /// </summary>
    /// <param name="ticks">The ticks for all events.</param>
    /// <param name="system">If the message has multiple ways of setting it, this selects the preferred way. Otherwise, it prefers Universal (GM).</param>
    /// <param name="parameter">The parameter and value to set.</param>
    /// <returns>The list of <b>MIDIMessage</b>s that set the parameter.</returns>
    public static MidiMessage[] Set(
        int ticks, Midi.System system, GlobalMidiParameter parameter)
    {
        switch (parameter.PType)
        {
            case GlobalMidiParameter.Type.MidiSystem:
                // Well, we set the system so we don't care about the current one
                return [Reset(ticks, parameter.AsMidiSystem)];

            case GlobalMidiParameter.Type.KeyShift:
            {
                // Three ways of setting it: GM. XG and GS.
                return system switch
                {
                    Midi.System.XG =>
                        // Transpose
                        [XgMessage(ticks, 0x00, 0x00, 0x06,
                        [(byte)(parameter.AsInt + 64)])],
                    Midi.System.GS =>
                        // Master Key-Shift
                        [GsMessage(ticks, 0x40, 0x00, 0x05,
                        [(byte)(parameter.AsInt + 64)])],
                    _ =>
                        // GM2 and GM are the same here
                        // Master Coarse Tuning
                        [DeviceControlMessage(ticks, 0x04, [
                            0x00, // LSB is not used for key shift
                            (byte)(parameter.AsInt + 64)])],
                };
            }

            case GlobalMidiParameter.Type.FineTune:
            {
                // Again, all three systems have their own way of setting it, and they are all different
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (system)
                {
                    default:
                    {
                        // GM tunes in 14-bit numbers, how nice!
                        var tuneValue = (int)float.Floor(
                            parameter.AsFloat * 81.92f + 8_192);
                        return [DeviceControlMessage(ticks, 0x03, [
                                (byte)(tuneValue & 0x7f), // LSB
                                (byte)((tuneValue >> 7) & 0x7f) // MSB
                        ])];
                    }

                    case Midi.System.XG:
                    {
                        // -102.4 to 102.3, in 0.1 cent steps
                        // Real range is 0 to 2047 with 1024 as center
                        var tuneValue = (int)float.Floor(
                            parameter.AsFloat * 10 + 1_024);
                        return [XgMessage(ticks, 0x00, 0x00, 0x00, [
                                (byte)((tuneValue >> 12) & 0x0f),
                                (byte)((tuneValue >> 8) & 0x0f),
                                (byte)((tuneValue >> 4) & 0x0f),
                                (byte)(tuneValue & 0x0f),
                        ])];
                    }

                    case Midi.System.GS:
                    {
                        // Gs is -100 cents to 100 cents, 0.1 cent steps
                        // Real range is 24 to 2024, so narrower than XG
                        var tuneValue = (int)float.Floor(
                            parameter.AsFloat * 10 + 1_024);
                        return [GsMessage(ticks, 0x40, 0x00, 0x00, [
                                (byte)((tuneValue >> 12) & 0x0f),
                                (byte)((tuneValue >> 8) & 0x0f),
                                (byte)((tuneValue >> 4) & 0x0f),
                                (byte)(tuneValue & 0x0f),
                        ])];
                    }
                }
            }
                
            case GlobalMidiParameter.Type.Gain:
            {
                // All three once more!
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (system)
                {
                    default:
                    {
                        // MIDI Master Volume corresponds to CC volume, so the effective volume is squared.
                        // Reverse that here
                        var gainValue = (int)float.Floor(
                            float.Sqrt(parameter.AsFloat) * 16_383);
                        return [DeviceControlMessage(ticks, 0x01, [
                                (byte)(gainValue & 0x7f), // LSB
                                (byte)((gainValue >> 7) & 0x7f), // MSB
                        ])];
                    }
                    
                    case Midi.System.XG:
                    {
                        var gainValue = (int)float.Floor(parameter.AsFloat * 127);
                        return [XgMessage(ticks, 0x00, 0x00, 0x04, [
                                (byte)gainValue
                        ])];
                    }

                    case Midi.System.GS:
                    {
                        // GS
                        var gainValue = (int)float.Floor(parameter.AsFloat * 127);
                        return [GsMessage(ticks, 0x40, 0x00, 0x04, [
                                (byte)gainValue,
                        ])];
                    }
                }
            }
                
            case GlobalMidiParameter.Type.Pan:
            {
                // Only GM and GS, XG doesn't have a pan message?
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (system)
                {
                    default:
                    {
                        // Master Balance message
                        var balance = (int)float.Floor(
                            parameter.AsFloat * 8_192) + 8_192;

                        return [DeviceControlMessage(ticks, 0x02, [
                                (byte)(balance & 0x7f), // LSB
                                (byte)((balance >> 7) & 0x7f) // MSB
                        ])];
                    }

                    case Midi.System.GS:
                    {
                        // 63, it ranges from 1 to 127, NOT 0 to 127!
                        var balance = (int)float.Floor(parameter.AsFloat * 63) + 64;
                        return [GsMessage(ticks, 0x40, 0x00, 0x06, [
                                (byte)balance
                        ])];
                    }
                }
            }
            default: break;
        }
        
        throw new NotSupportedException(parameter.PType.ToString());
    }

    /// <summary>
    /// Returns a list of MIDI events needed to set the given parameter.
    /// </summary>
    /// <param name="ticks">The ticks for all events.</param>
    /// <param name="channel">The channel number.</param>
    /// <param name="system">If the message has multiple ways of setting it, this selects the preferred way. Otherwise, it prefers Universal (GM).</param>
    /// <param name="parameter">The parameter and value to set.</param>
    /// <returns>The list of <b>MIDIMessage</b>s that set the parameter.</returns>
    public static MidiMessage[] Set(
        int ticks, 
        int channel, 
        Midi.System system, 
        ChannelMidiParameter parameter)
    {
        channel %= 16;
        var gsChannel = SyxToChannel(channel);

        return parameter.PType switch
        {
            ChannelMidiParameter.Type.Pressure => 
                [MidiMessage.ChannelPressure(ticks, channel, parameter.AsInt)],
            ChannelMidiParameter.Type.PitchWheel => 
                [MidiMessage.PitchWheel(ticks, channel, parameter.AsInt)],
            ChannelMidiParameter.Type.PitchWheelRange => 
                MidiMessage.RegisteredParameter(
                    ticks, channel,
                    ExtendedParameters.RPN.PitchWheelRange, 
                    (int)float.Floor(parameter.AsFloat * 128)),
            ChannelMidiParameter.Type.ModulationDepth => 
                MidiMessage.RegisteredParameter(ticks, channel,
                    ExtendedParameters.RPN.ModulationDepth,
                    // Cents, so data / 128 * 100 is data / 1.28
                    (int)float.Floor(parameter.AsFloat * 1.28f)),
            ChannelMidiParameter.Type.RxChannel => 
                system == Midi.System.XG
                    ? [XgMessage(ticks, 0x08, channel, 0x04, 
                        [(byte)parameter.AsFloat])]
                    : [GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x02,
                        [(byte)parameter.AsFloat])],
            ChannelMidiParameter.Type.PolyMode => parameter.AsBool
                ? [MidiMessage.ControllerChange(ticks, channel, Midi.CC.PolyModeOn, 0)]
                : [MidiMessage.ControllerChange(ticks, channel, Midi.CC.MonoModeOn, 0)],
            ChannelMidiParameter.Type.KeyShift =>
                // Prefer RPN as it's universal
                MidiMessage.RegisteredParameter(
                    ticks, channel, ExtendedParameters.RPN.CoarseTuning,
                    (parameter.AsInt + 64) << 7),
            ChannelMidiParameter.Type.FineTune =>
                // Prefer RPN as it's universal
                MidiMessage.RegisteredParameter(
                    ticks, channel, ExtendedParameters.RPN.FineTuning,
                    // Resolution is 100/8192 cents
                    (int)float.Floor(parameter.AsFloat * 81.92f + 8_192)),
            ChannelMidiParameter.Type.RandomPan =>
                // Only set via SysEx in both GS and XG (value 0 means random pan)
                system == Midi.System.XG
                    ? [XgMessage(ticks, 0x08, channel, 0x0e, [0])]
                    : [GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x1c, [0])],
            ChannelMidiParameter.Type.AssignMode =>
                // GS only
                [
                    GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x14, 
                        [(byte)parameter.AsAssignMode])
                ],
            ChannelMidiParameter.Type.EfxAssign =>
                // GS only (again)
                [
                    GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x22, 
                        [(byte)(parameter.AsBool ? 1 : 0)])
                ],
            ChannelMidiParameter.Type.CC1 =>
                // GS only!!! (again!)
                [
                    GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x1f, 
                        [(byte)parameter.AsCC])
                ],
            ChannelMidiParameter.Type.CC2 =>
                // The same as cc1, just different address
                [
                    GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x20, 
                        [(byte)parameter.AsCC])
                ],
            ChannelMidiParameter.Type.DrumMap =>
                // GS only, it's called "USE FOR RHYTHM PART" there
                [
                    GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x15, 
                        [(byte)parameter.AsInt])
                ],
            ChannelMidiParameter.Type.VelocitySenseDepth => 
                system == Midi.System.XG
                ? [XgMessage(ticks, 0x08, channel, 0x0c, 
                    [(byte)parameter.AsInt])]
                : [GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x1a, 
                    [(byte)parameter.AsInt])],
            ChannelMidiParameter.Type.VelocitySenseOffset =>
                // Similar to above
                system == Midi.System.XG
                    ? [XgMessage(ticks, 0x08, channel, 0x0d, 
                        [(byte)parameter.AsInt])]
                    : [GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x1b, 
                        [(byte)parameter.AsInt])],
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// GS/XG "part number" to channel number.
    /// </summary>
    /// <param name="part"></param>
    public static int SyxToChannel(int part) =>
        ((ReadOnlySpan<int>)[
            9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15])[part % 16];

    /// <summary>
    /// Channel number to GS/XG "part number"
    /// </summary>
    /// <param name="chan"></param>
    public static int ChannelToSyx(int chan) =>
        ((ReadOnlySpan<int>)[
            1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 10, 11, 12, 13, 14, 15])[chan % 16];

    public const int GsDataMinLen = 9;

    public static byte[] Gs(int a1, int a2, int a3, byte data)
    {
        var dataArray = new byte[GsDataMinLen + 1];
        Gs(a1, a2, a3, [data], dataArray);
        return dataArray;
    }
    
    public static byte[] Gs(int a1, int a2, int a3, byte data1, byte data2)
    {
        var dataArray = new byte[GsDataMinLen + 2];
        Gs(a1, a2, a3, [data1, data2], dataArray);
        return dataArray;
    }

    /// <summary>Gets raw GS System Exclusive message, without the 0xF0 status byte.</summary>
    /// <param name="a1">Address 1</param>
    /// <param name="a2">Address 2</param>
    /// <param name="a3">Address 3</param>
    /// <param name="data">Data, can be multiple bytes.</param>
    /// <param name="result">The final output, whose len must be <b>GsDataMinLen</b> + <b>data.Length</b></param>
    /// <returns>The same result provided for convenience</returns>
    /// <exception cref="ArgumentException"></exception>
    public static ReadOnlySpan<byte> Gs(
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

        return result[.. (GsDataMinLen + data.Length)];
    }

    /// <summary>
    /// Turns raw SysEx bytes (without the 0xF0 status byte) into a <b>MIDIMessage</b>.
    /// </summary>
    /// <param name="ticks">The tick time of the message.</param>
    /// <param name="data">The data for the message, without the 0xF0 status byte.</param>
    /// <returns></returns>
    public static MidiMessage Syx(int ticks, ReadOnlySpan<byte> data) =>
        new(ticks, MidiMessage.Type.SystemExclusive, data.ToArray());
    
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
        var dataArray = (Span<byte>)stackalloc byte[GsDataMinLen + data.Length];
        return MidiMessage.SystemExclusive(
            ticks, Gs(a1, a2, a3, data, dataArray));
    }
    
    public const int XgDataMinLen = 7;
    
    /// <summary>
    /// Gets raw XG System Exclusive message bytes, without the 0xF0 status byte.
    /// </summary>
    /// <param name="a1">Address 1</param>
    /// <param name="a2">Address 2</param>
    /// <param name="a3">Address 3</param>
    /// <param name="data">Data, can be multiple bytes.</param>
    /// <returns></returns>
    public static Span<byte> Xg(
        int a1, int a2, int a3, ReadOnlySpan<byte> data, Span<byte> result)
    {
        if (result.Length < XgDataMinLen + data.Length)
            throw new ArgumentException(
                $"Expected result of at least {XgDataMinLen + data.Length
                } length, got {result.Length}");
        
        result[0] = 0x43; // Yamaha
        result[1] = 0x10; // Device ID (defaults to 16 on roland)
        result[2] = 0x4c; // XG
        result[3] = (byte)a1;
        result[4] = (byte)a2;
        result[5] = (byte)a3;
        data.CopyTo(result[6..]);
        result[6 + data.Length] = 0xf7; // End of exclusive

        return result[.. (XgDataMinLen + data.Length)];
    }

    /// <summary>Gets a XG System Exclusive MIDI message</summary>
    /// <param name="ticks">The tick time of the message.</param>
    /// <param name="a1">Address 1</param>
    /// <param name="a2">Address 2</param>
    /// <param name="a3">Address 3</param>
    /// <param name="data">Data, can be multiple bytes</param>
    /// <returns></returns>
    public static MidiMessage XgMessage(
        int ticks, int a1, int a2, int a3, ReadOnlySpan<byte> data)
    {
        var dataArray = (Span<byte>)stackalloc byte[XgDataMinLen + data.Length];
        return MidiMessage.SystemExclusive(
            ticks, Xg(a1, a2, a3, data, dataArray));
    }

    /// <summary>
    /// Gets a raw Device Control System Exclusive message bytes, without the 0xF0 status byte.
    /// </summary>
    /// <param name="subID">The sub ID.</param>
    /// <param name="data">Data, can be multiple bytes.</param>
    /// <returns></returns>
    public static byte[] DeviceControl(int subID, ReadOnlySpan<byte> data)
    {
        var result = new byte[5 + data.Length];

        result[0] = 0x7f; // Universal realtime
        result[1] = 0x7f; // Device ID (broadcast)
        result[2] = 0x04; // Device Control
        result[3] = (byte)subID;
        data.CopyTo(result.AsSpan()[4..]);
        result[4 + data.Length] = 0xf7; // End of exclusive

        return result;
    }

    /// <summary>
    /// Gets a Device Control System Exclusive MIDI message.
    /// </summary>
    /// <param name="ticks">The tick time of the message.</param>
    /// <param name="subID">The sub ID.</param>
    /// <param name="data">Data, can be multiple bytes.</param>
    /// <returns></returns>
    public static MidiMessage DeviceControlMessage(
            int ticks, int subID, ReadOnlySpan<byte> data) =>
        MidiMessage.SystemExclusive(
            ticks, DeviceControl(subID, data));

    /// <summary>
    /// Gets a selected reset System Exclusive MIDI message.
    /// </summary>
    /// <param name="ticks"></param>
    /// <param name="system">The system to reset into.</param>
    /// <returns></returns>
    public static MidiMessage Reset(int ticks, Midi.System system) =>
        system switch
        {
            Midi.System.GS => 
                GsMessage(
                    ticks,
                    0x40,       // System parameter - Address
                    0x00,       // Global mode parameter -  Address
                    0x7f,       // MODE SET - Address
                    [0x00]      // 00 = GS Reset - Data
                ),
            Midi.System.XG =>
                XgMessage(
                    ticks,
                    0x00,       // System parameter - Address
                    0x00,       // Global mode parameter -  Address
                    0x7e,       // XG On
                    [0x00]      // 00 = GS Reset - Data
                ),
            Midi.System.GM =>
                MidiMessage.SystemExclusive(ticks, [
                    0x7e, // Universal Non-Realtime
                    0x7f, // Broadcast
                    0x09, // General MIDI
                    0x01, // General MIDI 1 On
                    0x7f, // End of exclusive
                ]),
            Midi.System.GM2 =>
                MidiMessage.SystemExclusive(ticks, [
                    0x7e, // Universal Non-Realtime
                    0x7f, // Broadcast
                    0x09, // General MIDI
                    0x03, // General MIDI 2 On
                    0x7f, // End of exclusive
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(system), system, null)
        };

    private static AnalyzedMessage AnalyzeGM(ReadOnlySpan<byte> syx)
    {
        if (syx.Length < 4) 
            return AnalyzedParameter.Type.Other;

        if (syx[2] == 0x04) // Device control
        {
            switch (syx[3])
            {
                default:
                    return AnalyzedParameter.Type.Other;

                case 0x01:
                {
                    // Master Volume
                    var value = ((syx[5] << 7) | syx[4]) / 16_383f;
                    // It corresponds to CC volume, so volume is squared.
                    var gain = float.Pow(value, 2);
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Gain, gain));
                }
                
                case 0x02:
                {
                    // Master Balance
                    // Complete MIDI 1.0 Detailed Specification page 57
                    // This is not specified in GM2 spec for some reason
                    var balance = (syx[5] << 7) | syx[4];
                    var value = (balance - 8_192) / 8_192f;
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Pan, value));
                }

                case 0x03:
                {
                    // Master Fine-Tuning
                    var tuningValue = ((syx[5] << 7) | syx[4]) - 8_192;
                    var value = tuningValue / 81.92f; // [-100;+99] cents range
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.FineTune, value));
                }
                
                case 0x04:
                    // Master Coarse Tuning
                    return AnalyzedMessage.Of((
                        GlobalMidiParameter.Type.KeyShift, syx[5] - 64));

                case 0x05:
                {
                    // Global Parameter control
                    if (
                        syx[4] != 0x01 || // Slot Path Length
                        syx[5] != 0x01 || // Parameter ID Width
                        syx[6] != 0x01 || // Value Width
                        syx[7] != 0x01 // Slot Path MSB
                    ) return AnalyzedParameter.Type.Other;

                    // Slot Path LSB
                    switch (syx[8]) 
                    {
                        default: return AnalyzedParameter.Type.Other;

                        case 0x01:
                        {
                            // Reverb
                            // Parameter
                            return syx[9] switch
                            {
                                0x01 or 0x02 =>
                                    AnalyzedMessage.Type.ReverbParam,
                                _ => AnalyzedParameter.Type.Other
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
                                _ => AnalyzedParameter.Type.Other
                            };
                        }
                    }
                }
            }
        }
        
        if (syx[2] != 0x09)
            return AnalyzedParameter.Type.Other;

        return syx[3] switch
        {
            0x01 or
            0x02 => AnalyzedMessage.Of(Midi.System.GM),
            0x03 => AnalyzedMessage.Of(Midi.System.GM2),
            _ => AnalyzedParameter.Type.Other
        };
    }
    
    private static AnalyzedMessage AnalyzeXG(ReadOnlySpan<byte> syx)
    {
        // Ensure XG
        if (syx[2] != 0x4c || syx.Length < 7)
            return AnalyzedParameter.Type.Other;

        var a1 = syx[3]; // Address 1
        var a2 = syx[4]; // Address 2
        var a3 = syx[5]; // Address 3
        var data = syx[6];
        
        if (a1 == 0x06 ||   // Display letters
            a1 == 0x07)     // Display bitmap
            return AnalyzedMessage.Of(AnalyzedMessage.Type.DisplayData);

        if (a1 == 0x00 && a2 == 0x00)
        {
            // XG SYSTEM
            return a3 switch
            {
                0x00 =>
                    // MASTER TUNE
                    OfFineTune(syx),
                0x06 =>
                    // TRANSPOSE
                    AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.KeyShift, data - 64)),
                // XG SYSTEM ON
                0x7e or
                // ALL PARAMETER RESET
                0x7f => AnalyzedMessage.Of(Midi.System.XG),
                _ => AnalyzedParameter.Type.Other,
            };
            
            AnalyzedMessage OfFineTune(ReadOnlySpan<byte> syx)
            {
                var tune =
                    ((syx[6] & 15) << 12) |
                    ((syx[7] & 15) << 8) |
                    ((syx[8] & 15) << 4) |
                    (syx[9] & 15);
                var cents = (tune - 1_024) / 10f;
                return AnalyzedMessage.Of(
                    (GlobalMidiParameter.Type.FineTune, cents));
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
                return AnalyzedParameter.Type.Other;

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
                    AnalyzedMessage.Of(AnalyzedParameter.OfControllerChange(
                        data == 1 ? Midi.CC.PolyModeOn : Midi.CC.MonoModeOn, 0, channel)),
                0x07 =>
                    // Part mode
                    AnalyzedMessage.OfDrumsOn(channel, data > 0),
                0x08 =>
                    // Note shift
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.KeyShift, data - 64),
                        channel)),
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
                _ => AnalyzedParameter.Type.Other
            };

            AnalyzedMessage OfControllerChange(Midi.CC cc) =>
                AnalyzedMessage.Of(AnalyzedParameter.OfControllerChange(
                    cc, data, channel));
        }

        // Drum part setup
        if (a1 >> 4 == 3)
            return AnalyzedParameter.Type.DrumSetup;

        return AnalyzedParameter.Type.Other;
    }
    
    private static AnalyzedMessage AnalyzeGS(ReadOnlySpan<byte> syx)
    {
        if (syx.Length < 10 ||
            // 0x12: DT1 (Device Transmit)
            syx[3] != 0x12)
            return AnalyzedParameter.Type.Other; // Corrupted?
        
        if (
            // Model ID (Display Data)
            syx[2] == 0x45) return AnalyzedMessage.Type.DisplayData;

        if (
            // Model ID (GS)
            syx[2] != 0x42)
            return AnalyzedParameter.Type.Other;

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
                // Master Tune
                case 0x00:
                {
                    var tune =
                        (data << 12) | (syx[8] << 8) | (syx[9] << 4) | syx[10];
                    var cents = (tune - 1_024) / 10f;
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.FineTune, cents));
                }
                
                // Master Volume
                case 0x04:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Gain, data / 127f));

                // Master Key-Shift
                case 0x05:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.KeyShift, data - 64));
                
                // Master Pan
                case 0x06:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Pan,
                            // 63, it ranges from 1 to 127, NOT 0 to 127!
                            (data - 64) / 63f));
                
                // MODE SET
                case 0x7f:
                {
                    if (data == 0x00)
                        // GS Reset/Mode-1
                        return AnalyzedMessage.Of(Midi.System.GS);
                    if (data == 0x7f)
                        // GS Off, default to gm
                        return AnalyzedMessage.Of(Midi.System.GM);
                    return AnalyzedParameter.Type.Other;
                }

            }
        }

        if (a1 == 0x41) return AnalyzedParameter.Type.DrumSetup;
        if (a1 != 0x40) return AnalyzedParameter.Type.Other;
        
        if (a2 == 0x00 && a3 == 0x05)
            return AnalyzedMessage.Of(
                (GlobalMidiParameter.Type.KeyShift, data - 64));

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
            var channel = SyxToChannel(a2 & 0x0f);
            return a3 switch
            {
                0x00 =>
                    // Tone number
                    AnalyzedMessage.OfProgramChange(channel, data),
                0x13 =>
                    // Mono/poly
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.PolyMode, data == 1), channel)),
                0x14 =>
                    // Assign mode
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        (MidiChannel.Assign)data, channel)),
                0x15 => AnalyzedMessage.OfDrumsOn(channel, data > 0),
                0x16 => AnalyzedMessage.Of(AnalyzedParameter.Of(
                    (ChannelMidiParameter.Type.KeyShift, data - 64), channel)),
                0x19 =>
                    // Part level (cc#7)
                    OfControllerChange(Midi.CC.MainVolume),
                0x1c =>
                    // Pan position
                    OfControllerChange(Midi.CC.Pan),
                0x1f =>
                    // CC1 Controller number
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        new ChannelMidiParameter(
                        ChannelMidiParameter.Type.CC1, (Midi.CC)data), 
                        channel)),
                0x20 =>
                    // CC2 Controller number
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        new ChannelMidiParameter(
                        ChannelMidiParameter.Type.CC2, (Midi.CC)data), 
                        channel)),
                0x21 =>
                    // Chorus send
                    OfControllerChange(Midi.CC.ChorusDepth),
                0x22 =>
                    // Reverb send
                    OfControllerChange(Midi.CC.ReverbDepth),
                0x2a =>
                    // Fine tune
                    AnalyzedMessage.Of(
                        AnalyzedParameter.Of(
                        // 0-16384
                        (ChannelMidiParameter.Type.FineTune,
                        (((data << 7) | syx[8]) - 8_192) / 81.92f),
                        channel)),
                    
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
                _ => AnalyzedParameter.Type.Other
            };

            AnalyzedMessage OfControllerChange(Midi.CC cc) =>
                AnalyzedMessage.Of(AnalyzedParameter.OfControllerChange(
                    cc, data, channel));
        }

        // Patch Parameter Tone Map
        if (a2 >> 4 == 4)
        {
            var channel = SyxToChannel(a2 & 0x0f);
            return a3 switch
            {
                0x00 or
                0x01 =>
                    // Tone map number (cc#32)
                    AnalyzedMessage.Of(AnalyzedParameter.OfControllerChange(
                        Midi.CC.BankSelectLSB, data, channel)),
                0x22 => 
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.EfxAssign, data == 1),
                        channel)),
                _ => AnalyzedParameter.Type.Other
            };
        }

        return AnalyzedParameter.Type.Other;
    }
}