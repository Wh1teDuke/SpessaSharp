using SpessaSharp.Synthesizer.Engine.Channel;
using SpessaSharp.Synthesizer.Engine.Channel.Parameters;
using SpessaSharp.Synthesizer.Engine.Effects;
using SpessaSharp.Synthesizer.Engine.Parameters;
using SpessaSharp.Utils;

namespace SpessaSharp.MIDI.Utils;

/// <summary> A general purpose class for handling MIDI messages. </summary>
public static class MidiUtils
{
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

            // Drum data entries are analyzed as 7-bit coarse values
            case ExtendedParameters.NRPN.MSB.DrumPitch:
                return DrumSetup(DrumParameter.Type.PitchCoarse, (value >> 7) - 64);
            case ExtendedParameters.NRPN.MSB.DrumPitchFine:
                return DrumSetup(DrumParameter.Type.PitchFine, (value >> 7) - 64);
            case ExtendedParameters.NRPN.MSB.DrumLevel:
                return DrumSetup(DrumParameter.Type.Level, value >> 7);
            case ExtendedParameters.NRPN.MSB.DrumPan:
                return DrumSetup(DrumParameter.Type.Pan, value >> 7);
            case ExtendedParameters.NRPN.MSB.DrumReverb:
                return DrumSetup(DrumParameter.Type.ReverbSend, value >> 7);
            case ExtendedParameters.NRPN.MSB.DrumChorus:
                return DrumSetup(DrumParameter.Type.ChorusSend, value >> 7);
            case ExtendedParameters.NRPN.MSB.DrumVariation:
                return DrumSetup(DrumParameter.Type.VariationSend, value >> 7);

            AnalyzedParameter DrumSetup(DrumParameter.Type type, float val) =>
                AnalyzedParameter.Of(
                    new ChannelDrumSetupMessage(channel, lsb, type, val));
        }
    }

    /// <summary>
    /// Returns a MIDI event needed to set the given GS Reverb Parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="parameter">The parameter to set.</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this GS Reverb Parameter.</returns>
    public static MidiMessage SetGSReverbParameter(
        int ticks, Effect.GSReverbType parameter, int value)
    {
        ReadOnlySpan<byte> gsReverbAddressMap = 
            [0x31, 0x32, 0x33, 0x34, 0x35, 0x37,];
        if (!Util.InRange(gsReverbAddressMap, (int)parameter))
            throw new Exception($"Invalid reverb parameter: {parameter}");
        var a3 = gsReverbAddressMap[(int)parameter];
        return GsMessage(ticks, 0x40, 0x01, a3, [(byte)value]);
    }
    
    /// <summary>
    /// Returns a MIDI event needed to set the given GS Chorus Parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="parameter">The parameter to set.</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this GS Chorus Parameter.</returns>
    public static MidiMessage SetGSChorusParameter(
        int ticks, Effect.GSChorusType parameter, int value)
    {
        ReadOnlySpan<byte> gsChorusAddressMap = 
            [0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40];
        if (!Util.InRange(gsChorusAddressMap, (int)parameter))
            throw new Exception($"Invalid chorus parameter: {parameter}");
        var a3 = gsChorusAddressMap[(int)parameter];
        return GsMessage(ticks, 0x40, 0x01, a3, [(byte)value]);
    }
    
    /// <summary>
    /// Returns a MIDI event needed to set the given GS Delay Parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="parameter">The parameter to set.</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this GS Delay Parameter.</returns>
    /// <exception cref="Exception"></exception>
    public static MidiMessage SetGSDelayParameter(
        int ticks, Effect.GSDelayType parameter, int value)
    {
        ReadOnlySpan<byte> gsDelayParameter = 
            [0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a];
        if (!Util.InRange(gsDelayParameter, (int)parameter))
            throw new Exception($"Invalid delay parameter: {parameter}");
        var a3 = gsDelayParameter[(int)parameter];
        return GsMessage(ticks, 0x40, 0x01, a3, [(byte)value]);
    }

    /// <summary>
    /// Returns a MIDI event needed to set the given GS Insertion Parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="parameter">The parameter to set: <c>type</c> or a send level name</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this GS Insertion Parameter.</returns>
    public static MidiMessage SetInsertionParameter(
        int ticks, Effect.InsertionType parameter, int value)
    {
        return parameter switch
        {
            Effect.InsertionType.Type => GsMessage(ticks, 0x40, 0x03, 0x00,
                [(byte)((value >> 8) & 0x7f), (byte)(value & 0x7f)]),
            Effect.InsertionType.SendLevelToReverb => GsMessage(ticks, 0x40, 0x03, 0x17, [(byte)value]),
            Effect.InsertionType.SendLevelToChorus => GsMessage(ticks, 0x40, 0x03, 0x18, [(byte)value]),
            Effect.InsertionType.SendLevelToDelay => GsMessage(ticks, 0x40, 0x03, 0x19, [(byte)value]),
            _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null)
        };
    }
    
    /// <summary>
    /// Returns a MIDI event needed to set the given GS Insertion Parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="parameter">The parameter to set: a 0-based effect-specific parameter number (0-19).</param>
    /// <param name="value">The value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this GS Insertion Parameter.</returns>
    public static MidiMessage SetInsertionParameter(
        int ticks, int parameter, int value) =>
        parameter is < 0 or > 19
            ? throw new Exception($"Invalid insertion parameter: {parameter}")
            : GsMessage(ticks, 0x40, 0x03, parameter + 3, [(byte)value]);

    /// <summary>
    /// Returns a list of MIDI events needed to set the given parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for output events.</param>
    /// <param name="system">If the message has multiple ways of setting it, this selects the preferred way. Otherwise, it prefers Universal (GM).</param>
    /// <param name="parameter">The parameter and value to set.</param>
    /// <returns>The list of <b>MIDIMessage</b>s that set the parameter.</returns>
    public static MidiMessage[] Set(
        int ticks, Midi.System? system, GlobalMidiParameter parameter)
    {
        switch (parameter.PType)
        {
            case GlobalMidiParameter.Type.System:
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
                
            case GlobalMidiParameter.Type.Volume:
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
                            parameter.AsFloat * 16_383);
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
    /// <param name="ticks">The MIDI tick time for output events.</param>
    /// <param name="channel">The channel number.</param>
    /// <param name="system">If the message has multiple ways of setting it, this selects the preferred way. Otherwise, it prefers Universal (GM).</param>
    /// <param name="parameter">The parameter and value to set.</param>
    /// <returns>The list of <b>MIDIMessage</b>s that set the parameter.</returns>
    public static MidiMessage[] Set(
        int ticks, 
        int channel, 
        Midi.System? system, 
        ChannelMidiParameter parameter)
    {
        channel %= 16;
        var gsChannel = ChannelToSyx(channel);

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
                        [(byte)parameter.AsInt])]
                    : [GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x02,
                        [(byte)parameter.AsInt])],
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
                [system == Midi.System.XG
                    ? XgMessage(ticks, 0x08, channel, 0x0e, [0])
                    : GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x1c, [0])],
            ChannelMidiParameter.Type.AssignMode =>
                // XG/GS only
                [system == Midi.System.XG
                    ? XgMessage(ticks, 0x08, channel, 0x06,
                        [(byte)parameter.AsAssignMode])
                    : GsMessage(ticks, 0x40, 0x10 | gsChannel, 0x14,
                        [(byte)parameter.AsAssignMode])],
            ChannelMidiParameter.Type.EfxAssign =>
                // GS only (again)
                [
                    GsMessage(ticks, 0x40, 0x40 | gsChannel, 0x22, 
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
            
            // That's it!
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Returns a MIDI event needed to set the given drum map parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for the output event.</param>
    /// <param name="map">The GS drum map/XG drum setup number.</param>
    /// <param name="system">ystem The system to prepare the message for. Any value other than <c>xg</c> will result in a GS-style message.</param>
    /// <param name="key">The MIDI drum key/note number to modify.</param>
    /// <param name="param">The parameter and value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> needed to set this drum map parameter.</returns>
    public static MidiMessage SetDrumMapParameter(
        int ticks,
        int map,
        Midi.System? system,
        int key,
        DrumParameter.Entry param)
    {
        if (system == Midi.System.XG)
        {
            if (SysexData.XGDrumParamMap(param.Type) is not {} a3Param)
                throw new Exception($"Invalid XG drum parameter {param.Type}");

            var midiValue = param.Type switch
            {
                DrumParameter.Type.RxNoteOff or 
                DrumParameter.Type.RxNoteOn => param.ToInt(),
                DrumParameter.Type.PitchFine or 
                DrumParameter.Type.PitchCoarse => param.AsInt + 64,
                _ => param.AsInt
            };
            
            var a1 = 0x30 | (map - SysexData.DEFAULT_XG_DRUM_MAP);
            return XgMessage(ticks, a1, key, a3Param, [(byte)midiValue]);
        }
        
        if (param.Type == DrumParameter.Type.PitchFine ||
            SysexData.GSDrumParamMap(param.Type) is not {} a2Param)
            throw new Exception($"Invalid XG drum parameter {param.Type}");
        
        // PLAY NOTE is relative to 60, while pitchCoarse is relative to 0
        {
            var midiValue =
                param.Type == DrumParameter.Type.PitchCoarse
                    ? 60 + param.ToInt()
                    : param.ToInt();

            // 0 = MAP1
            var a2 = ((map - SysexData.DEFAULT_GS_DRUM_MAP) << 4) | a2Param;
            return GsMessage(ticks, 0x41, a2, key, [(byte)midiValue]);            
        }
    }

    /// <summary>
    /// Returns a list of MIDI events needed to set the given channel drum parameter via NRPN.
    /// </summary>
    /// <param name="ticks">The MIDI tick time of the events.</param>
    /// <param name="channel">The MIDI channel number.</param>
    /// <param name="key">The MIDI drum key/note number to modify.</param>
    /// <param name="param">The parameter and value to set it to.</param>
    /// <returns>The list of <see cref="MidiMessage"/>s needed to set this channel drum parameter.</returns>
    public static MidiMessage[] SetDrumChannelParameter(
        int ticks,
        int channel,
        int key,
        DrumParameter.Entry param)
    {
        if (key is > 127 or < 0)
            throw new Exception("Key must be between 0 and 127.");

        var msb = 0;
        var coarse = 0;

        switch (param.Type)
        {
            case DrumParameter.Type.PitchCoarse:
                msb = ExtendedParameters.NRPN.MSB.DrumPitch;
                coarse = param.AsInt + 64;
                break;
            case DrumParameter.Type.PitchFine:
                msb = ExtendedParameters.NRPN.MSB.DrumPitchFine;
                coarse = param.AsInt + 64;
                break;
            case DrumParameter.Type.Level:
                msb = ExtendedParameters.NRPN.MSB.DrumLevel;
                coarse = param.AsInt;
                break;
            case DrumParameter.Type.Pan:
                msb = ExtendedParameters.NRPN.MSB.DrumPan;
                coarse = param.AsInt;
                break;
            case DrumParameter.Type.ReverbSend:
                msb = ExtendedParameters.NRPN.MSB.DrumReverb;
                coarse = param.AsInt;
                break;
            case DrumParameter.Type.ChorusSend:
                msb = ExtendedParameters.NRPN.MSB.DrumChorus;
                coarse = param.AsInt;
                break;
            case DrumParameter.Type.VariationSend:
                msb = ExtendedParameters.NRPN.MSB.DrumVariation;
                coarse = param.AsInt;
                break;

            case DrumParameter.Type.AssignGroup:
            case DrumParameter.Type.RxNoteOn:
            case DrumParameter.Type.RxNoteOff:
            default:
                throw new Exception($"Invalid NRPN drum parameter {param.Type}");
        }
        
        return MidiMessage.NonRegisteredParameter(
            ticks, channel, (msb << 7) | key, coarse << 7);
    }

    /// <summary>
    /// Returns a  MIDI event needed to set the given GS User Drum Set parameter.
    /// </summary>
    /// <param name="ticks">The MIDI tick time for output events.</param>
    /// <param name="drumSet">The drum set to modify, either 0 or 1.</param>
    /// <param name="midiNote">The MIDI note number of the drum key to modify.</param>
    /// <param name="parameter">The parameter to set and value to set it to.</param>
    /// <returns>The <see cref="MidiMessage"/> that sets the parameter.</returns>
    public static MidiMessage SetUserDrumParameter(
        int ticks, int drumSet, int midiNote, 
        UserDrumSetParameter.Entry parameter)
    {
        // PLAY NOTE is relative to 60 and not 0, but pitchCoarse is relative to 0
        var midiValue = (byte)parameter.ToInt();
        if (parameter is
            {
                Type: UserDrumSetParameter.Type.DrumParameters,
                AsDrumParameter.Type: DrumParameter.Type.PitchCoarse,
            })
            midiValue += 60;
        
        drumSet %= 2;
        var a2Param = SysexData.GsuSerDrumParamMap(parameter);
        var a2 = (drumSet << 4) | a2Param;
        return GsMessage(
            ticks, 0x21, a2, midiNote, [midiValue,]);
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
    
    public static byte[] Gs(int a1, int a2, int a3, ReadOnlySpan<byte> data)
    {
        var dataArray = new byte[GsDataMinLen + data.Length];
        Gs(a1, a2, a3, data, dataArray);
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
    
    public static byte[] Xg(int a1, int a2, int a3, ReadOnlySpan<byte> data)
    {
        var dataArray = new byte[XgDataMinLen + data.Length];
        Xg(a1, a2, a3, data, dataArray);
        return dataArray;
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

    public readonly ref struct AnalyzedMessageEnumerable(
        ArraySegment<AnalyzedMessage>? messages,
        AnalyzedMessage? single)
    {
        public AnalyzedMessageEnumerator GetEnumerator() =>
            new(messages, single);

        public static AnalyzedMessageEnumerable Of(AnalyzedMessage single) =>
            new (null, single);
        
        public static AnalyzedMessageEnumerable Of(
                ArraySegment<AnalyzedMessage> messages) =>
            new (messages, null);

        public static implicit operator AnalyzedMessageEnumerable(
            AnalyzedMessage msg) => Of(msg);
        
        public static implicit operator AnalyzedMessageEnumerable(
            ArraySegment<AnalyzedMessage> messages) => Of(messages);
        
        public static implicit operator AnalyzedMessageEnumerable(
            AnalyzedParameter msg) => Of(msg);
        
        public static implicit operator AnalyzedMessageEnumerable(
            AnalyzedParameter.Type msg) => Of(msg);
        
        public static implicit operator AnalyzedMessageEnumerable(
            AnalyzedMessage.Type msg) => Of(msg);
    }
    
    public ref struct AnalyzedMessageEnumerator(
        ArraySegment<AnalyzedMessage>? messages,
        AnalyzedMessage? single): IDisposable
    {
        private int _index = -1;
        public AnalyzedMessage Current =>
            single
            ?? messages?[_index]
            ?? throw new InvalidOperationException();

        public bool MoveNext() => 
            ++_index < (single != null ? 1 : messages?.Count);

        public void Dispose()
        {
            if (messages is {} msgs)
                Util.Return(msgs);
        }
    }
    
    /// <summary>
    /// Analyzes a MIDI System Exclusive message and returns an identification and data for it.
    /// Note that bulk dump and other sysExes are supported so this method may return more than one result.
    /// </summary>
    /// <remarks>The messages returned exclude <c>ChannelDrumSetupMessage</c></remarks>
    /// <param name="e">The message to analyze</param>
    /// <returns></returns>
    public static AnalyzedMessageEnumerable AnalyzeSysEx(MidiMessage e) =>
        AnalyzeSysEx(e.Data);

    /// <summary>
    /// Analyzes a MIDI System Exclusive message and returns an identification and data for it.
    /// Note that bulk dump and other sysExes are supported so this method may return more than one result.
    /// </summary>
    /// <remarks>The messages returned exclude <c>ChannelDrumSetupMessage</c></remarks>
    /// <param name="syx">The System Exclusive message, WITHOUT the first 0xF0 System Exclusive byte!</param>
    /// <returns></returns>
    public static AnalyzedMessageEnumerable AnalyzeSysEx(ReadOnlySpan<byte> syx)
    {
        // At least Manufacturer ID, Device ID and XG/GS model ID
        if (syx.Length < 3) return AnalyzedParameter.Type.Other;

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

    private static AnalyzedMessageEnumerable AnalyzeGM(ReadOnlySpan<byte> syx)
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
                        (GlobalMidiParameter.Type.Volume, gain));
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
                    int? value = syx.Length > 10 ? syx[10] : null;
                    return syx[8] switch
                    {
                        0x01 =>
                            // Reverb
                            /*var value = syx[10];*/
                            // Parameter
                            (syx[9] switch
                            {
                                // Reverb type
                                // Match 8850 manual, page 231
                                // All match except for plate which is 8 in GM and 5 in GS
                                0x00 => AnalyzedMessage.Of(
                                    Effect.GSReverbType.Macro, value == 0x08 ? 0x05 : value!.Value),
                                // Reverb time
                                0x01 => AnalyzedMessage.Of(
                                    Effect.GSReverbType.Time, value!.Value),
                                _ => AnalyzedParameter.Type.Other
                            }),
                        0x02 =>
                            // Chorus
                            // Parameter
                            (syx[9] switch
                            {
                                0x00 => 
                                    // Chorus type
                                    // Match 8850 manual, page 231
                                    // All match
                                    AnalyzedMessage.Of(
                                        Effect.GSChorusType.Macro, value!.Value),
                                0x01 =>
                                    // Mod rate
                                    AnalyzedMessage.Of(
                                        Effect.GSChorusType.Rate, value!.Value),
                                0x02 =>
                                    // Mod depth
                                    AnalyzedMessage.Of(
                                        Effect.GSChorusType.Depth, value!.Value),
                                0x03 =>
                                    // Mod feedback
                                    AnalyzedMessage.Of(
                                        Effect.GSChorusType.Feedback, value!.Value),
                                0x04 =>
                                    // Mod send to reverb
                                    AnalyzedMessage.Of(
                                        Effect.GSChorusType.SendLevelToReverb, value!.Value),
                                _ => AnalyzedParameter.Type.Other
                            }),
                        _ => AnalyzedParameter.Type.Other
                    };
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
    
    private static AnalyzedMessageEnumerable AnalyzeXG(ReadOnlySpan<byte> syx)
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
            return AnalyzedMessage.Type.DisplayData;

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
                <= 0x15 => AnalyzedMessage.Type.XGReverbParam,
                <= 0x35 => AnalyzedMessage.Type.XGChorusParam,
                _ => AnalyzedMessage.Type.XGVariationParam
            };
        
        // XG EFFECT 2
        if (a1 == 0x03 && a2 == 0x00)
            return AnalyzedMessage.Type.XGVariationParam;

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
                0x06 =>
                    // Same Note Number Key On Assign
                    AnalyzedMessage.Of(AnalyzedParameter.Of(
                        data == 0 
                            ? MidiChannel.Assign.Single 
                            : MidiChannel.Assign.FullMulti, channel)),
                0x07 =>
                    // Part mode
                    AnalyzedParameter.Of(
                        ChannelMidiParameter.DrumMap(data), channel),
                0x08 =>
                    // Note shift
                    AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.KeyShift, data - 64),
                        channel),
                0x0b =>
                    // Volume
                    OfControllerChange(Midi.CC.MainVolume),
                0x0e =>
                    // Pan, except for random,
                    // Which is a different parameter
                    data == 0
                        ? AnalyzedParameter.Of(
                            (ChannelMidiParameter.Type.RandomPan, true),
                            channel)
                        : OfControllerChange(Midi.CC.Pan),
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
                
                0x20 =>
                    // MW LFO PMOD Depth (alias to modulation wheel range)
                    AnalyzedParameter.Of(ChannelMidiParameter.ModulationDepth(
                        ((data - 63) / 127f) * 600), channel),
                
                0x23 =>
                    // Bend pitch control (alias to pitch wheel range)
                    AnalyzedParameter.Of(ChannelMidiParameter.PitchWheelRange(
                        /*centeredValue =*/data - 64), channel),
                
                _ => AnalyzedParameter.Type.Other
            };

            AnalyzedMessage OfControllerChange(Midi.CC cc) =>
                AnalyzedParameter.OfControllerChange(cc, data, channel);
        }

        // Drum part setup
        if (a1 >> 4 == 3)
        {
            var drumMap = (a1 & 0xf) + SysexData.DEFAULT_XG_DRUM_MAP;
            return a3 switch
            {
                // Pitch coarse
                0x00 => DrumSetup((DrumParameter.Type.PitchCoarse, data - 64)),
                // Pitch fine
                0x01 => DrumSetup((DrumParameter.Type.PitchFine, data - 64)),
                // Level
                0x02 => DrumSetup((DrumParameter.Type.Level, data)),
                // Assign Group
                0x03 => DrumSetup((DrumParameter.Type.AssignGroup, data)),
                // Pan
                0x04 => DrumSetup((DrumParameter.Type.Pan, data)),
                // Reverb Send
                0x05 => DrumSetup((DrumParameter.Type.ReverbSend, data)),
                // Chorus Send
                0x06 => DrumSetup((DrumParameter.Type.ChorusSend, data)),
                // Variation Send
                0x07 => DrumSetup((DrumParameter.Type.VariationSend, data)),
                // Rev Note Off
                0x09 => DrumSetup((DrumParameter.Type.RxNoteOff, data == 1)),
                // Rev Note On
                0x0a => DrumSetup((DrumParameter.Type.RxNoteOn, data == 1)),

                _ => AnalyzedParameter.Type.Other,
            };

            AnalyzedMessage DrumSetup(DrumParameter.Entry param) =>
                new MapDrumSetupMessage(drumMap, a2, param);
        }

        return AnalyzedParameter.Type.Other;
    }
    
    private static AnalyzedMessageEnumerable AnalyzeGS(ReadOnlySpan<byte> syx)
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
        // Data = syx[7]
        var value = syx[7];

        // System Parameters
        // MODE SET
        // This has been separated from 40 00 because 00 00 05 was erroneously
        // Decoded as "master key shift" even though it means "SC-88 output assign"
        // Testcase: FADED88.mid
        if (a1 == 0x00 && a2 == 0x00 && a3 == 0x7f)
        {
            return value switch
            {
                // GS Reset/Mode-1 (Single Module Mode)
                // GS Reset/Mode-2 (Double Module Mode)
                0x00 or 0x01 => AnalyzedMessage.Of(Midi.System.GS),
                0x7f =>
                    // GS Off, default to gm
                    AnalyzedMessage.Of(Midi.System.GM),
                _ => AnalyzedParameter.Type.Other
            };
        }
        
        // Patch common parameters
        if (a1 == 0x40 && a2 == 0x00)// System Parameter
        {
            switch (a3)
            {
                // Master Tune
                case 0x00:
                {
                    var tune =
                        (value << 12) | (syx[8] << 8) | (syx[9] << 4) | syx[10];
                    var cents = (tune - 1_024) / 10f;
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.FineTune, cents));
                }
                
                // Master Volume
                case 0x04:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Volume, value / 127f));

                // Master Key-Shift
                case 0x05:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.KeyShift, value - 64));
                
                // Master Pan
                case 0x06:
                    return AnalyzedMessage.Of(
                        (GlobalMidiParameter.Type.Pan,
                            // 63, it ranges from 1 to 127, NOT 0 to 127!
                            (value - 64) / 63f));
                
                // MODE SET
                case 0x7f:
                {
                    if (value is 
                        0x00 or // GS Reset/Mode-1  
                        0x01)   // GS Reset/Mode-2 (Double Module Mode)
                        return AnalyzedMessage.Of(Midi.System.GS);
                    if (value == 0x7f)
                        // GS Off, default to gm
                        return AnalyzedMessage.Of(Midi.System.GM);
                    return AnalyzedParameter.Type.Other;
                }

            }
        }

        // Drum Setup
        if (a1 is 0x41 or 0x51)
        {
            var drumMap = (a2 >> 4) + SysexData.DEFAULT_GS_DRUM_MAP;
            return (a2 & 0xf) switch
            {
                // Play Note Number (Pitch Coarse)
                0x1 => DrumSetup((DrumParameter.Type.PitchCoarse, value - 60)),
                // Level
                0x2 => DrumSetup((DrumParameter.Type.Level, value)),
                // Assign Group
                0x3 => DrumSetup((DrumParameter.Type.AssignGroup, value)),
                // Pan
                0x4 => DrumSetup((DrumParameter.Type.Pan, value)),
                // Reverb Send
                0x5 => DrumSetup((DrumParameter.Type.ReverbSend, value)),
                // Chorus Send
                0x6 => DrumSetup((DrumParameter.Type.ChorusSend, value)),
                // Rx. Note Off
                0x7 => DrumSetup((DrumParameter.Type.RxNoteOff, value == 1)),
                // Rx. Note On
                0x8 => DrumSetup((DrumParameter.Type.RxNoteOn, value == 1)),
                // Delay Send Level
                0x9 => DrumSetup((DrumParameter.Type.VariationSend, value)),

                _ => AnalyzedParameter.Type.Other,
            };
            
            AnalyzedMessage DrumSetup(DrumParameter.Entry param) =>
                new MapDrumSetupMessage(drumMap, a3, param);
        }
        
        // User Drum Set
        if (a1 == 0x21)
            return HandleSingleUserDrum(a2, a3, value);
        
        // User Drum Set Bulk Dump
        if (a1 == 0x29)
        {
            var dataLength = syx.Length - 9;
            // See the corresponding code in synth sysEx handler for comments

            var actualDrumParam = 0;
            switch (a2 & 0x0f)
            {
                default:
                    return AnalyzedParameter.Type.Other;
                
                case 0x0:
                    actualDrumParam = 1;
                    break;
                case 0x1:
                    actualDrumParam = 2;
                    break;
                case 0x2:
                    actualDrumParam = 3;
                    break;
                case 0x3:
                    actualDrumParam = 4;
                    break;
                case 0x4:
                    actualDrumParam = 5;
                    break;
                case 0x5:
                    actualDrumParam = 6;
                    break;
                case 0x6:
                {
                    var address2Off = (a2 & 0xf0) | 7;
                    var address2On = (a2 & 0xf0) | 8;
                    var analyzed = Util.Rent<AnalyzedMessage>(
                        dataLength * 2);
                    for (var midiNote = 0; midiNote < dataLength; midiNote++) 
                    {
                        analyzed[midiNote * 2 + 0] =
                            HandleSingleUserDrum(
                                address2Off,
                                midiNote,
                                syx[midiNote + 7] & 0xf);
                        analyzed[midiNote * 2 + 1] =
                            HandleSingleUserDrum(
                                address2On,
                                midiNote,
                                syx[midiNote + 7] >> 4);
                    }
                    return analyzed;
                }
                case 0x7:
                    actualDrumParam = 9;
                    break;
                case 0x8:
                    actualDrumParam = 0xa;
                    break;
                case 0x9:
                    actualDrumParam = 0xb;
                    break;
                case 0xa:
                    actualDrumParam = 0xc;
                    break;
                case 0xb:
                    actualDrumParam = 0;
                    break;
            }
            {
                var address2 = (a2 & 0xf0) | actualDrumParam;
                var analyzed = Util.Rent<AnalyzedMessage>(
                    dataLength);
                for (var midiNote = 0; midiNote < dataLength; midiNote++) 
                {
                    analyzed[midiNote * 2 + 0] =
                        HandleSingleUserDrum(
                            address2,
                            midiNote,
                            syx[midiNote + 7]);
                }

                return analyzed;
            }
        }

        // 0x40 -> Part Parameters, 0x50 -> Part Parameters (BLOCK B) Testcase: 95043-2.KYC.mid
        if (a1 is not 0x40 and not 0x50) return AnalyzedParameter.Type.Other;

        // Block B is the second 16-channel set
        var channelOffset = a1 == 0x50 ? 16 : 0;
        
        // Effects
        if (a2 == 0x01)
        {
            return a3 switch
            {
                0x30 => AnalyzedMessage.Of(Effect.GSReverbType.Macro, value),
                0x31 => AnalyzedMessage.Of(Effect.GSReverbType.Character, value),
                0x32 => AnalyzedMessage.Of(Effect.GSReverbType.PreLowPass, value),
                0x33 => AnalyzedMessage.Of(Effect.GSReverbType.Level, value),
                0x34 => AnalyzedMessage.Of(Effect.GSReverbType.Time, value),
                0x35 => AnalyzedMessage.Of(Effect.GSReverbType.DelayFeedback, value),
                // 0x36 is intentionally gone as it was reverb send to chorus in SC-55
                0x37 => AnalyzedMessage.Of(Effect.GSReverbType.PreDelayTime, value),
                0x38 => AnalyzedMessage.Of(Effect.GSChorusType.Macro, value),
                0x39 => AnalyzedMessage.Of(Effect.GSChorusType.PreLowPass, value),
                0x3a => AnalyzedMessage.Of(Effect.GSChorusType.Level, value),
                0x3b => AnalyzedMessage.Of(Effect.GSChorusType.Feedback, value),
                0x3c => AnalyzedMessage.Of(Effect.GSChorusType.Delay, value),
                0x3d => AnalyzedMessage.Of(Effect.GSChorusType.Rate, value),
                0x3e => AnalyzedMessage.Of(Effect.GSChorusType.Depth, value),
                0x3f => AnalyzedMessage.Of(Effect.GSChorusType.SendLevelToReverb, value),
                0x40 => AnalyzedMessage.Of(Effect.GSChorusType.SendLevelToDelay, value),
                0x50 => AnalyzedMessage.Of(Effect.GSDelayType.Macro, value),
                0x51 => AnalyzedMessage.Of(Effect.GSDelayType.PreLowPass, value),
                0x52 => AnalyzedMessage.Of(Effect.GSDelayType.TimeCenter, value),
                0x53 => AnalyzedMessage.Of(Effect.GSDelayType.TimeRatioLeft, value),
                0x54 => AnalyzedMessage.Of(Effect.GSDelayType.TimeRatioRight, value),
                0x55 => AnalyzedMessage.Of(Effect.GSDelayType.LevelCenter, value),
                0x56 => AnalyzedMessage.Of(Effect.GSDelayType.LevelLeft, value),
                0x57 => AnalyzedMessage.Of(Effect.GSDelayType.LevelRight, value),
                0x58 => AnalyzedMessage.Of(Effect.GSDelayType.Level, value),
                0x59 => AnalyzedMessage.Of(Effect.GSDelayType.Feedback, value),
                0x5a => AnalyzedMessage.Of(Effect.GSDelayType.SendLevelToReverb, value),
                _ => AnalyzedParameter.Type.Other
            };
        }

        // EFX Parameter
        if (a2 == 0x03)
        {
            switch (a3)
            {
                case 0x00: 
                    return AnalyzedMessage.Of(
                        Effect.InsertionType.Type, (value << 8) | syx[8]);
                case 0x17:
                    return AnalyzedMessage.Of(
                        Effect.InsertionType.SendLevelToReverb, value);
                case 0x18:
                    return AnalyzedMessage.Of(
                        Effect.InsertionType.SendLevelToChorus, value);
                case 0x19:
                    return AnalyzedMessage.Of(
                        Effect.InsertionType.SendLevelToDelay, value);
            }
            
            if (a3 is >= 0x03 and <= 0x16)
                return AnalyzedMessage.OfInsertionParameter(a3 - 3, value);

            return AnalyzedParameter.Type.Other;
        }

        switch (a2 >> 4)
        {
            // Patch part parameter
            case 1:
            {
                var channel = SyxToChannel(a2 & 0x0f) + channelOffset;
                return a3 switch
                {
                    0x00 =>
                        // Tone number
                        Util.Rent([
                            AnalyzedParameter.OfControllerChange(
                                Midi.CC.BankSelect, value, channel),
                            AnalyzedMessage.OfProgramChange(channel, syx[8]),
                        ]),
                    0x13 =>
                        // Mono/poly
                        AnalyzedParameter.Of(
                            (ChannelMidiParameter.Type.PolyMode, value == 1), channel),
                    0x14 =>
                        // Assign mode
                        AnalyzedParameter.Of((MidiChannel.Assign)value, channel),
                    0x15 => AnalyzedParameter.Of(
                        ChannelMidiParameter.DrumMap(value), channel),
                    0x16 => AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.KeyShift, value - 64), channel),
                    0x19 =>
                        // Part level (cc#7)
                        OfControllerChange(Midi.CC.MainVolume),
                    0x1a =>
                        // Velocity Sense Depth
                        AnalyzedParameter.Of(
                            (ChannelMidiParameter.Type.VelocitySenseDepth,
                                value), channel),
                    0x1b =>
                        // Velocity Sense Offset
                        AnalyzedParameter.Of(
                            (ChannelMidiParameter.Type.VelocitySenseOffset,
                                value), channel),
                    0x1c =>
                        // Pan position, except for random,
                        // Which is a different parameter
                        value == 0
                            ? AnalyzedParameter.Of(
                                (ChannelMidiParameter.Type.RandomPan,
                                    true), channel)
                            : OfControllerChange(Midi.CC.Pan),
                    0x1f =>
                        // CC1 Controller number
                        AnalyzedParameter.Of(
                            new ChannelMidiParameter(
                                ChannelMidiParameter.Type.CC1, (Midi.CC)value),
                            channel),
                    0x20 =>
                        // CC2 Controller number
                        AnalyzedParameter.Of(
                            new ChannelMidiParameter(
                                ChannelMidiParameter.Type.CC2, (Midi.CC)value),
                            channel),
                    0x21 =>
                        // Chorus send
                        OfControllerChange(Midi.CC.ChorusDepth),
                    0x22 =>
                        // Reverb send
                        OfControllerChange(Midi.CC.ReverbDepth),
                    0x2a =>
                        // Fine tune
                        AnalyzedParameter.Of(
                            // 0-16384
                            (ChannelMidiParameter.Type.FineTune,
                                /*tuneCents =*/(/*tune =*/((value << 7) | syx[8]) - 8_192) / 81.92f),
                            channel),
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
                    AnalyzedParameter.OfControllerChange(cc, value, channel);
            }

            // Patch Part Parameters (Controllers)
            case 2:
            {
                var channel = SyxToChannel(a2 & 0x0f) + channelOffset;

                return a3 switch
                {
                    // LFO1 Pitch depth
                    // Special case:
                    // If the source is a mod wheel, it's a strange way of setting the modulation depth
                    // Testcase: J-Cycle.mid (it affects gm.dls which uses LFO1 for modulation)
                    0x04 => AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.ModulationDepth,
                            /*cents = */(value / 127f) * 600), channel),
                    // See https://github.com/spessasus/SpessaSynth/issues/154
                    // Pitch control
                    // Special case:
                    // If the source is a pitch wheel, it's a strange way of setting the pitch wheel range
                    // Testcase: th07_03.mid
                    0x10 => AnalyzedParameter.Of(
                        (ChannelMidiParameter.Type.PitchWheelRange,
                            /*centeredValue = */value - 64), channel),

                    _ => AnalyzedParameter.Type.Other,
                };
            }

            // Patch Parameter Tone Map
            case 4:
            {
                var channel = SyxToChannel(a2 & 0x0f) + channelOffset;
                return a3 switch
                {
                    0x00 or
                    0x01 =>
                        // Tone map number (cc#32)
                        AnalyzedParameter.OfControllerChange(
                            Midi.CC.BankSelectLSB, value, channel),
                    0x22 => 
                        AnalyzedParameter.Of(
                            (ChannelMidiParameter.Type.EfxAssign, value == 1),
                            channel),
                    _ => AnalyzedParameter.Type.Other
                };
            }
        }

        return AnalyzedParameter.Type.Other;
    }

    private static AnalyzedMessage HandleSingleUserDrum(
        int a2, int a3, int data)
    {
        var drumSet = a2 >> 4;
        return (a2 & 0xf) switch
        {
            // Play Note
            0x1 => DrumSetup2(DrumParameter.Type.PitchCoarse, data - 60),
            // Level
            0x2 => DrumSetup1(DrumParameter.Type.Level, data),
            // Assign group
            0x3 => DrumSetup1(DrumParameter.Type.AssignGroup, data),
            // Pan
            0x4 => DrumSetup1(DrumParameter.Type.Pan, data),
            // Reverb Send
            0x5 => DrumSetup1(DrumParameter.Type.ReverbSend, data),
            // Chorus Send
            0x6 => DrumSetup1(DrumParameter.Type.ChorusSend, data),
            // Rx. Note Off
            0x7 => DrumSetup3(DrumParameter.Type.RxNoteOff, data == 1),
            // Rx. Note On
            0x8 => DrumSetup3(DrumParameter.Type.RxNoteOn, data == 1),
            // Delay Send Level
            0x9 => DrumSetup1(DrumParameter.Type.VariationSend, data),
            // Source Drum Set Map
            0xa => DrumSetup4(UserDrumSetParameter.Type.SourceDrumSet, data),
            // Program Number
            0xb => DrumSetup4(UserDrumSetParameter.Type.Program, data),
            // Source Note Number
            0xc => DrumSetup4(UserDrumSetParameter.Type.SourceNoteNumber, data),

            _ => AnalyzedParameter.Type.Other,
        };
        
        AnalyzedMessage DrumSetup1(DrumParameter.Type type, int val) =>
            AnalyzedMessage.Of(a3, drumSet, (type, val));
        AnalyzedMessage DrumSetup2(DrumParameter.Type type, float val) =>
            AnalyzedMessage.Of(a3, drumSet, (type, val));
        AnalyzedMessage DrumSetup3(DrumParameter.Type type, bool val) =>
            AnalyzedMessage.Of(a3, drumSet, (type, val));
        AnalyzedMessage DrumSetup4(UserDrumSetParameter.Type type, int val) =>
            AnalyzedMessage.Of(a3, drumSet, (type, val));
    }
}